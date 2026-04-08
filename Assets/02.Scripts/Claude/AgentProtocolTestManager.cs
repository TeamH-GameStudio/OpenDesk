using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Claude
{
    /// <summary>
    /// 새 프로토콜 테스트 전용 매니저.
    /// 미들웨어와의 WebSocket 통신을 직접 테스트하는 UI 컨트롤러.
    /// </summary>
    public class AgentProtocolTestManager : MonoBehaviour
    {
        [Header("WebSocket")]
        [SerializeField] private ClaudeWebSocketClient _wsClient;

        [Header("에이전트 선택")]
        [SerializeField] private Button _btnResearcher;
        [SerializeField] private Button _btnWriter;
        [SerializeField] private Button _btnAnalyst;

        [Header("채팅")]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private Button _sendButton;
        [SerializeField] private ScrollRect _chatScrollRect;
        [SerializeField] private RectTransform _chatContent;
        [SerializeField] private GameObject _userBubblePrefab;
        [SerializeField] private GameObject _aiBubblePrefab;
        [SerializeField] private GameObject _systemBubblePrefab;

        [Header("상태 표시")]
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _agentLabel;
        [SerializeField] private TMP_Text _thinkingText;
        [SerializeField] private Image _connectionDot;

        [Header("테스트 버튼 패널")]
        [SerializeField] private Button _btnSessionList;
        [SerializeField] private Button _btnSessionNew;
        [SerializeField] private Button _btnStatusRequest;
        [SerializeField] private Button _btnChatClear;
        [SerializeField] private Button _btnReconnect;

        [Header("Mock 테스트 버튼")]
        [SerializeField] private Button _btnMockThinking;
        [SerializeField] private Button _btnMockDelta;
        [SerializeField] private Button _btnMockMessage;
        [SerializeField] private Button _btnMockState;
        [SerializeField] private Button _btnMockSessionList;
        [SerializeField] private Button _btnMockFullFlow;

        [Header("FSM 테스트 버튼")]
        [SerializeField] private Button _btnFsmIdle;
        [SerializeField] private Button _btnFsmThinking;
        [SerializeField] private Button _btnFsmChatting;
        [SerializeField] private Button _btnFsmCompleted;
        [SerializeField] private Button _btnFsmError;
        [SerializeField] private Button _btnFsmDisconnected;
        [SerializeField] private Button _btnFsmTyping;

        [Header("우측 패널 (세션+채팅)")]
        [SerializeField] private GameObject _rightSessionChatPanel;
        [SerializeField] private Button _btnAddSession;

        [Header("세션/채팅 뷰 전환")]
        [SerializeField] private GameObject _sessionView;
        [SerializeField] private GameObject _chatView;
        [SerializeField] private Button _btnBack;
        [SerializeField] private RectTransform _sessionListContent;
        [SerializeField] private GameObject _sessionItemPrefab;

        [Header("로그")]
        [SerializeField] private ScrollRect _logScrollRect;
        [SerializeField] private RectTransform _logContent;
        [SerializeField] private GameObject _logEntryPrefab;

        private string _currentAgentId = "researcher";
        private StringBuilder _streamingBuffer = new();
        private bool _isStreaming;
        private GameObject _streamingBubble;

        // ── 세션별 채팅 기록 ──
        private string _currentSessionId;
        private readonly Dictionary<string, List<(bool isUser, string text)>> _sessionHistories = new();
        private readonly List<GameObject> _sessionTabs = new();

        // ── 초기화 ─────────────────────────────────────────────

        private void Start()
        {
            // 에이전트 선택 버튼
            _btnResearcher?.onClick.AddListener(() => SelectAgent("researcher"));
            _btnWriter?.onClick.AddListener(() => SelectAgent("writer"));
            _btnAnalyst?.onClick.AddListener(() => SelectAgent("analyst"));

            // 채팅
            _sendButton?.onClick.AddListener(OnSendClicked);
            _inputField?.onSubmit.AddListener(_ => OnSendClicked());

            // 테스트 버튼
            _btnSessionList?.onClick.AddListener(() =>
            {
                _wsClient.SendSessionList(_currentAgentId);
                AddLog($"-> session_list ({_currentAgentId})");
            });
            _btnSessionNew?.onClick.AddListener(() =>
            {
                _wsClient.SendSessionNew(_currentAgentId);
                AddLog($"-> session_new ({_currentAgentId})");
            });
            _btnStatusRequest?.onClick.AddListener(() =>
            {
                _wsClient.SendStatusRequest();
                AddLog("-> status_request");
            });
            _btnChatClear?.onClick.AddListener(() =>
            {
                _wsClient.SendChatClear(_currentAgentId);
                ClearChat();
                AddLog($"-> chat_clear ({_currentAgentId})");
            });
            _btnReconnect?.onClick.AddListener(async () =>
            {
                AddLog("-> 재연결 시도...");
                await _wsClient.DisconnectAsync();
                await _wsClient.ConnectAsync();
            });

            // WebSocket 이벤트 구독
            InitEventWrappers();
            _wsClient.OnConnectionChanged += HandleConnectionChanged;
            _wsClient.OnAgentState += _handleAgentState;
            _wsClient.OnAgentDelta += _handleAgentDelta;
            _wsClient.OnAgentMessage += _handleAgentMessage;
            _wsClient.OnAgentThinking += _handleAgentThinking;
            _wsClient.OnSessionList += _handleSessionList;
            _wsClient.OnSessionSwitched += _handleSessionSwitched;

            // Mock 테스트 버튼
            _btnMockThinking?.onClick.AddListener(MockThinking);
            _btnMockDelta?.onClick.AddListener(MockDelta);
            _btnMockMessage?.onClick.AddListener(MockMessage);
            _btnMockState?.onClick.AddListener(MockStateSequence);
            _btnMockSessionList?.onClick.AddListener(MockSessionList);
            _btnMockFullFlow?.onClick.AddListener(() => MockFullFlow().Forget());

            // FSM 테스트 버튼 — 실제 3D 에이전트 FSM 제어
            _btnFsmIdle?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.Idle, "Idle"));
            _btnFsmThinking?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.Thinking, "Thinking (의자 이동→앉기→고민)"));
            _btnFsmChatting?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.ChatDelta, "Chatting (SitToType→타이핑)"));
            _btnFsmCompleted?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.ChatFinal, "Completed (TypeToSit→대기→Cheering)"));
            _btnFsmError?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.TaskFailed, "Error (일어나기→에러 표정)"));
            _btnFsmDisconnected?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.Disconnected, "Disconnected"));
            _btnFsmTyping?.onClick.AddListener(() => ForceAgentFsm(Core.Models.AgentActionType.Executing, "Typing/도구 사용 (타이핑 모션)"));

            // 세션 추가 버튼
            _btnAddSession?.onClick.AddListener(CreateNewSession);

            // 뒤로가기 (채팅 → 세션 목록)
            _btnBack?.onClick.AddListener(BackToSessionList);

            // 우측 패널 초기 숨김, 세션 목록 뷰 활성
            if (_rightSessionChatPanel != null)
                _rightSessionChatPanel.SetActive(false);
            if (_sessionView != null) _sessionView.SetActive(true);
            if (_chatView != null) _chatView.SetActive(false);

            SelectAgent("researcher");
            UpdateConnectionUI(false);
            AddLog("Mock 모드 사용 가능 — 미들웨어 없이 테스트");
        }

        private void OnDestroy()
        {
            if (_wsClient != null)
            {
                _wsClient.OnConnectionChanged -= HandleConnectionChanged;
                _wsClient.OnAgentState -= _handleAgentState;
                _wsClient.OnAgentDelta -= _handleAgentDelta;
                _wsClient.OnAgentMessage -= _handleAgentMessage;
                _wsClient.OnAgentThinking -= _handleAgentThinking;
                _wsClient.OnSessionList -= _handleSessionList;
                _wsClient.OnSessionSwitched -= _handleSessionSwitched;
            }
        }

        // ── 에이전트 선택 ──────────────────────────────────────

        /// <summary>우측 세션+채팅 패널 표시</summary>
        public void ShowRightPanel()
        {
            if (_rightSessionChatPanel != null)
                _rightSessionChatPanel.SetActive(true);
        }

        /// <summary>우측 세션+채팅 패널 숨김</summary>
        public void HideRightPanel()
        {
            if (_rightSessionChatPanel != null)
                _rightSessionChatPanel.SetActive(false);
        }

        /// <summary>3D 에이전트 FSM을 직접 제어 (테스트 버튼용)</summary>
        private void ForceAgentFsm(Core.Models.AgentActionType actionType, string label)
        {
            var charCtrl = UnityEngine.Object.FindFirstObjectByType<Presentation.Character.AgentCharacterController>();
            if (charCtrl != null)
            {
                charCtrl.ForceState(actionType);
                AddLog($"[FSM] {label} → 에이전트 FSM 전환 완료");
            }
            else
            {
                AddLog($"[FSM] {label} → 에이전트를 찾을 수 없음");
            }

            // HUD 버블도 갱신
            var hud = FindAgentHUD();
            hud?.ApplyState(actionType);
        }

        /// <summary>미들웨어 수신 시 agent_state → UI + FSM 동시 갱신</summary>
        private void InjectFsmState(string state, string message = null, string tool = null)
        {
            // UI 핸들러 호출
            HandleAgentState(_currentAgentId, state, tool ?? "");

            // 3D 에이전트 FSM도 연동
            var actionType = state switch
            {
                "idle"     => Core.Models.AgentActionType.Idle,
                "thinking" => Core.Models.AgentActionType.Thinking,
                "working"  => Core.Models.AgentActionType.Executing,
                "error"    => Core.Models.AgentActionType.TaskFailed,
                _          => Core.Models.AgentActionType.Idle
            };

            var charCtrl = UnityEngine.Object.FindFirstObjectByType<Presentation.Character.AgentCharacterController>();
            charCtrl?.ForceState(actionType);
        }

        private void SelectAgent(string agentId)
        {
            _currentAgentId = agentId;
            if (_agentLabel != null)
                _agentLabel.SetText($"에이전트: {agentId}");

            // 버튼 하이라이트
            SetButtonHighlight(_btnResearcher, agentId == "researcher");
            SetButtonHighlight(_btnWriter, agentId == "writer");
            SetButtonHighlight(_btnAnalyst, agentId == "analyst");

            ClearChat();
            AddLog($"에이전트 선택: {agentId}");

            // 세션 목록 요청
            if (_wsClient.IsConnected)
                _wsClient.SendSessionList(agentId);
        }

        private void SetButtonHighlight(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = active ? new Color(0.13f, 0.59f, 0.95f) : new Color(0.3f, 0.3f, 0.3f);
        }

        // ── 채팅 ───────────────────────────────────────────────

        private void OnSendClicked()
        {
            if (_inputField == null || string.IsNullOrEmpty(_currentSessionId)) return;
            var text = _inputField.text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // 유저 버블 + 세션 기록
            SpawnBubble(_userBubblePrefab, text);
            SaveToSessionHistory(true, text);

            // 전송
            if (_wsClient.IsConnected)
            {
                _wsClient.SendChatMessage(_currentAgentId, text);
                AddLog($"-> chat_message: {text.Substring(0, Math.Min(text.Length, 40))}");
            }
            else
            {
                AddLog($"[로컬] 메시지: {text.Substring(0, Math.Min(text.Length, 40))}");
            }

            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        // ── 수신 핸들러 ────────────────────────────────────────

        private void HandleConnectionChanged(bool connected)
        {
            UpdateConnectionUI(connected);
            AddLog(connected ? "<- 연결됨" : "<- 연결 끊김");
        }

        // ── 이벤트 래퍼 (새 시그니처 -> 기존 핸들러 연결) ──
        private System.Action<string, string, string> _handleAgentState;
        private System.Action<string, string> _handleAgentDelta;
        private System.Action<string, string> _handleAgentMessage;
        private System.Action<string, string> _handleAgentThinking;
        private System.Action<string, string, SessionInfo[]> _handleSessionList;
        private System.Action<string, string, ChatHistoryEntry[]> _handleSessionSwitched;

        private void InitEventWrappers()
        {
            _handleAgentState = (agentId, state, tool) =>
                HandleAgentState(agentId, state, tool);
            _handleAgentDelta = (agentId, text) =>
                HandleAgentDelta(agentId, text);
            _handleAgentMessage = (agentId, message) =>
                HandleAgentMessage(agentId, message);
            _handleAgentThinking = (agentId, thinking) =>
                HandleAgentThinking(agentId, thinking);
            _handleSessionList = (agentId, currentSessionId, sessions) =>
                HandleSessionList(agentId, currentSessionId, sessions);
            _handleSessionSwitched = (agentId, sessionId, history) =>
                HandleSessionSwitched(agentId, sessionId, history);
        }

        private void HandleAgentState(string agentId, string state, string tool)
        {
            var stateKr = state switch
            {
                "thinking" => "생각 중...",
                "working"  => $"도구 사용 중: {tool}",
                "idle"     => "대기 중",
                "error"    => "오류 발생",
                _          => state
            };

            if (_statusText != null)
                _statusText.SetText($"[{agentId}] {stateKr}");

            var agentHud = FindAgentHUD();
            if (agentHud != null)
            {
                if (state == "working" && !string.IsNullOrEmpty(tool))
                {
                    var toolKr = tool switch
                    {
                        "web_search"  => "웹 검색 중...",
                        "read_file"   => "파일 읽는 중...",
                        "write_file"  => "파일 작성 중...",
                        "bash"        => "명령 실행 중...",
                        "list_files"  => "파일 탐색 중...",
                        _             => $"{tool} 사용 중..."
                    };
                    agentHud.ShowBubbleMessage(toolKr);
                }
                else if (state == "idle")
                {
                    agentHud.HideBubble();
                }
            }

            var actionType = state switch
            {
                "idle"     => Core.Models.AgentActionType.Idle,
                "thinking" => Core.Models.AgentActionType.Thinking,
                "working"  => Core.Models.AgentActionType.Executing,
                "error"    => Core.Models.AgentActionType.TaskFailed,
                _          => (Core.Models.AgentActionType?)null
            };
            if (actionType.HasValue)
            {
                var charCtrl = UnityEngine.Object.FindFirstObjectByType<Presentation.Character.AgentCharacterController>();
                charCtrl?.ForceState(actionType.Value);
            }

            AddLog($"<- agent_state: {agentId} = {state} {tool}");
        }

        private void HandleAgentThinking(string agentId, string thinking)
        {
            if (agentId != _currentAgentId) return;

            if (_thinkingText != null)
            {
                var preview = thinking?.Length > 80
                    ? thinking.Substring(0, 80) + "..."
                    : thinking;
                _thinkingText.SetText(preview ?? "");
                _thinkingText.gameObject.SetActive(true);
            }

            var agentHud = FindAgentHUD();
            if (agentHud != null)
            {
                var bubblePreview = thinking?.Length > 40
                    ? thinking.Substring(0, 40) + "..."
                    : thinking;
                agentHud.ShowBubbleMessage(bubblePreview);
            }

            AddLog($"<- agent_thinking: {agentId} ({thinking?.Length ?? 0}자)");
        }

        /// <summary>씬에서 현재 에이전트의 HUD 찾기</summary>
        private OpenDesk.Presentation.Character.AgentHUDController FindAgentHUD()
        {
            return UnityEngine.Object.FindFirstObjectByType<OpenDesk.Presentation.Character.AgentHUDController>();
        }

        private void HandleAgentDelta(string agentId, string text)
        {
            if (agentId != _currentAgentId) return;

            if (!_isStreaming)
            {
                _isStreaming = true;
                _streamingBuffer.Clear();
                _streamingBubble = SpawnBubble(_aiBubblePrefab, "");

                var charCtrl = UnityEngine.Object.FindFirstObjectByType<Presentation.Character.AgentCharacterController>();
                charCtrl?.ForceState(Core.Models.AgentActionType.ChatDelta);
            }

            _streamingBuffer.Append(text);
            UpdateBubbleText(_streamingBubble, _streamingBuffer.ToString());
        }

        private void HandleAgentMessage(string agentId, string message)
        {
            if (agentId != _currentAgentId) return;

            if (_isStreaming)
            {
                UpdateBubbleText(_streamingBubble, message);
                _isStreaming = false;
                _streamingBubble = null;
            }
            else
            {
                SpawnBubble(_aiBubblePrefab, message);
            }

            var charCtrl = UnityEngine.Object.FindFirstObjectByType<Presentation.Character.AgentCharacterController>();
            charCtrl?.ForceState(Core.Models.AgentActionType.ChatFinal);

            SaveToSessionHistory(false, message);

            AddLog($"<- agent_message: {agentId} ({message?.Length ?? 0}자)");
        }

        private void HandleSessionList(string agentId, string currentSessionId, SessionInfo[] sessions)
        {
            if (agentId != _currentAgentId) return;

            ClearSessionList();
            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    var item = SpawnSessionItem(session, session.session_id == currentSessionId);
                    var sid = session.session_id;
                    var btn = item.GetComponent<Button>();
                    if (btn != null)
                        btn.onClick.AddListener(() =>
                        {
                            _wsClient.SendSessionSwitch(_currentAgentId, sid);
                            AddLog($"-> session_switch: {sid}");
                        });
                }
            }

            AddLog($"<- session_list: {sessions?.Length ?? 0}개");
        }

        private void HandleSessionSwitched(string agentId, string sessionId, ChatHistoryEntry[] history)
        {
            if (agentId != _currentAgentId) return;

            _currentSessionId = sessionId;
            if (!_sessionHistories.ContainsKey(sessionId))
                _sessionHistories[sessionId] = new System.Collections.Generic.List<(bool, string)>();

            // 세션 목록 -> 채팅 뷰 전환
            if (_sessionView != null) _sessionView.SetActive(false);
            if (_chatView != null) _chatView.SetActive(true);

            ClearChat();
            if (history != null)
            {
                foreach (var entry in history)
                {
                    var prefab = entry.role == "user" ? _userBubblePrefab : _aiBubblePrefab;
                    SpawnBubble(prefab, entry.text);
                }
            }

            // 세션 목록도 갱신 요청
            _wsClient?.SendSessionList(_currentAgentId);

            AddLog($"<- session_switched: {sessionId} ({history?.Length ?? 0}개 메시지)");
        }

        // ── UI 유틸 ────────────────────────────────────────────

        private void UpdateConnectionUI(bool connected)
        {
            if (_connectionDot != null)
                _connectionDot.color = connected ? new Color(0.3f, 0.69f, 0.31f) : new Color(0.96f, 0.26f, 0.21f);
        }

        private GameObject SpawnBubble(GameObject prefab, string text)
        {
            if (prefab == null || _chatContent == null) return null;
            var go = Instantiate(prefab, _chatContent);
            UpdateBubbleText(go, text);
            ScrollToBottom(_chatScrollRect);
            return go;
        }

        private void UpdateBubbleText(GameObject bubble, string text)
        {
            if (bubble == null) return;
            var tmp = bubble.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.SetText(text ?? "");

            // ContentSizeFitter 강제 갱신
            LayoutRebuilder.ForceRebuildLayoutImmediate(_chatContent);
            ScrollToBottom(_chatScrollRect);
        }

        private void ClearChat()
        {
            if (_chatContent == null) return;
            for (int i = _chatContent.childCount - 1; i >= 0; i--)
                Destroy(_chatContent.GetChild(i).gameObject);

            _isStreaming = false;
            _streamingBubble = null;

            if (_thinkingText != null)
                _thinkingText.gameObject.SetActive(false);
        }

        private GameObject SpawnSessionItem(SessionInfo info, bool isActive)
        {
            if (_sessionItemPrefab == null || _sessionListContent == null) return null;
            var go = Instantiate(_sessionItemPrefab, _sessionListContent);
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                var title = string.IsNullOrEmpty(info.title) ? "(새 대화)" : info.title;
                tmp.SetText($"{title} ({info.message_count})");
            }
            var bg = go.GetComponent<Image>();
            if (bg != null)
                bg.color = isActive ? new Color(0.13f, 0.59f, 0.95f, 0.3f) : new Color(0.25f, 0.25f, 0.25f);
            return go;
        }

        private void ClearSessionList()
        {
            if (_sessionListContent == null) return;
            for (int i = _sessionListContent.childCount - 1; i >= 0; i--)
                Destroy(_sessionListContent.GetChild(i).gameObject);
        }

        private void AddLog(string message)
        {
            Debug.Log($"[ProtocolTest] {message}");

            if (_logEntryPrefab == null || _logContent == null) return;
            var go = Instantiate(_logEntryPrefab, _logContent);
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                tmp.SetText($"[{time}] {message}");
            }

            // 로그 최대 100개 유지
            while (_logContent.childCount > 100)
                Destroy(_logContent.GetChild(0).gameObject);

            ScrollToBottom(_logScrollRect);
        }

        private void ScrollToBottom(ScrollRect scrollRect)
        {
            if (scrollRect != null)
                Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        // ══════════════════════════════════════════════════════
        //  Mock 테스트 — 미들웨어 없이 프로토콜 검증
        // ══════════════════════════════════════════════════════

        private void MockThinking()
        {
            HandleAgentThinking(_currentAgentId,
                "사용자가 AI 트렌드를 물어봤으니 검색해봐야겠다. 최근 2026년 자료를 중심으로 찾아보자.");
            AddLog("[MOCK] agent_thinking 주입");
        }

        private void MockDelta()
        {
            var chunks = new[] { "안녕하세요! ", "AI 에이전트 ", "시장에 대해 ", "알려드릴게요." };
            foreach (var chunk in chunks)
                HandleAgentDelta(_currentAgentId, chunk);
            AddLog("[MOCK] agent_delta x4 주입");
        }

        private void MockMessage()
        {
            HandleAgentMessage(_currentAgentId,
                "<b>AI 에이전트 시장 동향</b>\n\n2026년 AI 에이전트 시장은 전년 대비 45% 성장하여 약 120억 달러 규모에 도달했습니다.\n\n주요 트렌드:\n1. 멀티에이전트 협업\n2. 자율적 도구 사용\n3. 장기 기억 시스템");
            AddLog("[MOCK] agent_message 주입 (TMP 포매팅)");
        }

        private void MockStateSequence()
        {
            HandleAgentState(_currentAgentId, "thinking", "");
            HandleAgentState(_currentAgentId, "working", "web_search");
            HandleAgentState(_currentAgentId, "idle", "");
            AddLog("[MOCK] agent_state 시퀀스 (thinking -> working -> idle)");
        }

        private void MockSessionList()
        {
            HandleSessionList(_currentAgentId, "s_mock001", new[]
            {
                new SessionInfo { session_id = "s_mock001", title = "AI 시장 분석", updated_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), message_count = 5 },
                new SessionInfo { session_id = "s_mock002", title = "경쟁사 조사", updated_at = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(), message_count = 12 },
                new SessionInfo { session_id = "s_mock003", title = "기술 트렌드 보고서", updated_at = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(), message_count = 3 },
            });
            AddLog("[MOCK] session_list_response 주입 (3개 세션)");
        }

        /// <summary>전체 대화 흐름을 시뮬레이션 (thinking -> delta x N -> message)</summary>
        private async UniTaskVoid MockFullFlow()
        {
            AddLog("[MOCK] === 전체 흐름 시작 ===");

            SpawnBubble(_userBubblePrefab, "AI 에이전트 시장 트렌드 알려줘");
            AddLog("[MOCK] 유저 메시지");

            HandleAgentState(_currentAgentId, "thinking", "");
            await UniTask.Delay(800, cancellationToken: destroyCancellationToken);

            HandleAgentThinking(_currentAgentId, "AI 에이전트 시장 트렌드를 찾아봐야겠다. 웹 검색을 해보자.");
            await UniTask.Delay(600, cancellationToken: destroyCancellationToken);

            HandleAgentDelta(_currentAgentId, "검색해볼게요!");
            HandleAgentMessage(_currentAgentId, "검색해볼게요!");
            await UniTask.Delay(300, cancellationToken: destroyCancellationToken);

            HandleAgentState(_currentAgentId, "working", "web_search");
            await UniTask.Delay(1500, cancellationToken: destroyCancellationToken);

            HandleAgentState(_currentAgentId, "thinking", "");
            HandleAgentThinking(_currentAgentId, "검색 결과를 정리해서 알려줘야겠다.");
            await UniTask.Delay(500, cancellationToken: destroyCancellationToken);

            var chunks = new[] { "3가지 ", "주요 자료를 ", "찾았어요!\n\n", "1. ", "멀티에이전트 ", "협업 ", "시장 ", "급성장\n", "2. ", "자율 도구 ", "사용 ", "확대\n", "3. ", "장기 기억 ", "시스템 ", "도입" };
            foreach (var chunk in chunks)
            {
                HandleAgentDelta(_currentAgentId, chunk);
                await UniTask.Delay(100, cancellationToken: destroyCancellationToken);
            }

            await UniTask.Delay(200, cancellationToken: destroyCancellationToken);
            HandleAgentMessage(_currentAgentId,
                "<b>3가지 주요 자료를 찾았어요!</b>\n\n1. <color=#4FC3F7>멀티에이전트 협업</color> 시장 급성장\n2. <color=#4FC3F7>자율 도구 사용</color> 확대\n3. <color=#4FC3F7>장기 기억 시스템</color> 도입");

            HandleAgentState(_currentAgentId, "idle", "");

            AddLog("[MOCK] === 전체 흐름 완료 ===");
        }

        // ══════════════════════════════════════════════════════
        //  세션 탭 관리
        // ══════════════════════════════════════════════════════

        private void CreateNewSession()
        {
            if (_wsClient != null && _wsClient.IsConnected)
            {
                _wsClient.SendSessionNew(_currentAgentId);
                AddLog($"-> session_new ({_currentAgentId})");
                return;
            }

            // Mock/로컬 세션 생성
            var sessionId = $"s_{_currentAgentId}_{_sessionHistories.Count + 1}";
            var title = $"대화 {_sessionHistories.Count + 1}";
            _sessionHistories[sessionId] = new List<(bool, string)>();
            RefreshSessionList();
            SwitchToSession(sessionId);
            AddLog($"새 세션 생성: {sessionId}");
        }

        private void RefreshSessionList()
        {
            if (_sessionListContent == null || _sessionItemPrefab == null) return;

            // 기존 아이템 제거
            for (int i = _sessionListContent.childCount - 1; i >= 0; i--)
                Destroy(_sessionListContent.GetChild(i).gameObject);

            int idx = 0;
            foreach (var kv in _sessionHistories)
            {
                idx++;
                var item = Instantiate(_sessionItemPrefab, _sessionListContent);
                item.SetActive(true);

                var tmp = item.GetComponentInChildren<TMP_Text>();
                if (tmp != null)
                    tmp.SetText($"대화 {idx} ({kv.Value.Count}개 메시지)");

                // 활성 세션 하이라이트
                var img = item.GetComponent<Image>();
                if (img != null)
                    img.color = kv.Key == _currentSessionId
                        ? new Color(0.13f, 0.59f, 0.95f, 0.3f)
                        : new Color(0.3f, 0.3f, 0.35f);

                var sid = kv.Key;
                var btn = item.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => SwitchToSession(sid));
            }
        }

        private void SwitchToSession(string sessionId)
        {
            _currentSessionId = sessionId;

            if (!_sessionHistories.ContainsKey(sessionId))
                _sessionHistories[sessionId] = new List<(bool, string)>();

            // 세션 목록 → 채팅 뷰 전환
            if (_sessionView != null) _sessionView.SetActive(false);
            if (_chatView != null) _chatView.SetActive(true);

            // 채팅 복원
            ClearChat();
            var history = _sessionHistories[sessionId];
            foreach (var (isUser, text) in history)
            {
                var prefab = isUser ? _userBubblePrefab : _aiBubblePrefab;
                SpawnBubble(prefab, text);
            }

            AddLog($"세션 진입: {sessionId} ({history.Count}개 메시지)");
        }

        private void BackToSessionList()
        {
            // 채팅 뷰 → 세션 목록
            if (_chatView != null) _chatView.SetActive(false);
            if (_sessionView != null) _sessionView.SetActive(true);
            RefreshSessionList();
        }

        private void SaveToSessionHistory(bool isUser, string text)
        {
            if (string.IsNullOrEmpty(_currentSessionId)) return;

            if (!_sessionHistories.ContainsKey(_currentSessionId))
                _sessionHistories[_currentSessionId] = new List<(bool, string)>();

            _sessionHistories[_currentSessionId].Add((isUser, text));
        }
    }
}
