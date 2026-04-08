using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Services;
using OpenDesk.SkillDiskette.Models;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// IClaudeService 구현체.
    /// 새 프로토콜(멀티에이전트)의 ClaudeWebSocketClient를 래핑.
    /// 기존 IClaudeService 인터페이스 호환을 위해 구 이벤트 시그니처로 중계.
    /// TODO: Phase 3에서 IClaudeService 인터페이스 자체를 새 프로토콜에 맞게 리팩토링.
    /// </summary>
    public class ClaudeService : IClaudeService, IDisposable
    {
        private readonly ClaudeWebSocketClient _client;
        private bool _disposed;

        /// <summary>기본 에이전트 ID (단일 에이전트 호환용)</summary>
        private string _defaultAgentId = "researcher";

        public bool IsConnected => _client != null && _client.IsConnected;

        // ── 이벤트 (구 시그니처 호환) ──

        public event Action<string> OnDelta;
        public event Action<string, float> OnFinal;
        public event Action<string> OnError;
        public event Action<string> OnStatus;
        public event Action<bool, string> OnConnectionChanged;
        public event Action OnCleared;

        [Inject]
        public ClaudeService(ClaudeWebSocketClient client)
        {
            _client = client;

            _client.OnAgentDelta += HandleDelta;
            _client.OnAgentMessage += HandleMessage;
            _client.OnAgentState += HandleState;
            _client.OnConnectionChanged += HandleConnectionChanged;
            _client.OnSessionSwitched += HandleSessionSwitched;
        }

        // ── 기본 통신 (구 인터페이스 호환) ──

        public void SetSystemPrompt(string systemPrompt)
        {
            // 새 프로토콜에서는 미들웨어가 system prompt 관리
            // 호출해도 무시 (로그만 남김)
            Debug.Log($"[ClaudeService] SetSystemPrompt 호출됨 ({systemPrompt?.Length ?? 0}자) — 미들웨어가 관리하므로 무시");
        }

        public void SendMessage(string message)
        {
            _client.SendChatMessage(_defaultAgentId, message);
        }

        public void ResumeSession(string conversationJson)
        {
            // 새 프로토콜에서는 session_switch 사용
            Debug.Log("[ClaudeService] ResumeSession 호출됨 — 새 프로토콜에서는 session_switch 사용");
        }

        public void ClearHistory()
        {
            _client.SendChatClear(_defaultAgentId);
        }

        public void SendMcpConfig(string mcpConfigJson)
        {
            // 새 프로토콜에서는 미들웨어가 도구 관리
            Debug.Log($"[ClaudeService] MCP config 전달 예약 ({mcpConfigJson?.Length ?? 0}자) — 미들웨어가 관리");
        }

        // ── 크래프팅 ──

        public async UniTask<CraftResult> CraftDisketteAsync(
            string naturalLanguagePrompt, CancellationToken ct)
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("Claude 서버에 연결되지 않았습니다");

            var metaPrompt = BuildCraftPrompt(naturalLanguagePrompt);
            var tcs = new UniTaskCompletionSource<string>();
            var accumulated = new StringBuilder();

            void OnDeltaHandler(AgentDeltaMessage msg)
            {
                if (msg.agent_id == _defaultAgentId)
                    accumulated.Append(msg.text);
            }

            void OnMessageHandler(AgentMessageMessage msg)
            {
                if (msg.agent_id == _defaultAgentId)
                {
                    var result = string.IsNullOrEmpty(msg.message) ? accumulated.ToString() : msg.message;
                    tcs.TrySetResult(result);
                }
            }

            void OnStateHandler(AgentStateMessage msg)
            {
                if (msg.agent_id == _defaultAgentId && msg.state == "error")
                    tcs.TrySetException(new Exception($"크래프팅 실패: {msg.message}"));
            }

            _client.OnAgentDelta += OnDeltaHandler;
            _client.OnAgentMessage += OnMessageHandler;
            _client.OnAgentState += OnStateHandler;

            try
            {
                _client.SendChatMessage(_defaultAgentId, metaPrompt);
                var response = await tcs.Task.AttachExternalCancellation(ct);
                var craftResult = ParseCraftResult(response);

                if (craftResult == null || !craftResult.IsValid)
                {
                    Debug.LogWarning($"[ClaudeService] 크래프팅 파싱 실패. 응답: {response[..Math.Min(response.Length, 200)]}");
                    return null;
                }

                Debug.Log($"[ClaudeService] 크래프팅 성공: {craftResult.skillName}");
                return craftResult;
            }
            finally
            {
                _client.OnAgentDelta -= OnDeltaHandler;
                _client.OnAgentMessage -= OnMessageHandler;
                _client.OnAgentState -= OnStateHandler;
            }
        }

        // ── 이벤트 핸들러 (새 → 구 시그니처 중계) ──

        private void HandleDelta(AgentDeltaMessage msg)
        {
            if (msg.agent_id == _defaultAgentId)
                OnDelta?.Invoke(msg.text);
        }

        private void HandleMessage(AgentMessageMessage msg)
        {
            if (msg.agent_id == _defaultAgentId)
                OnFinal?.Invoke(msg.message ?? "", 0f);
        }

        private void HandleState(AgentStateMessage msg)
        {
            if (msg.agent_id != _defaultAgentId) return;

            if (msg.state == "error")
                OnError?.Invoke(msg.message ?? "알 수 없는 에러");

            // 상태를 Status 이벤트로 중계
            var statusText = msg.state switch
            {
                "thinking" => "사고 중...",
                "working"  => $"도구 사용 중: {msg.tool}",
                "idle"     => "",
                "error"    => $"에러: {msg.message}",
                _          => msg.state
            };
            if (!string.IsNullOrEmpty(statusText))
                OnStatus?.Invoke(statusText);
        }

        private void HandleConnectionChanged(bool connected)
        {
            OnConnectionChanged?.Invoke(connected, connected ? "agent-middleware" : "");
        }

        private void HandleSessionSwitched(SessionSwitchedMessage msg)
        {
            if (msg.agent_id == _defaultAgentId && (msg.chat_history == null || msg.chat_history.Length == 0))
                OnCleared?.Invoke();
        }

        // ── 크래프팅 유틸 ──

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
                Debug.LogWarning($"[ClaudeService] JSON 파싱 실패: {e.Message}");
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
            json = Regex.Replace(json, @"[\r\n]+\s*", "\n");
            return json;
        }

        // ── Dispose ──

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_client != null)
            {
                _client.OnAgentDelta -= HandleDelta;
                _client.OnAgentMessage -= HandleMessage;
                _client.OnAgentState -= HandleState;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnSessionSwitched -= HandleSessionSwitched;
            }
        }
    }
}
