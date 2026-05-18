using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Auth;
using OpenDesk.SkillDiskette.Models;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// IAiChatService 의 유일한 구현체.
    /// Python 미들웨어(통합 AI 게이트웨이)로 모든 provider(anthropic_cli/anthropic_api/openai/gemini...) 호출을 위임한다.
    ///
    /// provider 선택은 PlayerPrefs `OpenDesk_ChatBackend` ("anthropic_cli" 기본, "anthropic_api", ...).
    /// 연결 직후 set_provider 메시지로 활성 provider 를 미들웨어에 통보.
    /// MCP/Plugin 도구는 SendMcpConfig 로 한 번 전달하면 이후 채팅에서 자동 적용.
    /// </summary>
    public class MiddlewareChatService : IAiChatService, IDisposable
    {
        public const string PlayerPrefsBackendKey = "OpenDesk_ChatBackend";
        public const string PlayerPrefsModelKey = "OpenDesk_AnthropicModel";
        // 2026-05-15: 기본을 anthropic_api 로 전환. API key 없어도 `claude login` OAuth 로 동작.
        // CLI 백엔드는 in-process 도구(ask_user/spawn_agent/task_*/cron_*/edit_file) 호출 불가로 deprecated.
        public const string DefaultProvider = "anthropic_api";

        private readonly ClaudeWebSocketClient _client;
        private readonly IAnthropicCredentialService _credentials;
        private readonly IAgentTelemetryService _telemetry;
        private bool _disposed;
        private string _activeProvider;
        private string _model;
        // 매 메시지마다 ChatPanel 이 동일 프롬프트로 SetSystemPrompt 를 재호출하는 경우가 많다.
        // 동일 prompt+model 이면 미들웨어 set_config 라운드트립을 스킵하여 불필요한 캐시 무효화를 막는다.
        private string _lastSentSystemPrompt;
        private string _lastSentModel;

        public bool IsConnected => _client != null && _client.IsConnected;
        public string ActiveProvider => _activeProvider;

        /// <summary>API Key 또는 OAuth 토큰 중 하나라도 있으면 true. UI 가드용.</summary>
        public bool IsAuthenticated => _credentials == null || _credentials.IsAuthenticated;

        // ── IAiChatService 이벤트 ──

        public event Action<string> OnDelta;
        public event Action<string, float> OnFinal;
        public event Action<string> OnError;
        public event Action<string> OnStatus;
        public event Action<bool, string> OnConnectionChanged;
        public event Action OnCleared;
        public event Action<ToolUserAskMessage> OnToolUserAsk;
        public event Action<SubAgentSpawnedMessage> OnSubAgentSpawned;
        public event Action<SubAgentCompletedMessage> OnSubAgentCompleted;
        public event Action<SubAgentFailedMessage> OnSubAgentFailed;
        public event Action<TaskStateMessage> OnTaskState;
        public event Action<CronStateMessage> OnCronState;

        [Inject]
        public MiddlewareChatService(
            ClaudeWebSocketClient client,
            IAnthropicCredentialService credentials = null,
            IAgentTelemetryService telemetry = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _credentials = credentials;
            _telemetry = telemetry;
            _activeProvider = ReadProviderPreference();
            _model = ReadModelPreference();

            _client.OnDelta += HandleDelta;
            _client.OnFinal += HandleFinal;
            _client.OnError += HandleError;
            _client.OnStatus += HandleStatus;
            _client.OnConnectionChanged += HandleConnectionChanged;
            _client.OnCleared += HandleCleared;
            _client.OnToolUserAsk += HandleToolUserAsk;
            _client.OnSubAgentSpawned += HandleSubAgentSpawned;
            _client.OnSubAgentCompleted += HandleSubAgentCompleted;
            _client.OnSubAgentFailed += HandleSubAgentFailed;
            _client.OnTaskState += HandleTaskState;
            _client.OnCronState += HandleCronState;
            // hook chain 의 telemetry 이벤트를 IAgentTelemetryService 로 forward.
            if (_telemetry != null)
            {
                _client.OnTelemetry += _telemetry.Ingest;
            }
        }

        // ── 기본 통신 ──

        public void SetSystemPrompt(string systemPrompt)
        {
            if (_client == null || !_client.IsConnected) return;
            var nextPrompt = systemPrompt ?? string.Empty;
            var nextModel = _model ?? string.Empty;
            if (nextPrompt == (_lastSentSystemPrompt ?? string.Empty)
                && nextModel == (_lastSentModel ?? string.Empty))
            {
                return; // 동일 — 재전송 스킵.
            }
            _client.SendConfig(systemPrompt, _model);
            _lastSentSystemPrompt = nextPrompt;
            _lastSentModel = nextModel;
            Debug.Log($"[MiddlewareChat] system prompt + model='{_model}' 설정 ({nextPrompt.Length}자)");
        }

        public void SetModel(string model)
        {
            var normalized = model ?? string.Empty;
            if (normalized == (_model ?? string.Empty)) return;

            _model = normalized;
            PlayerPrefs.SetString(PlayerPrefsModelKey, normalized);
            PlayerPrefs.Save();

            // SetSystemPrompt 캐시 무효화 — 다음 ApplySystemPrompt 호출 시 새 model 로
            // SendConfig 가 재전송되도록 한다. 연결 상태면 즉시 한번 갱신해 둔다.
            _lastSentModel = null;
            if (_client != null && _client.IsConnected)
            {
                var promptToResend = _lastSentSystemPrompt ?? string.Empty;
                _client.SendConfig(promptToResend, _model);
                _lastSentModel = normalized;
            }

            Debug.Log($"[MiddlewareChat] model 변경 → '{_model}'");
        }

        public void SendMessage(string message) => _client.SendChat(message);

        public void ResumeSession(string conversationJson)
        {
            // resume 은 미들웨어 측 system_prompt 를 자체적으로 덮어쓰므로 캐시 무효화.
            _lastSentSystemPrompt = null;
            _client.SendResume(conversationJson);
        }

        public void ClearHistory()
        {
            _lastSentSystemPrompt = null;
            _client.SendClear();
        }

        public void Abort()
        {
            if (_client == null) return;
            _client.SendCancel();
        }

        public void SendToolUserResponse(string toolUseId, string response, string[] selected)
        {
            if (_client == null) return;
            _client.SendToolUserResponse(toolUseId, response, selected);
        }

        public void SendToolUserResponse(string toolUseId, string response, string[] selected, bool remember)
        {
            if (_client == null) return;
            _client.SendToolUserResponse(toolUseId, response, selected, remember);
        }

        public void SendTaskControl(string action, string taskId)
        {
            if (_client == null) return;
            _client.SendTaskControl(action, taskId);
        }

        public void SendPluginRegistry(string agentId, PluginRegistryEntry[] entries)
        {
            if (_client == null) return;
            _client.SendPluginRegistry(agentId, entries);
        }

        public void SendMcpConfig(string mcpConfigJson)
        {
            if (_client == null || !_client.IsConnected) return;
            McpConfigPayload payload = null;
            if (!string.IsNullOrEmpty(mcpConfigJson))
            {
                try
                {
                    payload = JsonUtility.FromJson<McpConfigPayload>(mcpConfigJson);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MiddlewareChat] McpConfigPayload 파싱 실패: {ex.Message}");
                    return;
                }
            }
            _client.SendSetMcpConfig(payload);
        }

        public void SendMcpConfig(McpConfigPayload payload)
        {
            if (_client == null || !_client.IsConnected) return;
            _client.SendSetMcpConfig(payload);
        }

        public void SendSkillLoadout(SkillLoadoutPayload payload)
        {
            if (_client == null || !_client.IsConnected) return;
            _client.SendSetSkillLoadout(payload);
        }

        /// <summary>provider 동적 전환 (UI 토글 등에서 사용).</summary>
        public void SetProvider(string provider)
        {
            if (string.IsNullOrEmpty(provider) || provider == _activeProvider) return;
            _activeProvider = provider;
            PlayerPrefs.SetString(PlayerPrefsBackendKey, provider);
            PlayerPrefs.Save();
            if (_client != null && _client.IsConnected)
                _client.SendSetProvider(provider);
        }

        // ── 크래프팅 ──

        public async UniTask<CraftResult> CraftDisketteAsync(
            string naturalLanguagePrompt, CancellationToken ct)
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("미들웨어 서버에 연결되지 않았습니다");

            var metaPrompt = BuildCraftPrompt(naturalLanguagePrompt);
            var tcs = new UniTaskCompletionSource<string>();
            var accumulated = new StringBuilder();

            void OnDeltaHandler(string text) => accumulated.Append(text);
            void OnFinalHandler(string text, float cost)
            {
                var result = string.IsNullOrEmpty(text) ? accumulated.ToString() : text;
                tcs.TrySetResult(result);
            }
            void OnErrorHandler(string err)
                => tcs.TrySetException(new Exception($"크래프팅 실패: {err}"));

            _client.OnDelta += OnDeltaHandler;
            _client.OnFinal += OnFinalHandler;
            _client.OnError += OnErrorHandler;

            try
            {
                _client.SendChat(metaPrompt);
                var response = await tcs.Task.AttachExternalCancellation(ct);
                var craftResult = ParseCraftResult(response);
                if (craftResult == null || !craftResult.IsValid)
                {
                    Debug.LogWarning($"[MiddlewareChat] 크래프팅 파싱 실패. 응답: {response[..Math.Min(response.Length, 200)]}");
                    return null;
                }
                return craftResult;
            }
            finally
            {
                _client.OnDelta -= OnDeltaHandler;
                _client.OnFinal -= OnFinalHandler;
                _client.OnError -= OnErrorHandler;
            }
        }

        // ── 이벤트 핸들러 (중계) ──

        private void HandleDelta(string text) => OnDelta?.Invoke(text);
        private void HandleFinal(string text, float cost) => OnFinal?.Invoke(text, cost);
        private void HandleError(string err) => OnError?.Invoke(err);
        private void HandleStatus(string status) => OnStatus?.Invoke(status);
        private void HandleCleared() => OnCleared?.Invoke();
        private void HandleToolUserAsk(ToolUserAskMessage m) => OnToolUserAsk?.Invoke(m);
        private void HandleSubAgentSpawned(SubAgentSpawnedMessage m) => OnSubAgentSpawned?.Invoke(m);
        private void HandleSubAgentCompleted(SubAgentCompletedMessage m) => OnSubAgentCompleted?.Invoke(m);
        private void HandleSubAgentFailed(SubAgentFailedMessage m) => OnSubAgentFailed?.Invoke(m);
        private void HandleTaskState(TaskStateMessage m) => OnTaskState?.Invoke(m);
        private void HandleCronState(CronStateMessage m) => OnCronState?.Invoke(m);

        private void HandleConnectionChanged(bool connected, string model)
        {
            // 연결 상태가 바뀌면 미들웨어 측 세션이 새로 만들어졌을 수 있으므로 캐시 무효화.
            _lastSentSystemPrompt = null;
            _lastSentModel = null;

            // 연결 직후 활성 provider 와 모델을 미들웨어에 통보.
            if (connected && _client != null)
            {
                _client.SendSetProvider(_activeProvider);
                if (!string.IsNullOrEmpty(_model))
                    _client.SendConfig(systemPrompt: string.Empty, model: _model);
            }
            OnConnectionChanged?.Invoke(connected, model);
        }

        // ── PlayerPrefs ──

        private static string ReadProviderPreference()
        {
            var stored = PlayerPrefs.GetString(PlayerPrefsBackendKey, DefaultProvider);
            return string.IsNullOrEmpty(stored) ? DefaultProvider : NormalizeProvider(stored);
        }

        private static string ReadModelPreference()
        {
            return PlayerPrefs.GetString(PlayerPrefsModelKey, string.Empty);
        }

        // 기존 백엔드 토글 키 호환: "cli" → "anthropic_cli", "api" → "anthropic_api"
        private static string NormalizeProvider(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DefaultProvider;
            var k = raw.Trim().ToLowerInvariant();
            return k switch
            {
                "cli" => "anthropic_cli",
                "api" => "anthropic_api",
                _ => raw,
            };
        }

        // ── 크래프팅 유틸 (AnthropicCliChatService 와 동일 로직) ──

        private static string BuildCraftPrompt(string userPrompt)
        {
            return
$@"다음 요청을 분석하여 AI 에이전트 스킬을 정의해주세요.
반드시 아래 JSON 형식으로만 응답하세요. JSON 외 다른 텍스트는 출력하지 마세요.

{{
  ""skillName"": ""스킬 이름 (한글, 간결하게)"",
  ""description"": ""스킬 설명 (한 줄)"",
  ""promptContent"": ""에이전트가 이 스킬을 수행할 때 따를 상세 지시사항 (3~10줄)"",
  ""category"": ""General|Development|Document|Analysis|ExternalTool""
}}

사용자 요청: {userPrompt}";
        }

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
                Debug.LogWarning($"[MiddlewareChat] JSON 파싱 실패: {e.Message}");
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
            var json = braces.Value.Trim();
            return Regex.Replace(json, @"[\r\n]+\s*", "\n");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_client != null)
            {
                _client.OnDelta -= HandleDelta;
                _client.OnFinal -= HandleFinal;
                _client.OnError -= HandleError;
                _client.OnStatus -= HandleStatus;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnCleared -= HandleCleared;
                _client.OnToolUserAsk -= HandleToolUserAsk;
                _client.OnSubAgentSpawned -= HandleSubAgentSpawned;
                _client.OnSubAgentCompleted -= HandleSubAgentCompleted;
                _client.OnSubAgentFailed -= HandleSubAgentFailed;
                _client.OnTaskState -= HandleTaskState;
                _client.OnCronState -= HandleCronState;
                if (_telemetry != null)
                {
                    _client.OnTelemetry -= _telemetry.Ingest;
                }
            }
        }
    }
}
