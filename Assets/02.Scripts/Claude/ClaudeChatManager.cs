using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Claude 채팅 UI 관리 -- TestChattingScene 전용.
    /// 멀티 에이전트 프로토콜 대응 -- Inspector에서 테스트 대상 agent_id 지정.
    /// </summary>
    public class ClaudeChatManager : MonoBehaviour
    {
        [Header("WebSocket 클라이언트")]
        [SerializeField] private ClaudeWebSocketClient _client;

        [Header("에이전트 설정")]
        [SerializeField] private string _targetAgentId = "researcher";

        [Header("채팅 영역")]
        [SerializeField] private ScrollRect    _scrollRect;
        [SerializeField] private RectTransform _chatContent;

        [Header("메시지 프리팹")]
        [SerializeField] private GameObject _userBubblePrefab;
        [SerializeField] private GameObject _aiBubblePrefab;
        [SerializeField] private GameObject _systemBubblePrefab;

        [Header("입력")]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private Button         _sendButton;
        [SerializeField] private Button         _clearButton;

        [Header("상태 표시")]
        [SerializeField] private Image      _statusDot;
        [SerializeField] private TMP_Text   _statusText;
        [SerializeField] private TMP_Text   _modelText;
        [SerializeField] private TMP_Text   _typingIndicator;

        [Header("설정")]
        [SerializeField] private int _maxBubbles = 100;

        // ── 내부 상태 ─────────────────────────────────────────

        private readonly List<GameObject> _activeBubbles = new();
        private GameObject _currentStreamingBubble;
        private TMP_Text   _currentStreamingTMP;
        private string     _streamingText = "";
        private bool       _isStreaming;

        // ── 초기화 ───────────────────────────────────────────

        private void Start()
        {
            if (_client != null)
            {
                _client.OnAgentDelta      += HandleDelta;
                _client.OnAgentMessage    += HandleMessage;
                _client.OnAgentError      += HandleError;
                _client.OnAgentState      += HandleState;
                _client.OnAgentThinking   += HandleThinking;
                _client.OnAgentAction     += HandleAction;
                _client.OnConnectionChanged += HandleConnectionChanged;
                _client.OnSessionSwitched += HandleSessionSwitched;
            }

            if (_sendButton != null)
                _sendButton.onClick.AddListener(OnSendClicked);

            if (_clearButton != null)
                _clearButton.onClick.AddListener(OnClearClicked);

            if (_inputField != null)
                _inputField.onSubmit.AddListener(_ => OnSendClicked());

            SetConnectionUI(false);
            CreateSystemBubble("서버에 연결 중...");
        }

        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.OnAgentDelta      -= HandleDelta;
                _client.OnAgentMessage    -= HandleMessage;
                _client.OnAgentError      -= HandleError;
                _client.OnAgentState      -= HandleState;
                _client.OnAgentThinking   -= HandleThinking;
                _client.OnAgentAction     -= HandleAction;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnSessionSwitched -= HandleSessionSwitched;
            }
        }

        // ── 전송 ────────────────────────────────────────────

        private void OnSendClicked()
        {
            if (_inputField == null) return;

            var text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (_isStreaming) return;

            CreateUserBubble(text);
            _client?.SendChatMessage(_targetAgentId, text);

            _inputField.text = "";
            _inputField.ActivateInputField();

            _isStreaming = true;
            SetAgentStatus("응답 대기 중...");
        }

        private void OnClearClicked()
        {
            _client?.SendChatClear(_targetAgentId);
            DestroyAllBubbles();
            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");
        }

        // ── 서버 이벤트 핸들러 ──────────────────────────────

        private void HandleDelta(string agentId, string text)
        {
            if (agentId != _targetAgentId) return;

            SetAgentStatus("응답 중...");

            if (_currentStreamingBubble == null)
            {
                _currentStreamingBubble = CreateBubble(_aiBubblePrefab, text);
                _currentStreamingTMP    = _currentStreamingBubble?.GetComponentInChildren<TMP_Text>();
                _streamingText          = text;
            }
            else
            {
                _streamingText += text;
                if (_currentStreamingTMP != null)
                    _currentStreamingTMP.text = _streamingText;
            }

            AutoScroll();
        }

        private void HandleMessage(string agentId, string message)
        {
            if (agentId != _targetAgentId) return;

            if (_currentStreamingBubble != null)
            {
                if (_currentStreamingTMP != null && !string.IsNullOrEmpty(message))
                    _currentStreamingTMP.text = message;
            }
            else if (!string.IsNullOrEmpty(message))
            {
                CreateAIBubble(message);
            }

            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");

            AutoScroll();
        }

        private void HandleError(string agentId, string error, string message)
        {
            if (agentId != _targetAgentId && !string.IsNullOrEmpty(agentId)) return;

            CreateSystemBubble($"오류 [{error}]: {message}");

            if (_isStreaming)
            {
                if (_currentStreamingBubble != null && _currentStreamingTMP != null)
                    _currentStreamingTMP.text = _streamingText + "\n<color=#F44336><i>(응답 중단됨)</i></color>";

                _currentStreamingBubble = null;
                _currentStreamingTMP    = null;
                _streamingText          = "";
                _isStreaming             = false;
                SetAgentStatus("대기 중");
            }
        }

        private void HandleState(string agentId, string state, string tool)
        {
            if (agentId != _targetAgentId) return;

            var statusText = state switch
            {
                "thinking" => "사고 중...",
                "working"  => string.IsNullOrEmpty(tool) ? "작업 중..." : $"도구 실행: {tool}",
                "complete" => "응답 완료",
                "idle"     => "대기 중",
                _          => state
            };
            SetAgentStatus(statusText);
        }

        private void HandleThinking(string agentId, string thinking)
        {
            if (agentId != _targetAgentId) return;
            // thinking 내용을 상태에 요약 표시
            var preview = thinking.Length > 30 ? thinking[..30] + "..." : thinking;
            SetAgentStatus($"사고 중: {preview}");
        }

        private void HandleAction(string agentId, string action)
        {
            if (agentId != _targetAgentId) return;
            Debug.Log($"[ClaudeChatManager] 액션 수신: {action}");
        }

        private void HandleConnectionChanged(bool connected)
        {
            SetConnectionUI(connected);

            if (connected)
                CreateSystemBubble($"서버 연결 완료 -- 에이전트: {_targetAgentId}");
            else
                CreateSystemBubble("서버 연결 끊김 - 재연결 시도 중...");
        }

        private void HandleSessionSwitched(string agentId, string sessionId, Models.ChatHistoryEntry[] history)
        {
            if (agentId != _targetAgentId) return;
            CreateSystemBubble($"세션 전환: {sessionId}");
        }

        // ── UI 헬퍼 ─────────────────────────────────────────

        private void SetAgentStatus(string text)
        {
            if (_typingIndicator != null)
                _typingIndicator.text = text;
        }

        private void SetConnectionUI(bool connected)
        {
            if (_statusDot != null)
                _statusDot.color = connected
                    ? new Color32(76, 175, 80, 255)
                    : new Color32(244, 67, 54, 255);

            if (_statusText != null)
                _statusText.text = connected ? "연결됨" : "연결 끊김";

            SetAgentStatus(connected ? "대기 중" : "연결 끊김");

            if (_modelText != null)
                _modelText.text = connected ? _targetAgentId : "";
        }

        // ── 버블 생성 ───────────────────────────────────────

        private void CreateUserBubble(string text)
        {
            CreateBubble(_userBubblePrefab, text);
            AutoScroll();
        }

        private void CreateAIBubble(string text)
        {
            CreateBubble(_aiBubblePrefab, text);
            AutoScroll();
        }

        private void CreateSystemBubble(string text)
        {
            CreateBubble(_systemBubblePrefab, text);
            AutoScroll();
        }

        private GameObject CreateBubble(GameObject prefab, string text)
        {
            if (prefab == null || _chatContent == null) return null;

            var obj = Instantiate(prefab, _chatContent);
            var tmp = obj.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                tmp.text = text;

            _activeBubbles.Add(obj);

            while (_activeBubbles.Count > _maxBubbles)
            {
                var oldest = _activeBubbles[0];
                _activeBubbles.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            return obj;
        }

        private void DestroyAllBubbles()
        {
            foreach (var obj in _activeBubbles)
            {
                if (obj != null) Destroy(obj);
            }
            _activeBubbles.Clear();
        }

        private void AutoScroll()
        {
            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
