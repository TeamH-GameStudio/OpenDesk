using System.Collections.Generic;
using OpenDesk.Claude.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Claude 채팅 UI 관리 — TestChattingScene 전용 (레거시).
    /// 새 프로토콜 이벤트를 구독하여 기존 UI 로직 유지.
    /// </summary>
    public class ClaudeChatManager : MonoBehaviour
    {
        [Header("WebSocket 클라이언트")]
        [SerializeField] private ClaudeWebSocketClient _client;

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

        private readonly List<GameObject> _activeBubbles = new();
        private GameObject _currentStreamingBubble;
        private TMP_Text   _currentStreamingTMP;
        private string     _streamingText = "";
        private bool       _isStreaming;

        /// <summary>기본 에이전트 ID</summary>
        private const string DefaultAgentId = "researcher";

        private void Start()
        {
            if (_client != null)
            {
                _client.OnAgentDelta   += HandleDelta;
                _client.OnAgentMessage += HandleMessage;
                _client.OnAgentState   += HandleState;
                _client.OnConnectionChanged += HandleConnectionChanged;
                _client.OnSessionSwitched   += HandleSessionSwitched;
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
                _client.OnAgentDelta   -= HandleDelta;
                _client.OnAgentMessage -= HandleMessage;
                _client.OnAgentState   -= HandleState;
                _client.OnConnectionChanged -= HandleConnectionChanged;
                _client.OnSessionSwitched   -= HandleSessionSwitched;
            }
        }

        // ── 전송 ────────────────────────────────────────────

        private void OnSendClicked()
        {
            if (_inputField == null) return;
            var text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text) || _isStreaming) return;

            CreateUserBubble(text);
            _client?.SendChatMessage(DefaultAgentId, text);

            _inputField.text = "";
            _inputField.ActivateInputField();
            _isStreaming = true;
            SetAgentStatus("응답 대기 중...");
        }

        private void OnClearClicked()
        {
            _client?.SendChatClear(DefaultAgentId);
            DestroyAllBubbles();
            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");
        }

        // ── 수신 핸들러 ──────────────────────────────────────

        private void HandleDelta(AgentDeltaMessage msg)
        {
            if (msg.agent_id != DefaultAgentId) return;

            SetAgentStatus("응답 중...");

            if (_currentStreamingBubble == null)
            {
                _currentStreamingBubble = CreateBubble(_aiBubblePrefab, msg.text);
                _currentStreamingTMP    = _currentStreamingBubble?.GetComponentInChildren<TMP_Text>();
                _streamingText          = msg.text;
            }
            else
            {
                _streamingText += msg.text;
                if (_currentStreamingTMP != null)
                    _currentStreamingTMP.text = _streamingText;
            }

            AutoScroll();
        }

        private void HandleMessage(AgentMessageMessage msg)
        {
            if (msg.agent_id != DefaultAgentId) return;

            if (_currentStreamingBubble != null)
            {
                if (_currentStreamingTMP != null && !string.IsNullOrEmpty(msg.message))
                    _currentStreamingTMP.text = msg.message;
            }
            else if (!string.IsNullOrEmpty(msg.message))
            {
                CreateAIBubble(msg.message);
            }

            _currentStreamingBubble = null;
            _currentStreamingTMP    = null;
            _streamingText          = "";
            _isStreaming             = false;
            SetAgentStatus("대기 중");
            AutoScroll();
        }

        private void HandleState(AgentStateMessage msg)
        {
            if (msg.agent_id != DefaultAgentId) return;

            if (msg.state == "error" && !string.IsNullOrEmpty(msg.message))
            {
                CreateSystemBubble($"오류: {msg.message}");
                if (_isStreaming)
                {
                    if (_currentStreamingBubble != null && _currentStreamingTMP != null)
                        _currentStreamingTMP.text = _streamingText + "\n<color=#F44336><i>(응답 중단됨)</i></color>";

                    _currentStreamingBubble = null;
                    _currentStreamingTMP    = null;
                    _streamingText          = "";
                    _isStreaming             = false;
                }
                SetAgentStatus("대기 중");
            }
            else if (msg.state == "working")
            {
                SetAgentStatus($"도구 사용 중: {msg.tool}");
            }
            else if (msg.state == "thinking")
            {
                SetAgentStatus("사고 중...");
            }
        }

        private void HandleConnectionChanged(bool connected)
        {
            SetConnectionUI(connected);
            CreateSystemBubble(connected ? "Claude 채팅 준비 완료" : "서버 연결 끊김 - 재연결 시도 중...");
        }

        private void HandleSessionSwitched(SessionSwitchedMessage msg)
        {
            if (msg.agent_id != DefaultAgentId) return;
            if (msg.chat_history == null || msg.chat_history.Length == 0)
                CreateSystemBubble("대화가 초기화되었습니다");
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
                _modelText.text = connected ? "agent-middleware" : "";
        }

        // ── 버블 ────────────────────────────────────────────

        private void CreateUserBubble(string text) { CreateBubble(_userBubblePrefab, text); AutoScroll(); }
        private void CreateAIBubble(string text)   { CreateBubble(_aiBubblePrefab, text); AutoScroll(); }
        private void CreateSystemBubble(string text) { CreateBubble(_systemBubblePrefab, text); AutoScroll(); }

        private GameObject CreateBubble(GameObject prefab, string text)
        {
            if (prefab == null || _chatContent == null) return null;
            var obj = Instantiate(prefab, _chatContent);
            var tmp = obj.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = text;
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
                if (obj != null) Destroy(obj);
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
