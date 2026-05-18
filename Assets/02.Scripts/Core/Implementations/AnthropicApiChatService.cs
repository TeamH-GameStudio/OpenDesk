using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.SkillDiskette.Models;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// IAiChatService — Anthropic Messages API HTTP 직접 호출 백엔드.
    ///
    /// CLI 백엔드 대비 장점:
    ///   - 외부 프로세스/포트 불필요 (Python 미들웨어 없음)
    ///   - 직접 SSE 스트리밍 → 더 낮은 지연
    ///   - 빌드 배포 시 의존성 단순
    ///
    /// 단점:
    ///   - MCP/외부 도구 미지원 (별도 구현 필요)
    ///   - tool_use 미지원 (현 구현)
    ///
    /// 키 소스: IApiKeyVaultService.GetKeyAsync("anthropic")
    /// 모델 디폴트: claude-sonnet-4-5 (PlayerPrefs `OpenDesk_AnthropicModel`로 변경 가능)
    /// </summary>
    [Obsolete("Use MiddlewareChatService — API 백엔드도 이제 통합 미들웨어 게이트웨이를 경유한다 (anthropic_api provider). 가역 보존용으로만 남아있다.")]
    public class AnthropicApiChatService : IAiChatService, IDisposable
    {
        private const string ApiUrl          = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion      = "2023-06-01";
        private const string DefaultModel    = "claude-sonnet-4-5";
        private const int    DefaultMaxTok   = 4096;

        private readonly IApiKeyVaultService _vault;
        private readonly HttpClient          _http;

        private string _systemPrompt = "";
        private readonly List<ApiMessage> _history = new();
        private CancellationTokenSource _activeCts;
        private bool _connected;
        private bool _disposed;

        // ── 이벤트 ──

        public event Action<string>         OnDelta;
        public event Action<string, float>  OnFinal;
        public event Action<string>         OnError;
        public event Action<string>         OnStatus;
        public event Action<bool, string>   OnConnectionChanged;
        public event Action                 OnCleared;

        // 신규 인터랙티브/lifecycle 이벤트 — legacy API 직결은 미들웨어 도구를 안 거치므로 NoOp.
        public event Action<OpenDesk.Claude.Models.ToolUserAskMessage>       OnToolUserAsk;
        public event Action<OpenDesk.Claude.Models.SubAgentSpawnedMessage>   OnSubAgentSpawned;
        public event Action<OpenDesk.Claude.Models.SubAgentCompletedMessage> OnSubAgentCompleted;
        public event Action<OpenDesk.Claude.Models.SubAgentFailedMessage>    OnSubAgentFailed;
        public event Action<OpenDesk.Claude.Models.TaskStateMessage>         OnTaskState;
        public event Action<OpenDesk.Claude.Models.CronStateMessage>         OnCronState;

        private void _SilenceUnusedEvents()
        {
            _ = OnToolUserAsk; _ = OnSubAgentSpawned; _ = OnSubAgentCompleted;
            _ = OnSubAgentFailed; _ = OnTaskState; _ = OnCronState;
        }

        public void SendToolUserResponse(string toolUseId, string response, string[] selected) { /* NoOp — legacy */ }
        public void SendToolUserResponse(string toolUseId, string response, string[] selected, bool remember) { /* NoOp — legacy */ }
        public void SendPluginRegistry(string agentId, OpenDesk.Claude.Models.PluginRegistryEntry[] entries) { /* NoOp — legacy */ }
        public void SendTaskControl(string action, string taskId) { /* NoOp — legacy */ }

        public bool IsConnected => _connected;
        public bool IsAuthenticated => _connected;

        [Inject]
        public AnthropicApiChatService(IApiKeyVaultService vault)
        {
            _vault = vault;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5),
            };
            _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);

            // 키 보유 여부로 즉시 connected 시그널
            CheckKeyAsync().Forget();
        }

        private async UniTaskVoid CheckKeyAsync()
        {
            try
            {
                var key = await _vault.GetKeyAsync("anthropic");
                _connected = !string.IsNullOrEmpty(key);
                var model = PlayerPrefs.GetString("OpenDesk_AnthropicModel", DefaultModel);
                OnConnectionChanged?.Invoke(_connected, model);
                if (!_connected)
                    OnError?.Invoke("Anthropic API 키 미설정. ApiKeyVault에서 'anthropic' 키를 저장하세요.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ApiChat] 키 로드 실패: {e.Message}");
            }
        }

        // ── 기본 통신 ──

        public void SetSystemPrompt(string systemPrompt)
        {
            _systemPrompt = systemPrompt ?? "";
            Debug.Log($"[ApiChat] System prompt 설정 ({_systemPrompt.Length}자)");
        }

        // [Obsolete] — 신규 흐름은 MiddlewareChatService.SetModel 사용. 호출 무시.
        public void SetModel(string model) { }

        public void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _history.Add(new ApiMessage { role = "user", content = message });

            _activeCts?.Cancel();
            _activeCts = new CancellationTokenSource();
            StreamRequestAsync(_history, _systemPrompt, persistAssistant: true, _activeCts.Token).Forget();
        }

        public void ResumeSession(string conversationJson)
        {
            // CLI 백엔드와 동일한 포맷이라 가정 — { messages: [{role, content}, ...] }
            // 단순히 _history를 교체. 파싱 실패 시 무시.
            try
            {
                _history.Clear();
                if (string.IsNullOrEmpty(conversationJson)) return;
                var wrap = JsonUtility.FromJson<HistoryWrapper>(conversationJson);
                if (wrap?.messages != null) _history.AddRange(wrap.messages);
                Debug.Log($"[ApiChat] 세션 재개: {_history.Count}개 메시지 복원");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ApiChat] 세션 재개 실패: {e.Message}");
            }
        }

        public void ClearHistory()
        {
            _history.Clear();
            OnCleared?.Invoke();
            Debug.Log("[ApiChat] 히스토리 초기화");
        }

        public void Abort()
        {
            try { _activeCts?.Cancel(); }
            catch (Exception e) { Debug.LogWarning($"[ApiChat] Abort 실패: {e.Message}"); }
            Debug.Log("[ApiChat] Abort 호출 — 활성 스트림 취소");
        }

        public void SendMcpConfig(string mcpConfigJson)
        {
            // API 백엔드는 MCP 미지원. 추후 Anthropic MCP support 추가 시 구현.
            Debug.LogWarning("[ApiChat] MCP는 CLI 백엔드에서만 지원. 무시됨.");
        }

        public void SendSkillLoadout(OpenDesk.Core.Models.Skills.SkillLoadoutPayload payload)
        {
            // [Obsolete] — 신규 흐름은 MiddlewareChatService 가 처리. 호출되더라도 no-op.
        }

        // ── 크래프팅 ──

        public async UniTask<CraftResult> CraftDisketteAsync(
            string naturalLanguagePrompt, CancellationToken ct)
        {
            if (!_connected)
                throw new InvalidOperationException("Anthropic API 키 미설정");

            var metaPrompt = BuildCraftPrompt(naturalLanguagePrompt);
            var oneShot = new List<ApiMessage>
            {
                new() { role = "user", content = metaPrompt },
            };

            var response = await StreamRequestAsync(
                oneShot, systemPrompt: "", persistAssistant: false, ct);

            var craftResult = ParseCraftResult(response);
            if (craftResult == null || !craftResult.IsValid)
            {
                Debug.LogWarning($"[ApiChat] 크래프팅 파싱 실패. 응답: {response[..Math.Min(response.Length, 200)]}");
                return null;
            }

            Debug.Log($"[ApiChat] 크래프팅 성공: {craftResult.skillName}");
            return craftResult;
        }

        // ── 핵심: SSE 스트리밍 ──

        /// <summary>
        /// Anthropic Messages API에 stream=true로 POST → SSE 라인별 파싱 → OnDelta/OnFinal 발화.
        /// 반환값: 누적된 전체 텍스트.
        /// </summary>
        private async UniTask<string> StreamRequestAsync(
            List<ApiMessage> messages,
            string systemPrompt,
            bool persistAssistant,
            CancellationToken ct)
        {
            var apiKey = await _vault.GetKeyAsync("anthropic", ct);
            if (string.IsNullOrEmpty(apiKey))
            {
                _connected = false;
                OnConnectionChanged?.Invoke(false, "");
                var msg = "Anthropic API 키 미설정";
                OnError?.Invoke(msg);
                return "";
            }

            var model = PlayerPrefs.GetString("OpenDesk_AnthropicModel", DefaultModel);
            var maxTokens = PlayerPrefs.GetInt("OpenDesk_AnthropicMaxTokens", DefaultMaxTok);

            var body = BuildRequestJson(model, maxTokens, systemPrompt, messages);

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("x-api-key", apiKey);

            OnStatus?.Invoke("Thinking");

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception e)
            {
                OnError?.Invoke($"네트워크 오류: {e.Message}");
                return "";
            }

            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync();
                OnError?.Invoke($"API 오류 {(int)res.StatusCode}: {errBody[..Math.Min(errBody.Length, 300)]}");
                return "";
            }

            var accumulated = new StringBuilder();
            float estimatedCost = 0f;

            try
            {
                using var stream = await res.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data:")) continue;

                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;

                    var deltaText = ExtractDeltaText(data);
                    if (!string.IsNullOrEmpty(deltaText))
                    {
                        accumulated.Append(deltaText);
                        // UI 스레드 안전: NativeWebSocket과 동일한 패턴 — 메인 스레드에서 처리
                        await UniTask.SwitchToMainThread(ct);
                        OnDelta?.Invoke(deltaText);
                    }

                    var usageCost = ExtractUsageCost(data, model);
                    if (usageCost > 0) estimatedCost += usageCost;
                }
            }
            catch (OperationCanceledException) { /* 정상 취소 */ }
            catch (Exception e)
            {
                OnError?.Invoke($"스트리밍 오류: {e.Message}");
            }

            var fullText = accumulated.ToString();

            await UniTask.SwitchToMainThread(ct);

            if (persistAssistant && !string.IsNullOrEmpty(fullText))
                _history.Add(new ApiMessage { role = "assistant", content = fullText });

            OnFinal?.Invoke(fullText, estimatedCost);
            OnStatus?.Invoke("Idle");

            return fullText;
        }

        // ── JSON 빌더/파서 ──

        private static string BuildRequestJson(
            string model, int maxTokens, string systemPrompt, List<ApiMessage> messages)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{Escape(model)}\",");
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append("\"stream\":true,");
            if (!string.IsNullOrEmpty(systemPrompt))
                sb.Append($"\"system\":\"{Escape(systemPrompt)}\",");
            sb.Append("\"messages\":[");
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = messages[i];
                sb.Append($"{{\"role\":\"{m.role}\",\"content\":\"{Escape(m.content)}\"}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// SSE data 라인에서 content_block_delta의 text 추출.
        /// 형식 예: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hi"}}
        /// </summary>
        private static string ExtractDeltaText(string json)
        {
            // text_delta가 아닌 라인은 무시 (message_start, content_block_start 등)
            if (!json.Contains("\"type\":\"content_block_delta\"")) return null;

            // "text":"..." 추출 — JSON 문자열 이스케이프 고려
            var match = Regex.Match(json, "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return null;

            return Unescape(match.Groups[1].Value);
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\') { sb.Append(s[i]); continue; }
                if (i + 1 >= s.Length) break;
                var next = s[++i];
                switch (next)
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                         System.Globalization.CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(next); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>message_delta의 usage.output_tokens 기반 비용 추정 (Sonnet 기준 $3/$15 per MTok)</summary>
        private static float ExtractUsageCost(string json, string model)
        {
            if (!json.Contains("\"usage\"")) return 0f;
            var match = Regex.Match(json, "\"output_tokens\"\\s*:\\s*(\\d+)");
            if (!match.Success) return 0f;
            if (!int.TryParse(match.Groups[1].Value, out var tokens)) return 0f;

            // Sonnet 4.5 출력 가격: $15 / 1M tokens. Opus는 $75. 단순 추정.
            var perMillion = model.Contains("opus") ? 75f : 15f;
            return tokens * perMillion / 1_000_000f;
        }

        // ── 크래프팅 유틸 ──

        private static string BuildCraftPrompt(string userPrompt) =>
$@"다음 요청을 분석하여 AI 에이전트 스킬을 정의해주세요.
반드시 아래 JSON 형식으로만 응답하세요. JSON 외 다른 텍스트는 출력하지 마세요.

{{
  ""skillName"": ""스킬 이름 (한글, 간결하게)"",
  ""description"": ""스킬 설명 (한 줄)"",
  ""promptContent"": ""에이전트가 이 스킬을 수행할 때 따를 상세 지시사항 (3~10줄)"",
  ""category"": ""General|Development|Document|Analysis|ExternalTool""
}}

사용자 요청: {userPrompt}";

        private static CraftResult ParseCraftResult(string response)
        {
            try
            {
                var json = ExtractJson(response);
                if (string.IsNullOrEmpty(json)) return null;
                return JsonUtility.FromJson<CraftResult>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ApiChat] JSON 파싱 실패: {e.Message}");
                return null;
            }
        }

        private static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var cleaned = Regex.Replace(text, @"<[^>]+>", "");
            cleaned = Regex.Replace(cleaned, @"---\s*\w+\s*---", "");
            var braces = Regex.Match(cleaned, @"\{[\s\S]*\}");
            if (!braces.Success) return null;
            return Regex.Replace(braces.Value.Trim(), @"[\r\n]+\s*", "\n");
        }

        // ── DTO ──

        [Serializable]
        private class ApiMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class HistoryWrapper
        {
            public List<ApiMessage> messages;
        }

        // ── Dispose ──

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _http?.Dispose();
        }
    }
}
