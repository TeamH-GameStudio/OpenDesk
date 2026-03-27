using System;
using System.Collections.Generic;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>
    /// 터미널/채팅 패널 — 에이전트에게 직접 명령 입력 + 응답 표시
    /// - 세션 전환 (main/dev/planner/life)
    /// - 채널 메시지 통합 표시
    /// - Enter=전송, Shift+Enter=줄바꿈
    /// - 타임스탬프 + 보낸 사람 표시
    /// - 연결 상태 변경 시 시스템 메시지
    /// - 전송 실패 시 사용자 알림
    /// </summary>
    public class TerminalChatController : MonoBehaviour
    {
        [Header("세션")]
        [SerializeField] private TMP_Dropdown _sessionDropdown;

        [Header("채팅 영역")]
        [SerializeField] private ScrollRect    _chatScrollRect;
        [SerializeField] private RectTransform _chatContent;

        [Header("메시지 프리팹")]
        [SerializeField] private GameObject _userMessagePrefab;     // 우측 정렬, 파란 배경
        [SerializeField] private GameObject _agentMessagePrefab;    // 좌측 정렬, 회색 배경
        [SerializeField] private GameObject _systemMessagePrefab;   // 중앙, 노란 텍스트
        [SerializeField] private GameObject _channelMessagePrefab;  // 좌측 정렬, 초록 배경

        [Header("입력")]
        [SerializeField] private TMP_InputField _chatInputField;
        [SerializeField] private Button         _sendButton;
        [SerializeField] private Button         _clearButton;

        [Header("상태 표시")]
        [SerializeField] private TMP_Text   _typingText;           // 에이전트 상태: "대기 중" / "💭 사고 중..." 등
        [SerializeField] private Button     _scrollToBottomButton; // 위로 스크롤 시 표시

        [Header("설정")]
        [SerializeField] private int _maxMessages = 200;

        [Inject] private IOpenClawBridgeService _bridge;
        [Inject] private IAgentStateService     _agentState;
        [Inject] private IChannelService        _channelService;

        private string _currentSessionId = "main";
        private readonly Dictionary<string, List<ChatMessage>> _chatHistory = new();
        private readonly List<GameObject> _activeMessageObjects = new();
        private bool _isUserScrolling;

        // 스트리밍 응답 추적: runId → (GameObject, 누적 텍스트)
        private readonly Dictionary<string, (GameObject obj, string text, ChatMessage msg)> _streamingBubbles = new();

        private void Start()
        {
            // 세션 드롭다운
            if (_sessionDropdown != null)
            {
                _sessionDropdown.ClearOptions();
                _sessionDropdown.AddOptions(new List<string> { "main", "dev", "planner", "life" });
                _sessionDropdown.onValueChanged.AddListener(OnSessionChanged);
            }

            // 전송 버튼
            Debug.Log($"[Terminal] _sendButton={(_sendButton != null ? "OK" : "NULL")}, _chatInputField={(_chatInputField != null ? "OK" : "NULL")}, _bridge={(_bridge != null ? "OK" : "NULL")}");
            if (_sendButton != null)
                _sendButton.onClick.AddListener(OnSendClicked);

            // Enter 키 → 전송 (Input System 호환)
            if (_chatInputField != null)
                _chatInputField.onSubmit.AddListener(_ => OnSendClicked());

            // 초기화 버튼
            if (_clearButton != null)
                _clearButton.onClick.AddListener(OnClearClicked);

            // 스크롤 버튼 초기 숨김 (타이핑 인디케이터는 항상 표시)
            if (_scrollToBottomButton != null)
            {
                _scrollToBottomButton.gameObject.SetActive(false);
                _scrollToBottomButton.onClick.AddListener(ScrollToBottom);
            }

            // 스크롤 감지 — 사용자가 위로 스크롤하면 "최신으로" 버튼 표시
            if (_chatScrollRect != null)
            {
                _chatScrollRect.onValueChanged.AddListener(pos =>
                {
                    _isUserScrolling = pos.y > 0.05f; // 바닥에서 벗어나면
                    if (_scrollToBottomButton != null)
                        _scrollToBottomButton.gameObject.SetActive(_isUserScrolling);
                });
            }

            // 에이전트 이벤트 수신
            if (_bridge != null)
            {
                _bridge.OnEventReceived.Subscribe(OnEventReceived).AddTo(this);

                // 연결/해제 이벤트
                _bridge.ConnectionState.Subscribe(connected =>
                {
                    var sysMsg = connected ? "[연결] Gateway 연결됨" : "[끊김] Gateway 연결 끊김 - 자동 재연결 대기 중";
                    AddSystemMessage(sysMsg);

                    // 상태 텍스트도 갱신
                    if (_typingText != null)
                        _typingText.text = connected ? "대기 중" : "연결 끊김";
                }).AddTo(this);
            }

            // 에이전트 상태 변경 → StatusText 항상 표시
            if (_typingText != null)
                _typingText.text = "대기 중";

            if (_agentState != null)
            {
                _agentState.OnStateChanged.Subscribe(e =>
                {
                    if (e.SessionId != _currentSessionId) return;
                    var statusText = GetStatusText(e.State);
                    if (_typingText != null)
                        _typingText.text = statusText;
                }).AddTo(this);
            }

            // 채널 메시지 수신
            if (_channelService != null)
            {
                _channelService.OnChannelStatusChanged.Subscribe(ch =>
                {
                    if (ch.Status == ChannelStatus.Connected)
                        AddSystemMessage($"[{ch.DisplayName}] 채널 연결됨 — 이 채팅에서 메시지를 주고받을 수 있습니다");
                    else if (ch.Status == ChannelStatus.Disconnected)
                        AddSystemMessage($"[{ch.DisplayName}] 채널 연결 해제됨");
                }).AddTo(this);
            }

            // 시작 메시지
            AddSystemMessage("OpenDesk 터미널 준비 완료");
            AddSystemMessage("명령을 입력하세요. Enter=전송, Shift+Enter=줄바꿈");
        }

        private void Update()
        {
            // Enter 키 처리: TMP_InputField의 onSubmit 이벤트로 대체 (Input System 호환)
            // Update에서의 Input.GetKeyDown은 새 Input System과 충돌하므로 제거
        }

        // ── 전송 ────────────────────────────────────────────────────────

        private async void OnSendClicked()
        {
            Debug.Log("[Terminal] OnSendClicked 호출됨");
            if (_chatInputField == null) { Debug.LogWarning("[Terminal] _chatInputField NULL"); return; }

            var text = _chatInputField.text.Trim();
            if (string.IsNullOrEmpty(text)) { Debug.Log("[Terminal] 빈 텍스트 — 무시"); return; }

            // UI에 사용자 메시지 즉시 표시
            AddChatMessage(_currentSessionId, "나", text, MessageType.User);

            // 입력란 초기화 + 포커스 유지
            _chatInputField.text = "";
            _chatInputField.ActivateInputField();

            // Gateway로 전송
            if (_bridge == null || !_bridge.IsConnected)
            {
                AddSystemMessage("[!] Gateway에 연결되지 않았습니다. 메시지가 버퍼에 저장됩니다.");
            }

            try
            {
                if (_bridge != null)
                    await _bridge.SendMessageAsync(_currentSessionId, text);
            }
            catch (Exception ex)
            {
                AddSystemMessage($"[!] 전송 실패: {ex.Message}");
            }
        }

        // ── 이벤트 수신 ─────────────────────────────────────────────────

        private void OnEventReceived(AgentEvent e)
        {
            var sessionId = string.IsNullOrEmpty(e.SessionId) ? "main" : e.SessionId;

            // StatusText 갱신
            UpdateStatusText(e.ActionType);

            // AI 채팅 스트리밍 응답 처리
            if (e.ActionType == AgentActionType.ChatDelta)
            {
                HandleChatDelta(sessionId, e);
                return;
            }

            if (e.ActionType == AgentActionType.ChatFinal)
            {
                HandleChatFinal(sessionId, e);
                return;
            }
        }

        /// <summary>
        /// ChatDelta: 같은 runId의 말풍선에 텍스트를 누적 append
        /// 첫 delta면 새 말풍선 생성
        /// </summary>
        private void HandleChatDelta(string sessionId, AgentEvent e)
        {
            if (string.IsNullOrEmpty(e.Message)) return;

            var runId = e.RunId;
            if (string.IsNullOrEmpty(runId))
                runId = "_no_run_id";

            if (_streamingBubbles.TryGetValue(runId, out var existing))
            {
                // 기존 말풍선에 텍스트 append
                var newText = existing.text + e.Message;
                _streamingBubbles[runId] = (existing.obj, newText, existing.msg);

                // UI 업데이트
                if (existing.obj != null)
                {
                    var tmp = existing.obj.GetComponentInChildren<TMP_Text>();
                    if (tmp != null)
                    {
                        var time = existing.msg.Timestamp.ToString("HH:mm");
                        tmp.text = $"[{time}] AI\n{newText}";
                    }
                }

                // 히스토리에도 반영
                existing.msg.Text = newText;
            }
            else
            {
                // 첫 delta — 새 말풍선 생성
                var msg = new ChatMessage
                {
                    SessionId = sessionId,
                    Sender    = "AI",
                    Text      = e.Message,
                    Type      = MessageType.Agent,
                    Timestamp = DateTime.Now,
                };

                // 기록에 저장
                if (!_chatHistory.ContainsKey(sessionId))
                    _chatHistory[sessionId] = new List<ChatMessage>();
                _chatHistory[sessionId].Add(msg);

                // UI 생성
                var obj = InstantiateMessageUIAndReturn(msg);
                if (obj != null)
                    _streamingBubbles[runId] = (obj, e.Message, msg);
            }

            // 자동 스크롤
            if (!_isUserScrolling)
                ScrollToBottom();
        }

        /// <summary>
        /// ChatFinal: 스트리밍 말풍선의 텍스트를 최종 응답으로 교체하고 스트리밍 추적 해제
        /// </summary>
        private void HandleChatFinal(string sessionId, AgentEvent e)
        {
            var runId = e.RunId;
            if (string.IsNullOrEmpty(runId))
                runId = "_no_run_id";

            var finalText = !string.IsNullOrEmpty(e.Message) ? e.Message : null;

            if (_streamingBubbles.TryGetValue(runId, out var existing))
            {
                // 최종 텍스트가 있으면 교체, 없으면 delta 누적 결과 유지
                if (finalText != null)
                {
                    existing.msg.Text = finalText;

                    if (existing.obj != null)
                    {
                        var tmp = existing.obj.GetComponentInChildren<TMP_Text>();
                        if (tmp != null)
                        {
                            var time = existing.msg.Timestamp.ToString("HH:mm");
                            tmp.text = $"[{time}] AI\n{finalText}";
                        }
                    }
                }

                _streamingBubbles.Remove(runId);
            }
            else if (finalText != null)
            {
                // delta 없이 final만 온 경우 — 새 말풍선 생성
                AddChatMessage(sessionId, "AI", finalText, MessageType.Agent);
            }

            // 응답 완료 — StatusText는 OnEventReceived에서 이미 갱신됨
        }

        // ── 세션 전환 ───────────────────────────────────────────────────

        private void OnSessionChanged(int index)
        {
            if (_sessionDropdown == null) return;
            _currentSessionId = _sessionDropdown.options[index].text;
            RebuildChatDisplay();
        }

        private void OnClearClicked()
        {
            if (_chatHistory.ContainsKey(_currentSessionId))
                _chatHistory[_currentSessionId].Clear();

            // "system" 키의 메시지도 초기화
            ClearDisplayedMessages();
            AddSystemMessage("채팅 기록이 초기화되었습니다.");
        }

        // ── 메시지 관리 ─────────────────────────────────────────────────

        private enum MessageType { User, Agent, System, Channel }

        private class ChatMessage
        {
            public string      SessionId;
            public string      Sender;
            public string      Text;
            public MessageType Type;
            public DateTime    Timestamp;
        }

        private void AddChatMessage(string sessionId, string sender, string text, MessageType type)
        {
            var msg = new ChatMessage
            {
                SessionId = sessionId,
                Sender    = sender,
                Text      = text,
                Type      = type,
                Timestamp = DateTime.Now,
            };

            // 기록에 저장
            if (!_chatHistory.ContainsKey(sessionId))
                _chatHistory[sessionId] = new List<ChatMessage>();
            _chatHistory[sessionId].Add(msg);

            // 기록 제한
            if (_chatHistory[sessionId].Count > _maxMessages)
                _chatHistory[sessionId].RemoveAt(0);

            // 현재 세션이면 UI에 표시
            if (sessionId == _currentSessionId || type == MessageType.System)
                InstantiateMessageUI(msg);
        }

        private void AddSystemMessage(string text)
        {
            var msg = new ChatMessage
            {
                SessionId = "system",
                Sender    = "시스템",
                Text      = text,
                Type      = MessageType.System,
                Timestamp = DateTime.Now,
            };

            // 시스템 메시지는 모든 세션에 표시
            InstantiateMessageUI(msg);
        }

        private void InstantiateMessageUI(ChatMessage msg)
        {
            InstantiateMessageUIAndReturn(msg);
        }

        /// <summary>
        /// 메시지 UI를 생성하고 GameObject를 반환 (스트리밍 업데이트용)
        /// </summary>
        private GameObject InstantiateMessageUIAndReturn(ChatMessage msg)
        {
            if (_chatContent == null) return null;

            var prefab = msg.Type switch
            {
                MessageType.User    => _userMessagePrefab,
                MessageType.Agent   => _agentMessagePrefab,
                MessageType.System  => _systemMessagePrefab,
                MessageType.Channel => _channelMessagePrefab,
                _                   => _systemMessagePrefab,
            };

            if (prefab == null) return null;

            var obj = Instantiate(prefab, _chatContent);

            // 메시지 텍스트: "[시간] 보낸사람: 내용" 형식
            var tmp = obj.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                var time = msg.Timestamp.ToString("HH:mm");

                if (msg.Type == MessageType.System)
                    tmp.text = $"[{time}] {msg.Text}";
                else
                    tmp.text = $"[{time}] {msg.Sender}\n{msg.Text}";
            }

            _activeMessageObjects.Add(obj);

            // 표시 메시지 수 제한
            while (_activeMessageObjects.Count > _maxMessages)
            {
                var oldest = _activeMessageObjects[0];
                _activeMessageObjects.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            // 자동 스크롤 (사용자가 위로 스크롤 중이면 건너뜀)
            if (!_isUserScrolling)
                ScrollToBottom();

            return obj;
        }

        private void RebuildChatDisplay()
        {
            ClearDisplayedMessages();

            // 현재 세션 메시지 재표시
            if (_chatHistory.TryGetValue(_currentSessionId, out var messages))
            {
                foreach (var msg in messages)
                    InstantiateMessageUI(msg);
            }
        }

        private void ClearDisplayedMessages()
        {
            foreach (var obj in _activeMessageObjects)
            {
                if (obj != null) Destroy(obj);
            }
            _activeMessageObjects.Clear();
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            if (_chatScrollRect != null)
                _chatScrollRect.verticalNormalizedPosition = 0f;
            _isUserScrolling = false;
            if (_scrollToBottomButton != null)
                _scrollToBottomButton.gameObject.SetActive(false);
        }

        // ── 이벤트 포맷팅 ───────────────────────────────────────────────

        private void UpdateStatusText(AgentActionType actionType)
        {
            if (_typingText != null)
                _typingText.text = GetStatusText(actionType);
        }

        private static string GetStatusText(AgentActionType state)
        {
            return state switch
            {
                AgentActionType.TaskStarted       => "▶ 작업 시작",
                AgentActionType.Thinking          => "💭 사고 중...",
                AgentActionType.Planning          => "📋 계획 수립 중...",
                AgentActionType.Executing         => "⚙ 실행 중...",
                AgentActionType.Reviewing         => "🔍 결과 검토 중...",
                AgentActionType.ToolUsing         => "🔧 도구 호출 중...",
                AgentActionType.ToolResult        => "📎 도구 결과 수신",
                AgentActionType.SubAgentSpawned   => "👥 서브에이전트 생성",
                AgentActionType.SubAgentCompleted => "✅ 서브에이전트 완료",
                AgentActionType.SubAgentFailed    => "❌ 서브에이전트 실패",
                AgentActionType.TaskCompleted     => "✅ 작업 완료",
                AgentActionType.TaskFailed        => "❌ 작업 실패",
                AgentActionType.ChatDelta         => "💬 응답 중...",
                AgentActionType.ChatFinal         => "대기 중",
                AgentActionType.Connected         => "대기 중",
                AgentActionType.Disconnected      => "연결 끊김",
                AgentActionType.AgentLifecycle    => "에이전트 작업 중...",
                _                                 => "대기 중",
            };
        }
    }
}
