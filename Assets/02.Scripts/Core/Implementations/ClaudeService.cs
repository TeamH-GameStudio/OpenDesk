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
    /// ClaudeWebSocketClient를 래핑하여 이벤트 중계 + 크래프팅 기능 제공.
    /// </summary>
    public class ClaudeService : IClaudeService, IDisposable
    {
        private readonly ClaudeWebSocketClient _client;
        private bool _disposed;

        public bool IsConnected => _client != null && _client.IsConnected;

        // ── 이벤트 ──

        public event Action<string, string, string> OnAgentState;
        public event Action<string, string> OnAgentThinking;
        public event Action<string, string> OnAgentDelta;
        public event Action<string, string> OnAgentMessage;
        public event Action<string, string> OnAgentAction;
        public event Action<string, string, string> OnAgentError;
        public event Action<string, string, SessionInfo[]> OnSessionList;
        public event Action<string, string, ChatHistoryEntry[]> OnSessionSwitched;
        public event Action<bool> OnConnectionChanged;

        [Inject]
        public ClaudeService(ClaudeWebSocketClient client)
        {
            _client = client;

            _client.OnAgentState      += (a, s, t) => OnAgentState?.Invoke(a, s, t);
            _client.OnAgentThinking   += (a, t) => OnAgentThinking?.Invoke(a, t);
            _client.OnAgentDelta      += (a, t) => OnAgentDelta?.Invoke(a, t);
            _client.OnAgentMessage    += (a, m) => OnAgentMessage?.Invoke(a, m);
            _client.OnAgentAction     += (a, act) => OnAgentAction?.Invoke(a, act);
            _client.OnAgentError      += (a, e, m) => OnAgentError?.Invoke(a, e, m);
            _client.OnSessionList     += (a, c, s) => OnSessionList?.Invoke(a, c, s);
            _client.OnSessionSwitched += (a, s, h) => OnSessionSwitched?.Invoke(a, s, h);
            _client.OnConnectionChanged += c => OnConnectionChanged?.Invoke(c);
        }

        // ── 기본 통신 ──

        public void SendMessage(string agentId, string message)
            => _client.SendChatMessage(agentId, message);

        public void ClearHistory(string agentId)
            => _client.SendChatClear(agentId);

        public void RequestStatus()
            => _client.SendStatusRequest();

        public void RequestSessionList(string agentId)
            => _client.SendSessionList(agentId);

        public void CreateSession(string agentId)
            => _client.SendSessionNew(agentId);

        public void SwitchSession(string agentId, string sessionId)
            => _client.SendSessionSwitch(agentId, sessionId);

        public void DeleteSession(string agentId, string sessionId)
            => _client.SendSessionDelete(agentId, sessionId);

        // ── 크래프팅 ──

        public async UniTask<CraftResult> CraftDisketteAsync(
            string agentId, string naturalLanguagePrompt, CancellationToken ct)
        {
            if (_client == null || !_client.IsConnected)
                throw new InvalidOperationException("Claude 서버에 연결되지 않았습니다");

            var metaPrompt = BuildCraftPrompt(naturalLanguagePrompt);
            var tcs = new UniTaskCompletionSource<string>();
            var accumulated = new StringBuilder();

            void OnDeltaHandler(string aId, string text)
            {
                if (aId == agentId) accumulated.Append(text);
            }

            void OnMessageHandler(string aId, string message)
            {
                if (aId != agentId) return;
                var result = string.IsNullOrEmpty(message) ? accumulated.ToString() : message;
                tcs.TrySetResult(result);
            }

            void OnErrorHandler(string aId, string err, string msg)
            {
                if (aId != agentId) return;
                tcs.TrySetException(new Exception($"크래프팅 실패: {err} - {msg}"));
            }

            _client.OnAgentDelta   += OnDeltaHandler;
            _client.OnAgentMessage += OnMessageHandler;
            _client.OnAgentError   += OnErrorHandler;

            try
            {
                _client.SendChatMessage(agentId, metaPrompt);
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
                _client.OnAgentDelta   -= OnDeltaHandler;
                _client.OnAgentMessage -= OnMessageHandler;
                _client.OnAgentError   -= OnErrorHandler;
            }
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
            // 람다 이벤트는 client 파괴 시 자동 해제
        }
    }
}
