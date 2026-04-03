using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude;
using OpenDesk.Core.Services;
using OpenDesk.SkillDiskette.Models;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// IClaudeService 구현체.
    /// 씬의 ClaudeWebSocketClient를 래핑하여 이벤트 중계 + 크래프팅 기능 제공.
    /// </summary>
    public class ClaudeService : IClaudeService, IDisposable
    {
        private readonly ClaudeWebSocketClient _client;
        private bool _disposed;

        public bool IsConnected => _client != null && _client.IsConnected;

        // ── 이벤트 ──

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

            _client.OnDelta += HandleDelta;
            _client.OnFinal += HandleFinal;
            _client.OnError += HandleError;
            _client.OnStatus += HandleStatus;
            _client.OnConnectionChanged += HandleConnectionChanged;
            _client.OnCleared += HandleCleared;
        }

        // ── 기본 통신 ──

        public void SetSystemPrompt(string systemPrompt)
        {
            if (_client == null || !_client.IsConnected) return;
            _client.SendConfig(systemPrompt);
            Debug.Log($"[ClaudeService] System prompt 설정 ({systemPrompt.Length}자)");
        }

        public void SendMessage(string message)
        {
            _client.SendChat(message);
        }

        public void ResumeSession(string conversationJson)
        {
            _client.SendResume(conversationJson);
        }

        public void ClearHistory()
        {
            _client.SendClear();
        }

        public void SendMcpConfig(string mcpConfigJson)
        {
            if (_client == null || !_client.IsConnected || string.IsNullOrEmpty(mcpConfigJson))
                return;

            // config 메시지에 mcpConfig 필드를 추가하여 전송
            // 기존 ConfigRequest에는 mcpConfig 필드가 없으므로 수동 JSON 구성
            var json = $"{{\"type\":\"config\",\"mcpConfig\":{mcpConfigJson}}}";
            // 직접 SendText가 불가하므로 SendConfig를 확장하거나 별도 처리
            // 당장은 Debug.Log로 표시 — Day 9에서 프로토콜 확장 시 구현
            Debug.Log($"[ClaudeService] MCP config 전달 예약 ({mcpConfigJson.Length}자)");
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

            void OnDeltaHandler(string text) => accumulated.Append(text);

            void OnFinalHandler(string text, float cost)
            {
                var result = string.IsNullOrEmpty(text) ? accumulated.ToString() : text;
                tcs.TrySetResult(result);
            }

            void OnErrorHandler(string err) =>
                tcs.TrySetException(new Exception($"크래프팅 실패: {err}"));

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
                    Debug.LogWarning($"[ClaudeService] 크래프팅 파싱 실패. 응답: {response[..Math.Min(response.Length, 200)]}");
                    return null;
                }

                Debug.Log($"[ClaudeService] 크래프팅 성공: {craftResult.skillName}");
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
        private void HandleConnectionChanged(bool connected, string model)
            => OnConnectionChanged?.Invoke(connected, model);
        private void HandleCleared() => OnCleared?.Invoke();

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

            // 1. TMP 리치텍스트 태그 제거 (formatter.py가 주입한 <color=...>, </color>, <b>, </b> 등)
            var cleaned = Regex.Replace(text, @"<[^>]+>", "");

            // 2. "--- json ---" 같은 코드블록 라벨 제거
            cleaned = Regex.Replace(cleaned, @"---\s*\w+\s*---", "");

            // 3. { ... } 추출
            var braces = Regex.Match(cleaned, @"\{[\s\S]*\}");
            if (!braces.Success) return null;

            var json = braces.Value.Trim();

            // 4. 줄바꿈/탭 정리
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
                _client.OnDelta -= HandleDelta;
                _client.OnFinal -= HandleFinal;
                _client.OnError -= HandleError;
                _client.OnStatus -= HandleStatus;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnCleared -= HandleCleared;
            }
        }
    }
}
