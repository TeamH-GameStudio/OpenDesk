using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Skills;
using OpenDesk.Pipeline;
using OpenDesk.Presentation.Character;
using OpenDesk.SkillDiskette;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Chat
{
    /// <summary>
    /// 채팅 패널 — UI Toolkit (UXML/USS + UIDocument). ChatTest 의 ChatView 디자인을 차용하고
    /// OpenDesk 도메인 (IAiChatService 스트리밍 / ChatMessageStore 영속 / AgentEquipmentManager system prompt 합성) 에 통합.
    ///
    /// 외부 API: Open(sessionId, agentName, role) / Close()
    /// 인스펙터: 같은 GameObject 의 UIDocument.Source Asset 에 ChatPanelView.uxml 을 연결.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChatPanelView : MonoBehaviour
    {
        // ── DI ──
        private IAiChatService _chat;
        private IAgentSkillLoadoutService _loadoutService;
        private ISkillCatalogService _catalogService;
        private IAgentRepository _agentRepository;
        private IAgentStateService _agentStateService;

        [Inject]
        public void Construct(
            IAiChatService chat,
            IAgentSkillLoadoutService loadoutService = null,
            ISkillCatalogService catalogService = null,
            IAgentRepository agentRepository = null,
            IAgentStateService agentStateService = null)
        {
            _chat = chat;
            _loadoutService = loadoutService;
            _catalogService = catalogService;
            _agentRepository = agentRepository;
            _agentStateService = agentStateService;
        }

        private IDisposable _agentRepoSubscription;

        // ── UIDocument 참조 ──
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _messagesContainer;
        private ScrollView _scroll;
        private TextField _input;
        private VisualElement _inputInner;
        private VisualElement _inputRow;
        private Button _sendButton;
        private Button _stopButton;
        private Button _sessionsButton;
        private Button _closeButton;
        private Label _agentNameLabel;
        private Label _agentRoleLabel;
        private Label _statusLabel;
        private Label _emptyHint;

        // Sessions drawer
        private VisualElement _sessionsDrawer;
        private ScrollView _sessionsScroll;
        private Label _sessionsEmpty;
        private Button _sessionsNewButton;
        private Button _sessionsCloseButton;

        // Header extras
        private DropdownField _modelDropdown;
        private Button _newSessionButton;

        // Deco menu — 좌측 캐릭터 영역 우하단 햄버거 + 팝업 메뉴
        private VisualElement _decoMenu;
        private Button _decoMenuToggle;
        private Button _decoMenuSkillsButton;
        private Button _decoMenuAgentSettingsButton;
        private bool _decoMenuOpen;

        // Deco skills — 좌측 캐릭터 영역 좌하단 스킬 빠른보기
        private VisualElement _decoSkills;
        private Button _decoSkillsToggle;
        private VisualElement _decoSkillsPanel;
        private VisualElement _decoSkillsList;
        private Button _decoSkillsEmptyAdd;
        private Button _decoSkillsMore;
        private bool _decoSkillsOpen;
        private IDisposable _loadoutSubscription;
        private const int MAX_DECO_SKILL_BUBBLES = 5;

        // ── 상태 ──
        private ChatPanelViewModel _vm;
        private string _currentAgentId;
        private int _currentAgentIndex;
        private string _currentSessionId;
        private string _currentAgentName;
        private AgentRole _currentRole;
        private string _currentRoleText; // 위저드 원본(자유 텍스트), enum 매핑 안 되는 경우도 보존
        private bool _isOpen;
        private bool _sessionsOpen;
        private ChatMessageVM _streamingMessage;
        private string _streamingBuffer = string.Empty;
        // 채팅 버블 본문에 적용할 타이핑 페이서 — 캐릭터 입모양과 동일한 StreamingTextBuffer 를
        // 채팅 UI 에도 적용해 burst 가 그대로 튀지 않도록 한다. _streamingBuffer 는 raw 누적
        // (final.text 비교용 — 정확성 우선), pacer 는 화면 출력만 페이싱 (체감 자연스러움 우선).
        private OpenDesk.Characters.Talking.StreamingTextBuffer _streamingPacer;
        // 진행 중 응답이 속한 세션 — Close → Open 사이 또는 SwitchToSession 으로 _currentSessionId
        // 가 바뀌어도, 백그라운드 final 은 응답을 시작한 원래 세션에 저장되어야 한다.
        private string _streamingSessionId;
        // 마지막으로 표시한 activity row 텍스트 — Close 후 재진입 시 동일 라벨로 row 복원.
        private string _streamingActivityText;
        private bool _userAborted;

        // ── VM ↔ VisualElement 매핑 ──
        // OnMessageUpdated 가 "마지막 인덱스" 가정으로 잘못된 버블에 텍스트를 박는 버그가 있었다
        // (Close 후 백그라운드 delta 가 도착 → 다시 Open 시 LoadHistory 가 리스트를 reset →
        // _streamingMessage 가 dangling → 마지막=User 버블에 Agent 본문이 덮어쓰임).
        // 메시지 VM 마다 실제 element 를 1:1 추적해 안전하게 갱신한다.
        private readonly System.Collections.Generic.Dictionary<ChatMessageVM, VisualElement> _vmToElement = new();

        // ── Activity row (진행 중 thinking / tool 한 줄 표시) ──
        // 사용자 메시지와 assistant bubble 사이 inline 삽입. status 이벤트로 텍스트만 교체한다.
        // 응답 본문이 시작되면 dim, final 시 dot 이 회색으로 전환되어 흔적으로 남는다.
        private VisualElement _activityRow;
        private VisualElement _activityDot;
        private Label _activityText;

        // ── 모델 선택 ──
        private const string MODEL_PREF_KEY = "OpenDesk_AnthropicModel";
        private static readonly (string Display, string Id)[] AVAILABLE_MODELS =
        {
            ("Sonnet 4.6 (권장)",     "claude-sonnet-4-6"),
            ("Haiku 4.5 (저비용)",    "claude-haiku-4-5-20251001"),
            ("Opus 4.7",              "claude-opus-4-7"),
            ("Opus 4.7 (1M context)", "claude-opus-4-7[1m]"),
        };

        // ── 에이전트 연결 ──
        // 레거시 _linkedHUD (AgentHUDController) 는 제거됨 — AgentHudView 가 상태/호버를 자체 구독으로 처리.
        private AgentCharacterController _linkedCharCtrl;

        // ── 외부 이벤트 (세션 리스트 진입 등) ──
        public event Action SessionsRequested;

        /// <summary>
        /// 패널이 사용자 액션(close 버튼 등)으로 닫힐 때 발행.
        /// AgentOfficeInstaller 가 이를 구독해 ICameraFocusService.ReleaseFocus 로 카메라를 overview 로 복귀시킨다.
        /// AgentHudView 도 구독해 HUD 카드를 다시 표시한다.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// 패널이 열려 채팅 모드로 진입할 때 발행. AgentHudView 가 구독해 HUD 카드를 페이드 아웃.
        /// </summary>
        public event Action Opened;

        /// <summary>
        /// 좌측 캐릭터 영역 메뉴에서 "스킬" 항목을 선택했을 때 발행. 외부에서 SkillMarketView 등에 라우팅.
        /// </summary>
        public event Action SkillsRequested;

        /// <summary>
        /// 좌측 캐릭터 영역 메뉴에서 "에이전트 세팅" 항목을 선택했을 때 발행. 외부에서 위저드/세팅 패널에 라우팅.
        /// </summary>
        public event Action AgentSettingsRequested;

        // ══════════════════════════════════════════════════
        //  Lifecycle
        // ══════════════════════════════════════════════════

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildView();
            BindChatService();
            BindAgentRepository();
            BindLoadoutService();
            SetVisible(false);
        }

        private void OnDisable()
        {
            UnbindAgentRepository();
            UnbindChatService();
            UnbindLoadoutService();
            UnregisterCallbacks();
            _vm?.Dispose();
            _vm = null;
        }

        // ══════════════════════════════════════════════════
        //  Agent Repository 구독 — 위저드/외부 변경 시 system prompt 자동 재합성
        // ══════════════════════════════════════════════════

        private void BindAgentRepository()
        {
            if (_agentRepository == null) return;

            _agentRepoSubscription = _agentRepository.OnChanged
                .Subscribe(change =>
                {
                    // 현재 패널이 열려있고 변경된 에이전트가 *이* 에이전트일 때만 재합성.
                    // _currentAgentId 는 record.id 가 아니라 우선 ID 슬롯(드물게 name) — 양쪽 비교.
                    if (!_isOpen) return;
                    if (change.Kind != AgentRepositoryChangeKind.Saved) return;
                    if (change.Record == null) return;

                    var matchesById = !string.IsNullOrEmpty(_currentAgentId)
                        && string.Equals(change.AgentId, _currentAgentId, StringComparison.Ordinal);
                    var matchesByName = !string.IsNullOrEmpty(_currentAgentName)
                        && string.Equals(change.Record.name, _currentAgentName, StringComparison.Ordinal);

                    if (!matchesById && !matchesByName) return;

                    // CharacterController.Profile.Source 를 갱신해 다음 ApplySystemPrompt 에서
                    // 새 record 가 사용되도록 한다. 시각 슬롯(prefab/hud)은 그대로 유지.
                    if (_linkedCharCtrl?.Profile != null)
                    {
                        var t = typeof(AgentProfileSO);
                        const System.Reflection.BindingFlags F =
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        t.GetField("_source", F)?.SetValue(_linkedCharCtrl.Profile, change.Record);
                    }

                    ApplySystemPrompt();
                });
        }

        private void UnbindAgentRepository()
        {
            _agentRepoSubscription?.Dispose();
            _agentRepoSubscription = null;
        }

        // ══════════════════════════════════════════════════
        //  외부 API
        // ══════════════════════════════════════════════════

        public bool IsOpen => _isOpen;

        /// <summary>현재 진입한 에이전트의 식별자 (없으면 빈 문자열).</summary>
        public string CurrentAgentId => _currentAgentId ?? string.Empty;

        /// <summary>현재 진입한 에이전트의 표시 이름 (없으면 빈 문자열).</summary>
        public string CurrentAgentName => _currentAgentName ?? string.Empty;

        /// <summary>현재 진입한 에이전트의 역할 (enum 매핑 안 되는 경우 AgentRole.None).</summary>
        public AgentRole CurrentRole => _currentRole;

        /// <summary>
        /// 에이전트의 대화 히스토리로 진입. 마지막 세션이 있으면 자동 로드, 없으면 새 세션 생성.
        /// 일반적으로 OfficeHudView 의 에이전트 카드 클릭에서 호출.
        ///
        /// rawRole: 사용자가 위저드에서 입력한 원본 역할 문자열. enum 매핑되지 않는 자유 텍스트(예: "writer")
        /// 도 보존해야 system prompt 에 정확히 반영된다.
        /// </summary>
        public void OpenForAgent(string agentId, string agentName, AgentRole role, string rawRole = null)
        {
            _currentAgentId = agentId ?? agentName ?? string.Empty;
            _currentAgentIndex = StableIntFromId(_currentAgentId);
            _currentAgentName = agentName;
            _currentRole = role;
            _currentRoleText = rawRole;

            var sessionId = ResolveOrCreateLatestSession();
            Open(sessionId, agentName, role, rawRole);
        }

        /// <summary>
        /// 특정 세션을 직접 열기. 외부에서 sessionId 를 이미 알고 있을 때 (예: 알림).
        /// </summary>
        public void Open(string sessionId, string agentName, AgentRole role, string rawRole = null)
        {
            _currentSessionId = sessionId;
            _currentAgentName = agentName;
            _currentRole = role;
            if (!string.IsNullOrEmpty(rawRole))
                _currentRoleText = rawRole;

            // OpenForAgent 를 거치지 않고 직접 진입한 경우 agentId/index 가 비어있을 수 있으므로 채워준다.
            if (string.IsNullOrEmpty(_currentAgentId))
            {
                _currentAgentId = agentName ?? sessionId ?? string.Empty;
                _currentAgentIndex = StableIntFromId(_currentAgentId);
            }

            EnsureViewModel();
            _vm.AgentName = agentName;
            _vm.AgentRole = ResolveRoleDisplay();
            _vm.Status = string.Empty;

            FindLinkedAgent();
            LoadHistory();
            ResumeSession();

            CloseSessionsDrawer();
            SetVisible(true);
            var wasOpen = _isOpen;
            _isOpen = true;
            _input?.Focus();

            // 동일 패널 재진입(에이전트 전환) 시에도 Opened 발행 — HUD 가 이미 숨겨져 있어도 no-op 이라 안전.
            if (!wasOpen) Opened?.Invoke();
        }

        // 사용자 입력 원문(_currentRoleText) 이 있으면 그대로, 없으면 enum → 한글 fallback.
        private string ResolveRoleDisplay()
        {
            return !string.IsNullOrWhiteSpace(_currentRoleText)
                ? _currentRoleText
                : RoleDisplayName(_currentRole);
        }

        public void Close()
        {
            if (!_isOpen)
            {
                // 중복 Close 호출에도 이벤트 1회만 발행 (카메라가 ReleaseFocus 를 중복 호출해도 무해하지만 의도 명확화).
                SetVisible(false);
                return;
            }
            CloseDecoMenu();
            CloseDecoSkills();
            SetVisible(false);
            _isOpen = false;
            Closed?.Invoke();
        }

        // ══════════════════════════════════════════════════
        //  Build
        // ══════════════════════════════════════════════════

        private void BuildView()
        {
            if (_document == null)
            {
                Debug.LogError("[ChatPanelView] UIDocument 누락");
                return;
            }

            var rootEl = _document.rootVisualElement;
            if (rootEl == null)
            {
                Debug.LogError("[ChatPanelView] rootVisualElement null — UIDocument.Source Asset 에 ChatPanelView.uxml 을 연결하세요");
                return;
            }

            _root              = rootEl.Q<VisualElement>("chat-panel");
            _scroll            = rootEl.Q<ScrollView>("chat-scroll");
            // ScrollView 의 contentContainer 자체에 messages-container 스타일을 입힌다.
            // 별도 wrapper 를 두면 자식 행의 stretch/세로 stack 동작이 어긋나 메시지가
            // 겹쳐 보이는 현상이 생긴다. (Unity 2022 UI Toolkit ScrollView 의 layout 정책)
            _messagesContainer = _scroll?.contentContainer;
            if (_messagesContainer != null && !_messagesContainer.ClassListContains("messages-container"))
                _messagesContainer.AddToClassList("messages-container");
            _input             = rootEl.Q<TextField>("chat-input");
            _sendButton        = rootEl.Q<Button>("chat-send-button");
            _stopButton        = rootEl.Q<Button>("chat-stop-button");
            _sessionsButton    = rootEl.Q<Button>("chat-sessions-button");
            _closeButton       = rootEl.Q<Button>("chat-close-button");
            _agentNameLabel    = rootEl.Q<Label>("chat-agent-name");
            _agentRoleLabel    = rootEl.Q<Label>("chat-agent-role");
            _statusLabel       = rootEl.Q<Label>("chat-status");
            _emptyHint         = rootEl.Q<Label>("chat-empty-hint");
            _inputInner        = _input?.Q("unity-text-input");
            _inputRow          = rootEl.Q<VisualElement>(className: "chat-input-row");

            _sessionsDrawer       = rootEl.Q<VisualElement>("sessions-drawer");
            _sessionsScroll       = rootEl.Q<ScrollView>("sessions-drawer-scroll");
            _sessionsEmpty        = rootEl.Q<Label>("sessions-drawer-empty");
            _sessionsNewButton    = rootEl.Q<Button>("sessions-drawer-new");
            _sessionsCloseButton  = rootEl.Q<Button>("sessions-drawer-close");

            _modelDropdown        = rootEl.Q<DropdownField>("chat-model-dropdown");
            _newSessionButton     = rootEl.Q<Button>("chat-new-session-button");

            _decoMenu                     = rootEl.Q<VisualElement>("deco-menu");
            _decoMenuToggle               = rootEl.Q<Button>("deco-menu-toggle");
            _decoMenuSkillsButton         = rootEl.Q<Button>("deco-menu-skills");
            _decoMenuAgentSettingsButton  = rootEl.Q<Button>("deco-menu-agent-settings");

            _decoSkills          = rootEl.Q<VisualElement>("deco-skills");
            _decoSkillsToggle    = rootEl.Q<Button>("deco-skills-toggle");
            _decoSkillsPanel     = rootEl.Q<VisualElement>("deco-skills-panel");
            _decoSkillsList      = rootEl.Q<VisualElement>("deco-skills-list");
            _decoSkillsEmptyAdd  = rootEl.Q<Button>("deco-skills-empty-add");
            _decoSkillsMore      = rootEl.Q<Button>("deco-skills-more");

            if (_newSessionButton == null)
                Debug.LogWarning("[ChatPanelView] chat-new-session-button 미발견 — UXML 동기화 확인 필요");
            if (_modelDropdown == null)
                Debug.LogWarning("[ChatPanelView] chat-model-dropdown 미발견 — UXML 동기화 확인 필요");

            // Unity 6 TextField 기본 동작 차단 — Focus()/mouse up 시 자동 select-all 이
            // Shift+Enter 직후 캐럿 위치 강제와 충돌해 사용자가 "전체 선택" 을 보게 되는 현상 방지.
            // (cf. ITextSelection.selectAllOnFocus / selectAllOnMouseUp)
            if (_input != null)
            {
                _input.textSelection.selectAllOnFocus = false;
                _input.textSelection.selectAllOnMouseUp = false;
            }

            BindModelDropdown();
            RegisterCallbacks();
            EnsureViewModel();
        }

        private void BindModelDropdown()
        {
            if (_modelDropdown == null) return;

            var choices = new List<string>(AVAILABLE_MODELS.Length);
            foreach (var (display, _) in AVAILABLE_MODELS) choices.Add(display);
            _modelDropdown.choices = choices;

            var savedId = PlayerPrefs.GetString(MODEL_PREF_KEY, AVAILABLE_MODELS[0].Id);
            var idx = FindModelIndexById(savedId);
            _modelDropdown.index = idx;
            _modelDropdown.value = AVAILABLE_MODELS[idx].Display;
        }

        private static int FindModelIndexById(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < AVAILABLE_MODELS.Length; i++)
                if (AVAILABLE_MODELS[i].Id == id) return i;
            return 0;
        }

        private void EnsureViewModel()
        {
            if (_vm != null) return;
            _vm = new ChatPanelViewModel();
            _vm.MessageAdded   += OnMessageAdded;
            _vm.MessageUpdated += OnMessageUpdated;
            _vm.HistoryReset   += OnHistoryReset;
            _vm.CanSendChanged += OnCanSendChanged;
            _vm.AgentInfoChanged += UpdateAgentInfo;
            _vm.StatusChanged  += UpdateStatus;
        }

        private void RegisterCallbacks()
        {
            if (_input != null)
                _input.RegisterValueChangedCallback(OnDraftChanged);
            if (_sendButton != null)
                _sendButton.clicked += OnSendClicked;
            if (_stopButton != null)
                _stopButton.clicked += OnStopClicked;
            if (_sessionsButton != null)
                _sessionsButton.clicked += OnSessionsClicked;
            if (_closeButton != null)
                _closeButton.clicked += OnCloseClicked;
            if (_sessionsNewButton != null)
                _sessionsNewButton.clicked += OnSessionsNewClicked;
            if (_sessionsCloseButton != null)
                _sessionsCloseButton.clicked += CloseSessionsDrawer;
            if (_newSessionButton != null)
                _newSessionButton.clicked += OnSessionsNewClicked;
            if (_modelDropdown != null)
                _modelDropdown.RegisterValueChangedCallback(OnModelChanged);
            if (_decoMenuToggle != null)
                _decoMenuToggle.clicked += OnDecoMenuToggleClicked;
            if (_decoMenuSkillsButton != null)
                _decoMenuSkillsButton.clicked += OnDecoMenuSkillsClicked;
            if (_decoMenuAgentSettingsButton != null)
                _decoMenuAgentSettingsButton.clicked += OnDecoMenuAgentSettingsClicked;
            if (_decoSkillsToggle != null)
                _decoSkillsToggle.clicked += OnDecoSkillsToggleClicked;
            if (_decoSkillsEmptyAdd != null)
                _decoSkillsEmptyAdd.clicked += OnDecoSkillsAddClicked;
            if (_decoSkillsMore != null)
                _decoSkillsMore.clicked += OnDecoSkillsMoreClicked;
            if (_inputInner != null)
            {
                _inputInner.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
                _inputInner.RegisterCallback<FocusInEvent>(OnInputFocusIn);
                _inputInner.RegisterCallback<FocusOutEvent>(OnInputFocusOut);
            }
        }

        private void UnregisterCallbacks()
        {
            if (_input != null) _input.UnregisterValueChangedCallback(OnDraftChanged);
            if (_sendButton != null) _sendButton.clicked -= OnSendClicked;
            if (_stopButton != null) _stopButton.clicked -= OnStopClicked;
            if (_sessionsButton != null) _sessionsButton.clicked -= OnSessionsClicked;
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_sessionsNewButton != null) _sessionsNewButton.clicked -= OnSessionsNewClicked;
            if (_sessionsCloseButton != null) _sessionsCloseButton.clicked -= CloseSessionsDrawer;
            if (_newSessionButton != null) _newSessionButton.clicked -= OnSessionsNewClicked;
            if (_modelDropdown != null) _modelDropdown.UnregisterValueChangedCallback(OnModelChanged);
            if (_decoMenuToggle != null) _decoMenuToggle.clicked -= OnDecoMenuToggleClicked;
            if (_decoMenuSkillsButton != null) _decoMenuSkillsButton.clicked -= OnDecoMenuSkillsClicked;
            if (_decoMenuAgentSettingsButton != null) _decoMenuAgentSettingsButton.clicked -= OnDecoMenuAgentSettingsClicked;
            if (_decoSkillsToggle != null) _decoSkillsToggle.clicked -= OnDecoSkillsToggleClicked;
            if (_decoSkillsEmptyAdd != null) _decoSkillsEmptyAdd.clicked -= OnDecoSkillsAddClicked;
            if (_decoSkillsMore != null) _decoSkillsMore.clicked -= OnDecoSkillsMoreClicked;
            if (_inputInner != null)
            {
                _inputInner.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
                _inputInner.UnregisterCallback<FocusInEvent>(OnInputFocusIn);
                _inputInner.UnregisterCallback<FocusOutEvent>(OnInputFocusOut);
            }

            if (_vm != null)
            {
                _vm.MessageAdded   -= OnMessageAdded;
                _vm.MessageUpdated -= OnMessageUpdated;
                _vm.HistoryReset   -= OnHistoryReset;
                _vm.CanSendChanged -= OnCanSendChanged;
                _vm.AgentInfoChanged -= UpdateAgentInfo;
                _vm.StatusChanged  -= UpdateStatus;
            }
        }

        // ══════════════════════════════════════════════════
        //  Chat service 결합
        // ══════════════════════════════════════════════════

        private void BindChatService()
        {
            EnsureStreamingPacer();
            if (_chat == null) return;
            _chat.OnDelta  += HandleDelta;
            _chat.OnFinal  += HandleFinal;
            _chat.OnError  += HandleError;
            _chat.OnStatus += HandleStatus;
            _chat.OnToolUserAsk += HandleToolUserAsk;
        }

        private void UnbindChatService()
        {
            if (_streamingPacer != null)
                _streamingPacer.OnAppended -= HandlePacerAppended;
            if (_chat == null) return;
            _chat.OnDelta  -= HandleDelta;
            _chat.OnFinal  -= HandleFinal;
            _chat.OnError  -= HandleError;
            _chat.OnStatus -= HandleStatus;
            _chat.OnToolUserAsk -= HandleToolUserAsk;
        }

        private void HandleToolUserAsk(OpenDesk.Claude.Models.ToolUserAskMessage ask)
        {
            if (ask == null || string.IsNullOrEmpty(ask.tool_use_id)) return;
            // 현재 라운드의 텍스트 발화 placeholder 가 살아있다면 finalize 후 닫는다.
            // 닫지 않으면 다음 라운드의 delta 가 같은 placeholder 에 계속 누적되어
            // 카드(여기서 추가됨)보다 위에 답변이 표시되는 시각 순서 역전이 발생.
            FinalizeStreamingMessage();
            _vm?.AddToolAskMessage(ask);
        }

        private void FinalizeStreamingMessage()
        {
            if (_streamingMessage == null) return;
            // pacer 의 _accumulated StringBuilder + 잔여 큐를 모두 비운다.
            // Flush 만 호출하면 큐는 비지만 _accumulated 가 그대로 남아, 다음 라운드 첫 chunk 의
            // OnAppended 에서 "이전 라운드 텍스트 + 새 chunk" 가 누적된 채 emit 되어
            // placeholder #2 에 이전 라운드 본문이 다시 박히고, 최종 final 텍스트로 덮어쓰면서 사라지는 현상 발생.
            _streamingPacer?.Clear();
            if (!string.IsNullOrEmpty(_streamingBuffer))
                _vm.UpdateMessageText(_streamingMessage, MarkdownToTmpConverter.Convert(_streamingBuffer));
            _streamingMessage = null;
            _streamingBuffer = string.Empty;
        }

        // 같은 GO 에 StreamingTextBuffer 가 없으면 자동 부착 (prefab 작업 없이 동작 보장).
        // ChatPanelView 는 UI Toolkit (UIDocument + Label) 기반이라 TMP_Text 출력 surface 가 없다.
        // pacer 는 char-paced 누적 문자열을 OnAppended 로 emit 하는 타이머로만 쓰고,
        // 실제 화면 갱신은 ViewModel → MessageUpdated → UI Toolkit Label 경로로 흐른다.
        //
        // Awake 자동 탐색 (GetComponentInChildren<TMP_Text>) 이 이 GO 자식의 무관한 TMP_Text 를
        // 잘못 잡지 않도록 SetTarget(null) 로 명시 무효화 — 외부 구독 모드로 강제.
        private void EnsureStreamingPacer()
        {
            if (_streamingPacer != null) return;
            _streamingPacer = GetComponent<OpenDesk.Characters.Talking.StreamingTextBuffer>();
            if (_streamingPacer == null)
                _streamingPacer = gameObject.AddComponent<OpenDesk.Characters.Talking.StreamingTextBuffer>();
            _streamingPacer.SetTarget(null); // UI Toolkit 모드 — TMP 자동 탐색 무효화
            _streamingPacer.OnAppended += HandlePacerAppended;
        }

        // pacer 가 char 단위로 emit 한 누적 텍스트로 streaming 메시지 본문 갱신.
        // _streamingMessage 가 null 인 케이스(이전 응답 종료 후 잔여 emit) 는 무시 — dangling 방지.
        private void HandlePacerAppended(string accumulated)
        {
            if (_streamingMessage == null) return;
            // 스트리밍 중에도 라이브 마크다운 → TMP 리치텍스트 변환을 적용한다.
            // 미완성 토큰 (`**bo`, 미닫힌 코드블록, 헤더 없는 파이프 행) 은 변환 후보가 아니므로
            // raw 로 통과 → 다음 토큰 도착 시 자연스럽게 스냅 변환된다.
            _vm.UpdateMessageText(_streamingMessage, MarkdownToTmpConverter.Convert(accumulated));
            ScrollToBottom();
        }

        // ══════════════════════════════════════════════════
        //  히스토리 + Resume + system prompt
        // ══════════════════════════════════════════════════

        private void LoadHistory()
        {
            var messages = ChatMessageStore.Load(_currentSessionId);
            _vm.ResetHistory(messages);
            UpdateEmptyHint();
            // ResetHistory 가 _vm.Messages 를 새 인스턴스 리스트로 교체하면서 _streamingMessage 가
            // dangling 이 된다. 진행 중인 응답이 *이* 세션이라면 누적된 본문으로 streaming 메시지를
            // 다시 끼워넣어 사용자가 화면에서 응답이 사라지는 것을 막는다.
            RestoreStreamingIfActive();
        }

        // Close 후 재진입 또는 SwitchToSession 직후에 호출. _streamingSessionId 가 현재 세션과
        // 일치할 때만 진행 중인 응답을 화면에 복구한다. 다른 세션이면 백그라운드는 그대로 두고
        // store 에는 final 도착 시 원래 세션 키로 저장된다 (HandleFinal 의 storeKey 분기).
        private void RestoreStreamingIfActive()
        {
            if (_vm == null || !_vm.IsStreaming) return;
            if (string.IsNullOrEmpty(_streamingSessionId)) return;
            if (!string.Equals(_streamingSessionId, _currentSessionId, StringComparison.Ordinal)) return;

            var hasBuffer = !string.IsNullOrEmpty(_streamingBuffer);
            var bodyForBubble = hasBuffer ? _streamingBuffer : "...";
            _streamingMessage = _vm.AddMessage(ChatSender.Agent, bodyForBubble);

            // pacer 의 _accumulated 를 raw buffer 와 sync — 이후 들어오는 delta 가
            // 짧은 누적값을 OnAppended 로 emit 해서 본문이 줄어드는 사고를 막는다.
            _streamingPacer?.Reset(_streamingBuffer);

            // activity row 는 의미 있는 단계 흔적이 있을 때만 복원. 본문이 이미 흐르는데
            // 마지막 의미 있는 status 가 없다면 row 없이 본문만 보여준다.
            if (!string.IsNullOrEmpty(_streamingActivityText))
            {
                EnsureActivityRow(_streamingActivityText);
                if (hasBuffer) DimActivityRow();
            }
            else if (!hasBuffer)
            {
                // 응답이 아직 시작도 안 됐다면 "생각 중..." 으로 사용자에게 진행 신호 제공.
                EnsureActivityRow("생각 중...");
            }
        }

        private void ResumeSession()
        {
            if (_chat == null || !_chat.IsConnected) return;

            // 같은 세션에서 진행 중인 응답이 있다면 ClearHistory/ResumeSession 으로 server 측
            // context 를 reset 하면 응답이 망가진다. 다시 진입한 경우는 그냥 백그라운드 흐름을 잇는다.
            if (_vm != null && _vm.IsStreaming
                && !string.IsNullOrEmpty(_streamingSessionId)
                && string.Equals(_streamingSessionId, _currentSessionId, StringComparison.Ordinal))
                return;

            var convFile = ChatMessageStore.LoadConversationFile(_currentSessionId);
            if (convFile == null || convFile.Messages.Count == 0)
            {
                _chat.ClearHistory();
                ApplySystemPrompt();
                return;
            }

            var historyJson = JsonUtility.ToJson(convFile);
            _chat.ResumeSession(historyJson);
        }

        private void ApplySystemPrompt()
        {
            if (_chat == null || !_chat.IsConnected) return;

            var roleText = ResolveRoleDisplay();

            var equipment = _linkedCharCtrl?.Equipment;
            if (equipment != null)
            {
                // JSON-SSOT: Source 가 있으면 BindAgent — traits 까지 손실 없이 전달.
                var record = _linkedCharCtrl.Profile?.Source;
                if (record != null)
                {
                    equipment.BindAgent(record);
                }
                else
                {
                    var tone = _linkedCharCtrl.Profile != null
                        ? _linkedCharCtrl.Profile.Tone
                        : AgentTone.None;

#pragma warning disable CS0618 // 디자이너 SO (Source 없음) 폴백 경로.
                    if (!string.IsNullOrWhiteSpace(_currentRoleText))
                    {
                        equipment.SetAgentProfile(
                            _currentAgentName ?? string.Empty,
                            _currentRoleText,
                            ToneToKoreanLocal(tone),
                            agentId: _currentAgentName);
                    }
                    else
                    {
                        equipment.SetAgentProfile(_currentAgentName, _currentRole, tone, agentId: _currentAgentName);
                    }
#pragma warning restore CS0618
                }

                if (_loadoutService != null && _catalogService != null)
                    equipment.BindLoadoutService(_loadoutService, _catalogService, _currentAgentName);

                var pipeline = FindFirstObjectByType<OfficePipelineManager>();
                var prompt = pipeline != null
                    ? pipeline.BuildFullSystemPrompt(equipment)
                    : equipment.BuildSystemPrompt();

                if (!string.IsNullOrEmpty(prompt))
                {
                    _chat.SetSystemPrompt(prompt);

                    // Skill 인덱스만 system prompt 에 들어가므로, 본문 캐시는 별도 채널로 미들웨어에 전달.
                    // 미들웨어가 read_skill_body 도구를 호출할 때 메모리/디스크에서 본문을 반환한다.
                    _chat.SendSkillLoadout(equipment.BuildSkillLoadoutPayload());

                    var fileCount = pipeline?.Inbox?.FilePaths?.Count ?? 0;
                    Debug.Log($"[ChatPanelView] System prompt 적용 (역할='{roleText}', {prompt.Length}자, 스킬 {equipment.EquippedCount}개, 파일 {fileCount}개)");
                    return;
                }
            }

            // fallback — equipment 미발견 시 최소 system prompt 라도 역할 반영.
            _chat.SetSystemPrompt(
                $"당신은 '{_currentAgentName}'이라는 이름의 {roleText} 전문 에이전트입니다. " +
                "한국어로 대화하며, 사용자의 요청에 전문적으로 답변합니다.");
            Debug.Log($"[ChatPanelView] Fallback system prompt 적용 (역할='{roleText}', equipment 없음)");
        }

        private static string ToneToKoreanLocal(AgentTone tone) => tone switch
        {
            AgentTone.Friendly => "친절한",
            AgentTone.Logical  => "논리적인",
            AgentTone.Humorous => "유머러스한",
            AgentTone.Formal   => "격식체",
            AgentTone.Casual   => "편안한",
            _                   => string.Empty,
        };

        // ══════════════════════════════════════════════════
        //  에이전트 연결 (HUD/FSM)
        // ══════════════════════════════════════════════════

        private void FindLinkedAgent()
        {
            _linkedCharCtrl = null;

            // _currentAgentId(=profile.SessionId) 와 일치하는 캐릭터를 우선 매칭한다.
            // 다중 에이전트 오피스에서 "첫 번째 spawned" 를 잡으면 채팅 상태가 다른 캐릭터에 박히는 버그.
            var spawner = FindFirstObjectByType<AgentSpawner>();
            if (spawner != null && !string.IsNullOrEmpty(_currentAgentId)
                && spawner.SpawnedAgents.TryGetValue(_currentAgentId, out var match)
                && match?.ModelInstance != null)
            {
                _linkedCharCtrl = match.ModelInstance.GetComponent<AgentCharacterController>();
            }

            if (_linkedCharCtrl == null)
                _linkedCharCtrl = FindFirstObjectByType<AgentCharacterController>();
        }

        /// <summary>
        /// 채팅 흐름에 따른 에이전트 상태 발행. IAgentStateService 를 단일 경로로 사용해
        /// FSM(<see cref="AgentCharacterController"/>) 과 HUD(<see cref="OpenDesk.Presentation.UI.Hud.AgentHudView"/>)
        /// 가 동시에 반응하도록 한다. 서비스 미주입(테스트 씬 등) 시에는 캐릭터 FSM 만 직접 갱신.
        /// </summary>
        private void SetAgentState(AgentActionType state)
        {
            var sessionKey = !string.IsNullOrEmpty(_currentAgentId)
                ? _currentAgentId
                : _linkedCharCtrl != null ? _linkedCharCtrl.SessionId : null;

            if (_agentStateService != null && !string.IsNullOrEmpty(sessionKey))
            {
                _agentStateService.ForceState(sessionKey, state);
                return;
            }

            _linkedCharCtrl?.ForceState(state);
        }

        // ══════════════════════════════════════════════════
        //  입력 콜백
        // ══════════════════════════════════════════════════

        private void OnDraftChanged(ChangeEvent<string> evt)
        {
            if (_vm == null) return;
            _vm.Draft = evt.newValue ?? string.Empty;
        }

        private void OnSendClicked() => TrySend();

        private void OnStopClicked()
        {
            if (_chat == null) return;

            _userAborted = true;
            _chat.Abort();

            // 현재까지 받은 텍스트로 메시지 마감 — 사용자가 보고 있던 내용을 보존.
            var partial = !string.IsNullOrEmpty(_streamingBuffer)
                ? _streamingBuffer + "\n\n[중단됨]"
                : "[중단됨]";

            if (_streamingMessage != null)
                _vm.UpdateMessageText(_streamingMessage, MarkdownToTmpConverter.Convert(partial));

            var storeKey = !string.IsNullOrEmpty(_streamingSessionId) ? _streamingSessionId : _currentSessionId;
            if (!string.IsNullOrWhiteSpace(_streamingBuffer))
                ChatMessageStore.Append(storeKey, ChatSender.Agent, _streamingBuffer, _currentAgentName);

            SetAgentState(AgentActionType.Idle);
            // [중단됨] 표시가 메시지 본문에 박혔으므로 activity row 흔적은 제거.
            ClearActivityRow();
            UpdateStatus("중단됨");
            RestoreStatusAfterDelay().Forget();
            _streamingSessionId = null;
            _streamingActivityText = null;

            _streamingMessage = null;
            _streamingBuffer = string.Empty;
            _vm.IsStreaming = false;
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            var isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            if (!isEnter) return;

            // 한글 IME composition 진행 중 Enter 는 syllable commit 트리거 — OS 가 캡처하므로 우리는 통과시켜
            // IME 가 commit 만 수행하도록 한다. (compositionString 비어있을 때만 우리 핸들러가 발화)
            // legacy Input API 이긴 하지만 IME 상태 노출은 Input System 모드와 무관하게 동작.
            if (!string.IsNullOrEmpty(UnityEngine.Input.compositionString))
                return;

            // OS-cooked event 의 evt.shiftKey 는 IME 가 Enter 를 가로채고 commit 후 후속 이벤트를
            // 합성할 때 modifier 가 누락되는 케이스가 있다. (Windows 한글 IME 에서 자주 발생.)
            // 하드웨어 키 상태를 직접 읽어 Shift 감지를 보강 — New Input System 의 Keyboard.current.
            var shiftHeld = evt.shiftKey
                || (Keyboard.current != null && Keyboard.current.shiftKey.isPressed);

            // Enter 키는 우리가 항상 가로채 직접 처리. TextField multiline 의 기본 동작에 의존하면
            // Unity 6 에서 Shift+Enter 줄바꿈이 일관되게 발화하지 않는다 (multiline=true 라도 modifier 가 붙으면
            // 내부 manipulator 가 \n 삽입을 건너뜀).
            evt.StopPropagation();
            evt.PreventDefault();

            if (shiftHeld)
            {
                // Shift+Enter — 줄바꿈. IME composition commit 보장 위해 한 프레임 yield 후 cursor 위치에 \n 삽입.
                DeferredInsertNewlineAsync().Forget();
                return;
            }

            // 일반 Enter — IME composition 종료를 기다린 뒤 전송.
            DeferredSendAsync().Forget();
        }

        private async UniTaskVoid DeferredInsertNewlineAsync()
        {
            if (_input == null) return;

            // IME composition commit 완료 보장 — 마지막 글자가 \n 앞에 정상 반영되도록.
            await UniTask.Yield();
            if (_input == null) return;

            var current = _input.value ?? string.Empty;
            var cursor = _input.textSelection.cursorIndex;
            var select = _input.textSelection.selectIndex;
            var start = Math.Min(cursor, select);
            var end = Math.Max(cursor, select);

            if (start < 0) start = current.Length;
            if (start > current.Length) start = current.Length;
            if (end < start) end = start;
            if (end > current.Length) end = current.Length;

            var newValue = current.Substring(0, start) + "\n" + current.Substring(end);
            var newCaret = start + 1;

            _input.SetValueWithoutNotify(newValue);
            if (_vm != null) _vm.Draft = newValue;

            // 포커스 + 캐럿 위치 강제.
            // - selectAllOnFocus 를 BuildView 시점에 false 로 설정해 두었으므로 Focus() 가 select-all 을 발화하지 않음.
            // - SelectRange(cursor, selection) 으로 두 인덱스를 동일하게 → 선택 영역 없음 + 캐럿만 표시.
            //   (ITextSelection.selectIndex 는 read-only 인 케이스가 있어 SelectRange 가 정식 API.)
            _input.Focus();
            _input.textSelection.SelectRange(newCaret, newCaret);

            // 같은 프레임 후반에 Unity 내부 manipulator 가 selection 을 재차 건드는 경우 대비 — 한 번 더 강제.
            _input.schedule.Execute(() =>
            {
                if (_input == null) return;
                _input.textSelection.SelectRange(newCaret, newCaret);
            }).StartingIn(0);
        }

        private async UniTaskVoid DeferredSendAsync()
        {
            // 한 프레임 yield — IME composition commit 완료 보장.
            await UniTask.Yield();

            // TextField multiline 이 동시에 발화해 \n 이 끝에 추가됐을 수 있으므로 제거.
            // (보통 우리가 PreventDefault 로 막지만, 일부 IME/플랫폼에서 흘러들어오는 케이스 보강.)
            if (_input?.value != null)
            {
                var v = _input.value;
                if (v.EndsWith("\n"))
                {
                    var trimmed = v.TrimEnd('\n');
                    _input.SetValueWithoutNotify(trimmed);
                    if (_vm != null) _vm.Draft = trimmed;
                }
                else if (_vm != null && _vm.Draft != v)
                {
                    // IME commit 으로 _input.value 는 갱신됐지만 ViewModel Draft 가 아직 못 따라잡은 경우 동기화
                    _vm.Draft = v;
                }
            }

            TrySend();
        }

        private void OnInputFocusIn(FocusInEvent _) => _inputRow?.AddToClassList("chat-input-row--focused");
        private void OnInputFocusOut(FocusOutEvent _) => _inputRow?.RemoveFromClassList("chat-input-row--focused");

        private void OnSessionsClicked()
        {
            ToggleSessionsDrawer();
            SessionsRequested?.Invoke();
        }

        // ── Deco menu (좌측 캐릭터 영역 우하단) ──

        private void OnDecoMenuToggleClicked()
        {
            if (_decoMenu == null) return;
            _decoMenuOpen = !_decoMenuOpen;
            if (_decoMenuOpen) _decoMenu.AddToClassList("deco-menu--open");
            else _decoMenu.RemoveFromClassList("deco-menu--open");
        }

        private void CloseDecoMenu()
        {
            if (_decoMenu == null || !_decoMenuOpen) return;
            _decoMenuOpen = false;
            _decoMenu.RemoveFromClassList("deco-menu--open");
        }

        private void OnDecoMenuSkillsClicked()
        {
            CloseDecoMenu();
            SkillsRequested?.Invoke();
            Debug.Log("[ChatPanelView] SkillsRequested 발행 — 외부 라우팅 대기");
        }

        private void OnDecoMenuAgentSettingsClicked()
        {
            CloseDecoMenu();
            AgentSettingsRequested?.Invoke();
            Debug.Log("[ChatPanelView] AgentSettingsRequested 발행 — 외부 라우팅 대기");
        }

        // ── Deco skills (좌측 캐릭터 영역 좌하단 스킬 빠른보기) ──

        private void OnDecoSkillsToggleClicked()
        {
            if (_decoSkills == null) return;
            _decoSkillsOpen = !_decoSkillsOpen;
            if (_decoSkillsOpen)
            {
                RefreshSkillBubbles();
                _decoSkills.AddToClassList("deco-skills--open");
            }
            else
            {
                _decoSkills.RemoveFromClassList("deco-skills--open");
            }
        }

        private void CloseDecoSkills()
        {
            if (_decoSkills == null || !_decoSkillsOpen) return;
            _decoSkillsOpen = false;
            _decoSkills.RemoveFromClassList("deco-skills--open");
        }

        private void OnDecoSkillsAddClicked()
        {
            CloseDecoSkills();
            SkillsRequested?.Invoke();
            Debug.Log("[ChatPanelView] SkillsRequested (스킬 없음 → 추가) — 외부 라우팅 대기");
        }

        private void OnDecoSkillsMoreClicked()
        {
            CloseDecoSkills();
            SkillsRequested?.Invoke();
            Debug.Log("[ChatPanelView] SkillsRequested (더보기) — 외부 라우팅 대기");
        }

        private void OnDecoSkillBubbleClicked(SkillDescriptor descriptor)
        {
            CloseDecoSkills();
            SkillsRequested?.Invoke();
            Debug.Log($"[ChatPanelView] SkillsRequested (skill='{descriptor?.DisplayName}') — 외부 라우팅 대기");
        }

        // 현재 에이전트의 장착 스킬 목록을 fresh 하게 다시 그린다.
        // 0개 → "+" 추가 버튼만, 1~5개 → 카드, 6개 이상 → 앞 5개 + "..." 더보기.
        private void RefreshSkillBubbles()
        {
            if (_decoSkillsList == null) return;
            _decoSkillsList.Clear();

            var ids = ResolveEquippedSkillIds();
            var count = ids?.Count ?? 0;

            ToggleClass(_decoSkillsEmptyAdd, "deco-skills__chip--show", count == 0);
            ToggleClass(_decoSkillsMore, "deco-skills__chip--show", count > MAX_DECO_SKILL_BUBBLES);

            if (count == 0) return;

            var take = Math.Min(count, MAX_DECO_SKILL_BUBBLES);
            for (int i = 0; i < take; i++)
            {
                var id = ids[i];
                var descriptor = _catalogService?.GetById(id);
                var displayName = !string.IsNullOrEmpty(descriptor?.DisplayName) ? descriptor.DisplayName : id;
                var captured = descriptor;

                // chip: 평소 둥근 아이콘만 보이고, 호버 시 배경 바가 옆으로 펼쳐지며 이름 라벨이 등장.
                var chip = new Button(() => OnDecoSkillBubbleClicked(captured))
                {
                    text = string.Empty, // Button 기본 text 비움 — 자식 라벨만 사용
                    tooltip = descriptor?.Description ?? string.Empty,
                };
                chip.AddToClassList("deco-skills__chip");

                var iconLabel = new Label(IconCharForSkill(displayName));
                iconLabel.AddToClassList("deco-skills__chip-icon");
                chip.Add(iconLabel);

                var nameLabel = new Label(displayName);
                nameLabel.AddToClassList("deco-skills__chip-label");
                nameLabel.AddToClassList("od-body-sm");
                chip.Add(nameLabel);

                _decoSkillsList.Add(chip);
            }
        }

        // 아이콘에 표시할 한 글자 — DisplayName 첫 글자 (한글이면 한 음절, 영문이면 한 글자).
        private static string IconCharForSkill(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "?";
            return displayName.Substring(0, 1);
        }

        private IReadOnlyList<string> ResolveEquippedSkillIds()
        {
            if (_loadoutService == null) return Array.Empty<string>();
            var agentKey = !string.IsNullOrEmpty(_currentAgentId) ? _currentAgentId : _currentAgentName;
            if (string.IsNullOrEmpty(agentKey)) return Array.Empty<string>();
            var loadout = _loadoutService.GetLoadout(agentKey);
            return loadout?.EquippedSkillIds ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        private static void ToggleClass(VisualElement el, string className, bool on)
        {
            if (el == null) return;
            if (on) el.AddToClassList(className);
            else el.RemoveFromClassList(className);
        }

        private void BindLoadoutService()
        {
            if (_loadoutService == null) return;
            // 다른 화면(스킬 마켓 등)에서 장착이 바뀌면 메뉴가 열려있을 때만 즉시 반영.
            // 닫혀있다면 다음 토글 시점에 fresh data 로 그려지므로 갱신 비용 없음.
            _loadoutSubscription = _loadoutService.OnLoadoutChanged.Subscribe(_ =>
            {
                if (_decoSkillsOpen) RefreshSkillBubbles();
            });
        }

        private void UnbindLoadoutService()
        {
            _loadoutSubscription?.Dispose();
            _loadoutSubscription = null;
        }

        private void OnModelChanged(ChangeEvent<string> evt)
        {
            if (_modelDropdown == null) return;
            var idx = _modelDropdown.index;
            if (idx < 0 || idx >= AVAILABLE_MODELS.Length) return;
            var modelId = AVAILABLE_MODELS[idx].Id;
            // SetModel 이 _model 갱신 + PlayerPrefs 영속 + connected 면 미들웨어로 즉시 SendConfig 까지 수행.
            // 이전엔 PlayerPrefs 만 갱신했는데 MiddlewareChatService._model 은 생성자에서 한 번만 읽혀
            // 드롭다운 변경이 미들웨어로 전파되지 않는 결함이 있었다.
            _chat?.SetModel(modelId);
            UpdateStatus($"모델 변경: {modelId}");
            Debug.Log($"[ChatPanelView] 모델 변경 → {modelId}");
            RestoreStatusAfterDelay().Forget();
        }

        private void OnCloseClicked() => Close();

        private void OnSessionsNewClicked()
        {
            // _currentAgentName 미설정 시(예: 채팅 패널을 직접 Open 으로 열었거나 OpenForAgent 가 호출되지 않은 경로)
            // 빈 이름으로 세션을 생성해도 의미가 없으므로 사용자에게 안내.
            if (string.IsNullOrEmpty(_currentAgentName))
            {
                Debug.LogWarning("[ChatPanelView] 새 세션 생성 불가: 현재 에이전트가 설정되지 않음. OpenForAgent 가 호출되어야 함.");
                UpdateStatus("에이전트 미선택 — 사무실에서 에이전트를 먼저 클릭하세요");
                RestoreStatusAfterDelay().Forget();
                return;
            }

            var newIdx = AgentSessionStore.CreateSession(_currentAgentIndex, _currentAgentName, _currentRole);
            var newSession = AgentSessionStore.Load(newIdx);
            if (newSession == null)
            {
                Debug.LogWarning($"[ChatPanelView] 세션 로드 실패: idx={newIdx}");
                UpdateStatus("새 세션 생성 실패");
                RestoreStatusAfterDelay().Forget();
                return;
            }

            Debug.Log($"[ChatPanelView] 새 세션 생성: {newSession.SessionId} (agent={_currentAgentName}, idx={newIdx})");
            SwitchToSession(newSession.SessionId);
            UpdateStatus("새 대화 시작");
            RestoreStatusAfterDelay().Forget();
        }

        private void TrySend()
        {
            if (_vm == null || !_vm.CanSend) return;
            var text = _vm.Draft.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _vm.ClearDraft();
            if (_input != null) _input.SetValueWithoutNotify(string.Empty);

            SendAsync(text).Forget();
        }

        private async UniTaskVoid SendAsync(string text)
        {
            _vm.IsStreaming = true;
            // 응답 진행 중 상태는 헤더 라벨이 아니라 메시지 영역 inline activity row 가 전달.
            UpdateStatus(string.Empty);

            // 사용자 메시지: 영속 + VM 추가
            ChatMessageStore.Append(_currentSessionId, ChatSender.User, text, _currentAgentName);
            _vm.AddMessage(ChatSender.User, text);
            UpdateEmptyHint();

            SetAgentState(AgentActionType.Thinking);

            // 스킬 변화 반영
            ApplySystemPrompt();

            if (_chat != null && _chat.IsConnected)
            {
                _streamingBuffer = string.Empty;
                _streamingSessionId = _currentSessionId;
                _streamingActivityText = "생각 중...";
                _userAborted = false;
                // 새 발화 — 이전 페이서 잔여 큐/누적 정리. dangling delta 방지.
                _streamingPacer?.Clear();
                // 순서: User bubble → activity row → assistant bubble placeholder.
                EnsureActivityRow(_streamingActivityText);
                _streamingMessage = _vm.AddMessage(ChatSender.Agent, "...");
                _chat.SendMessage(text);
            }
            else
            {
                ClearActivityRow();
                _vm.AddMessage(ChatSender.System,
                    "AI 백엔드에 연결되지 않았습니다. (CLI: 미들웨어 실행 / API: Anthropic 키 설정 확인)");
                SetAgentState(AgentActionType.Idle);
                _vm.IsStreaming = false;
                UpdateStatus(string.Empty);
            }

            await UniTask.Yield();
        }

        // ══════════════════════════════════════════════════
        //  IAiChatService 핸들러
        // ══════════════════════════════════════════════════

        private void HandleDelta(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // tool_use 후 새 라운드의 첫 delta — placeholder 가 finalize 된 상태.
            // 카드 아래에 새 답변이 자연스럽게 추가되도록 새 placeholder 를 컨테이너 끝에 append.
            if (_streamingMessage == null)
            {
                _streamingMessage = _vm.AddMessage(ChatSender.Agent, string.Empty);
            }

            if (string.IsNullOrEmpty(_streamingBuffer))
            {
                SetAgentState(AgentActionType.ChatDelta);
                // 응답 본문이 시작됐다는 시각 신호 — activity row 는 흐릿하게.
                DimActivityRow();
            }

            // raw 누적 — final.text 길이 비교용. 페이싱 큐와 별개로 즉시 누적해야
            // HandleFinal 에서 partial final.text 를 정확히 판별할 수 있다.
            _streamingBuffer += text;
            // 화면 출력은 pacer 큐로 흘려보낸다 (char 단위 페이싱 → 자연스러운 타이핑).
            // 큐가 길어지면 burst 가속이 자동으로 따라잡으므로 사용자 체감 응답 속도는 유지.
            // pacer 가 OnAppended → HandlePacerAppended → _vm.UpdateMessageText 로 라우팅.
            if (_streamingPacer != null)
                _streamingPacer.Enqueue(text);
            else
                _vm.UpdateMessageText(_streamingMessage, MarkdownToTmpConverter.Convert(_streamingBuffer)); // 안전망 — pacer 미부착 환경
        }

        private void HandleFinal(string text, float cost)
        {
            // 미들웨어의 final.text 정책 편차로 응답이 잘려 보이는 케이스 방지.
            //  - anthropic_api: tool_use 다중 round 종료 시 *마지막 round* 의 final_message.content 만 반환 →
            //    delta 로 누적된 이전 round 텍스트가 final.text 에 빠짐.
            //  - anthropic_cli: result 메시지의 `result` 필드가 partial 일 수 있음 (timeout, accumulated_text fallback).
            // 정책: delta 누적이 더 길면 buffer 를 우선 — delta 스트림이 사용자가 본 실시간 응답과 정확히 일치.
            var bufferLen = _streamingBuffer?.Length ?? 0;
            var textLen = text?.Length ?? 0;
            // 선택된 텍스트가 raw buffer 든 미들웨어 final 이든 항상 Convert 를 통과시켜 일관된 표현 유지.
            // 미들웨어 final 은 이미 markdown_to_tmp 적용본이지만 마크다운 마커가 없어 재변환은 no-op.
            var chosen = bufferLen >= textLen ? (_streamingBuffer ?? string.Empty) : (text ?? string.Empty);
            var finalText = MarkdownToTmpConverter.Convert(chosen);

            // pacer 큐에 남은 잔여 토큰이 있으면 즉시 모두 출력 (체감 지연 제거).
            // Flush 가 OnAppended 를 한 번 더 emit → _vm 본문이 잠깐 buffer 까지 갔다가
            // 아래 finalText (markdown_to_tmp 적용본) 로 덮어쓰여 자연스럽게 정착.
            _streamingPacer?.Flush();

            if (_streamingMessage != null)
                _vm.UpdateMessageText(_streamingMessage, finalText);

            // 응답을 시작한 *원래* 세션에 저장 — Close → Open 도중 SwitchToSession 으로
            // _currentSessionId 가 다른 세션으로 바뀌었어도 정확한 세션에 누적된다.
            var storeKey = !string.IsNullOrEmpty(_streamingSessionId) ? _streamingSessionId : _currentSessionId;
            if (!string.IsNullOrEmpty(finalText))
                ChatMessageStore.Append(storeKey, ChatSender.Agent, finalText, _currentAgentName);

            SetAgentState(AgentActionType.ChatFinal);
            // 마지막 활동 한 줄을 회색 dot + dim 상태로 그대로 흔적 남김. 헤더 status 는 비움.
            FinishActivityRow();
            UpdateStatus(string.Empty);
            _streamingSessionId = null;
            _streamingActivityText = null;

            var pipeline = FindFirstObjectByType<OfficePipelineManager>();
            if (pipeline?.Outbox != null && pipeline.Inbox != null
                && pipeline.Inbox.FilePaths.Count > 0
                && !string.IsNullOrEmpty(finalText))
            {
                pipeline.Outbox.ReceiveResult(finalText);
            }

            RestoreStatusAfterDelay().Forget();

            _streamingMessage = null;
            _streamingBuffer = string.Empty;
            _vm.IsStreaming = false;
            ScrollToBottom();
        }

        private void HandleError(string errorMsg)
        {
            // 사용자가 stop 버튼을 눌러 발생한 cancel 에코는 이미 UI 가 [중단됨] 으로 마무리 했으므로 노이즈 억제.
            if (_userAborted)
            {
                _userAborted = false;
                return;
            }

            // pacer 큐의 잔여 토큰을 버린다 — 오류 메시지가 박힐 자리에 잔여가 끼면 안 됨.
            _streamingPacer?.Clear();

            if (_streamingMessage != null)
                _vm.UpdateMessageText(_streamingMessage, $"[오류] {errorMsg}");
            else
                _vm.AddMessage(ChatSender.System, $"[오류] {errorMsg}");

            SetAgentState(AgentActionType.Idle);
            // 오류 메시지가 본문에 박혔으므로 진행 표시 흔적은 제거 (중복 피함).
            ClearActivityRow();
            UpdateStatus("대기 중");

            _streamingMessage = null;
            _streamingBuffer = string.Empty;
            _streamingSessionId = null;
            _streamingActivityText = null;
            _vm.IsStreaming = false;
        }

        private void HandleStatus(string statusText)
        {
            // 응답 진행 중 status 는 메시지 영역 activity row 로 흐르고, 헤더는 비워둔다.
            // 비스트리밍 시(모델 변경/세션 알림 등) 헤더 status 로 표시.
            if (_vm != null && _vm.IsStreaming)
            {
                // "응답 중..." 류는 본문 delta 가 곧 흘러나와 같은 의미를 전달하므로 표시하지 않는다.
                // 도구 호출 / 도구 결과 같은 의미 있는 단계만 activity row 에 남긴다.
                if (IsResponseStartStatus(statusText)) return;

                _streamingActivityText = statusText;
                // 패널이 닫혀있는 동안에는 activity row 자체가 없으므로 텍스트만 캐시해두고,
                // 다음 Open 시 RestoreStreamingIfActive 가 같은 텍스트로 row 를 복원한다.
                if (_isOpen) UpdateActivityRow(statusText);
            }
            else
            {
                UpdateStatus(statusText);
            }
        }

        private static bool IsResponseStartStatus(string statusText)
        {
            if (string.IsNullOrEmpty(statusText)) return false;
            var s = statusText.Trim();
            // anthropic_cli 가 첫 text 블록 진입 시 발화하는 라벨. 한/영 변형 보강.
            return s.StartsWith("응답 중", StringComparison.Ordinal)
                || s.StartsWith("Responding", StringComparison.OrdinalIgnoreCase);
        }

        private async UniTaskVoid RestoreStatusAfterDelay()
        {
            await UniTask.Delay(3500, cancellationToken: destroyCancellationToken);
            if (_vm != null && !_vm.IsStreaming)
                UpdateStatus(string.Empty);
        }

        // ══════════════════════════════════════════════════
        //  VM → View 렌더링
        // ══════════════════════════════════════════════════

        private void OnHistoryReset()
        {
            if (_messagesContainer == null) return;
            _messagesContainer.Clear();
            _vmToElement.Clear();
            // 히스토리 리셋 시 진행 표시 흔적도 같이 제거. 다음 응답에서 새 row 가 생성된다.
            ClearActivityRow();
            foreach (var msg in _vm.Messages)
            {
                var el = BuildMessageElement(msg, animateIn: false);
                _messagesContainer.Add(el);
                _vmToElement[msg] = el;
            }
            ScrollToBottom();
        }

        private void OnMessageAdded(ChatMessageVM msg)
        {
            if (_messagesContainer == null) return;
            var el = BuildMessageElement(msg, animateIn: true);
            _messagesContainer.Add(el);
            _vmToElement[msg] = el;
            UpdateEmptyHint();
            ScrollToBottom();
        }

        private void OnMessageUpdated(ChatMessageVM msg)
        {
            // VM 인스턴스 ↔ 실제 element 직접 매핑으로 안전 갱신.
            // dangling _streamingMessage (Close 후 LoadHistory 가 리스트를 reset 한 경우의 옛 VM)
            // 이 들어오면 dictionary 에 없으므로 자연스럽게 skip — 이전에 "마지막 인덱스" 가정으로
            // User 버블에 Agent 본문이 덮어쓰이던 버그를 차단한다.
            if (_messagesContainer == null || msg == null) return;
            if (!_vmToElement.TryGetValue(msg, out var el) || el == null) return;
            var bubble = el.Q<Label>(className: "message__bubble");
            if (bubble != null) bubble.text = msg.Body;
        }

        // ══════════════════════════════════════════════════
        //  Activity row — 진행 중 thinking / tool 한 줄 표시
        // ══════════════════════════════════════════════════

        private void EnsureActivityRow(string text)
        {
            if (_messagesContainer == null) return;

            if (_activityRow == null)
            {
                _activityRow = new VisualElement();
                _activityRow.AddToClassList("message");
                _activityRow.AddToClassList("message--activity");

                _activityDot = new VisualElement();
                _activityDot.AddToClassList("activity__dot");
                _activityRow.Add(_activityDot);

                _activityText = new Label();
                _activityText.AddToClassList("activity__text");
                _activityRow.Add(_activityText);

                _messagesContainer.Add(_activityRow);
            }

            if (_activityText != null) _activityText.text = text ?? string.Empty;
            // 새 단계 진입 — dim/done 클래스 초기화해 활기 있는 상태로 복귀.
            _activityRow.RemoveFromClassList("message--activity--dim");
            _activityDot?.RemoveFromClassList("activity__dot--done");
            ScrollToBottom();
        }

        private void UpdateActivityRow(string text)
        {
            if (_activityRow == null) { EnsureActivityRow(text); return; }
            if (_activityText != null) _activityText.text = text ?? string.Empty;
            _activityRow.RemoveFromClassList("message--activity--dim");
        }

        private void DimActivityRow()
        {
            _activityRow?.AddToClassList("message--activity--dim");
        }

        // 응답이 정상 완료된 경우 — dot 을 회색으로, dim 상태로 흔적 남김. 참조는 끊어
        // 다음 응답에서 새 row 가 사용자 메시지 직후에 깔리도록 한다.
        private void FinishActivityRow()
        {
            if (_activityRow == null) return;
            _activityDot?.AddToClassList("activity__dot--done");
            _activityRow.AddToClassList("message--activity--dim");
            _activityRow = null;
            _activityDot = null;
            _activityText = null;
        }

        // 오류/중단 등 별도 메시지가 본문에 들어가는 경우 — row 자체를 hierarchy 에서 제거.
        private void ClearActivityRow()
        {
            if (_activityRow == null) return;
            _activityRow.RemoveFromHierarchy();
            _activityRow = null;
            _activityDot = null;
            _activityText = null;
        }

        private void OnCanSendChanged(bool canSend)
        {
            if (_sendButton != null) _sendButton.SetEnabled(canSend);

            // 스트리밍 중일 때는 send 를 숨기고 stop 노출.
            var streaming = _vm != null && _vm.IsStreaming;
            if (_inputRow != null)
            {
                if (streaming) _inputRow.AddToClassList("chat-input-row--streaming");
                else _inputRow.RemoveFromClassList("chat-input-row--streaming");
            }
            if (_stopButton != null) _stopButton.SetEnabled(streaming);
        }

        private void UpdateAgentInfo()
        {
            if (_agentNameLabel != null) _agentNameLabel.text = _vm.AgentName;
            if (_agentRoleLabel != null) _agentRoleLabel.text = _vm.AgentRole;
        }

        private void UpdateStatus(string text)
        {
            _vm.Status = text;
            if (_statusLabel != null) _statusLabel.text = text ?? string.Empty;
        }

        private void UpdateEmptyHint()
        {
            if (_emptyHint == null) return;
            _emptyHint.style.display = _vm.Messages.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement BuildMessageElement(ChatMessageVM msg, bool animateIn)
        {
            var row = new VisualElement();
            row.AddToClassList("message");
            switch (msg.Sender)
            {
                case ChatSender.User:    row.AddToClassList("message--user");      break;
                case ChatSender.Agent:   row.AddToClassList("message--assistant"); break;
                case ChatSender.System:  row.AddToClassList("message--system");    break;
                case ChatSender.ToolAsk: row.AddToClassList("message--tool-ask");  break;
                default:                 row.AddToClassList("message--system");    break;
            }

            if (msg.Sender == ChatSender.ToolAsk)
            {
                BuildToolAskCard(row, msg);
                if (animateIn)
                {
                    row.style.opacity = 0f;
                    row.schedule.Execute(() => row.style.opacity = 1f).StartingIn(16);
                }
                return row;
            }

            if (msg.Sender == ChatSender.Agent && !string.IsNullOrEmpty(_vm.AgentName))
            {
                var senderLabel = new Label(_vm.AgentName);
                senderLabel.AddToClassList("message__sender-label");
                row.Add(senderLabel);
            }

            // Bubble wrapper — max-width 를 wrapper 에 두면 안쪽 Label 이 wrapper 의 확정 픽셀 폭
            // 안에서 measure → wrap → height 재계산이 정상 발화한다. Label 에 직접 percent max-width 를
            // 주면 Unity UI Toolkit (2022.x ~ 6.x) 이 첫 패스에서 intrinsic width 기준으로 height 를 잡고
            // wrap 후 갱신하지 않아 마지막 줄이 bubble bounds 밖으로 렌더링되어 잘리는 layout bug 발생.
            var bubbleWrap = new VisualElement();
            bubbleWrap.AddToClassList("message__bubble-wrap");

            var bubble = new Label(msg.Body);
            bubble.AddToClassList("message__bubble");
            bubble.style.whiteSpace = WhiteSpace.Normal;
            // 마우스 드래그로 본문 선택 + Ctrl/Cmd+C 복사. 스트리밍 중 text 가 갱신되면 선택이
            // 리셋되는 건 UI Toolkit 기본 동작이지만, 스트리밍 종료 후엔 안정적으로 긁힌다.
            bubble.selection.isSelectable = true;
            bubbleWrap.Add(bubble);
            row.Add(bubbleWrap);

            // 메시지 액션 (복사 / 수정) — 상시 표시. System / ToolAsk 메시지는 액션 없음.
            if (msg.Sender != ChatSender.System && msg.Sender != ChatSender.ToolAsk)
                row.Add(BuildMessageActions(msg));

            if (animateIn)
            {
                // opacity fade-in 만 사용. translate 인라인은 layout 측정 직전에 적용되면서
                // ScrollView contentContainer 안에서 형제 행과 z-order 가 꼬일 위험이 있어 제거.
                row.style.opacity = 0f;
                row.schedule.Execute(() => row.style.opacity = 1f).StartingIn(16);
            }
            return row;
        }

        /// <summary>
        /// 인터랙티브 도구 카드. payload_kind 에 따라 분기:
        ///   - "capability_pick" : route_capability 의 인라인 도구 선택 카드.
        ///                         플러그인 타일 + "다음부터 자동" 토글로 렌더 (askUserQuestion 과 동일 시각 패턴 + remember flag).
        ///   - 그 외 (ask_user)  : AskUserQuestion 의 인라인 버전 — 라디오/체크박스 + 자유 입력 + 전송.
        ///
        /// 응답 후에는 옵션/입력이 숨겨지고 응답 요약 + "응답됨" 배지가 표시된다 (collapsed view).
        /// </summary>
        private void BuildToolAskCard(VisualElement row, ChatMessageVM msg)
        {
            var ask = msg.AskPayload;
            if (ask == null) return;

            // route_capability 의 capability_pick 카드는 별도 렌더링.
            if (string.Equals(ask.payload_kind, "capability_pick", StringComparison.Ordinal))
            {
                BuildCapabilityPickerCard(row, msg);
                return;
            }

            // 카드 자체가 outer ScrollView 와 캡처 경합을 일으키지 않도록.
            row.pickingMode = PickingMode.Position;

            var card = new VisualElement();
            card.AddToClassList("tool-ask");
            row.Add(card);

            // 헤더 chip
            if (!string.IsNullOrEmpty(ask.header))
            {
                var header = new Label(ask.header);
                header.AddToClassList("tool-ask__header");
                card.Add(header);
            }

            // 질문 본문
            var question = new Label(ask.question ?? string.Empty);
            question.AddToClassList("tool-ask__question");
            question.style.whiteSpace = WhiteSpace.Normal;
            card.Add(question);

            // 옵션 컨테이너 (collapsed 상태에선 hide)
            var optionsRoot = new VisualElement();
            optionsRoot.AddToClassList("tool-ask__options");
            card.Add(optionsRoot);

            // 옵션 위젯 — single 은 라디오, multi 는 체크박스. App UI Radio 의존 회피로 Toggle 로 갈음.
            var selectedToggles = new List<Toggle>();
            if (ask.options != null)
            {
                foreach (var opt in ask.options)
                {
                    if (opt == null || string.IsNullOrEmpty(opt.label)) continue;
                    var optionRow = new VisualElement();
                    optionRow.AddToClassList("tool-ask__option-row");

                    var toggle = new Toggle(opt.label);
                    toggle.userData = opt.label;
                    toggle.AddToClassList(ask.multi_select ? "tool-ask__check" : "tool-ask__radio");

                    if (!ask.multi_select)
                    {
                        toggle.RegisterValueChangedCallback(evt =>
                        {
                            if (!evt.newValue) return;
                            foreach (var other in selectedToggles)
                                if (other != toggle && other.value) other.SetValueWithoutNotify(false);
                        });
                    }

                    selectedToggles.Add(toggle);
                    optionRow.Add(toggle);

                    if (!string.IsNullOrEmpty(opt.description))
                    {
                        var desc = new Label(opt.description);
                        desc.AddToClassList("tool-ask__option-desc");
                        desc.style.whiteSpace = WhiteSpace.Normal;
                        optionRow.Add(desc);
                    }
                    optionsRoot.Add(optionRow);
                }
            }

            // 자유 입력
            var freeInput = new TextField();
            freeInput.AddToClassList("tool-ask__free-input");
            freeInput.multiline = true;
            freeInput.label = "자유 입력 (선택)";
            card.Add(freeInput);

            // 전송 버튼
            var submitRow = new VisualElement();
            submitRow.AddToClassList("tool-ask__submit-row");
            var submit = new Button(() => SubmitToolAsk(card, msg, selectedToggles, freeInput))
            {
                text = "전송",
            };
            submit.AddToClassList("tool-ask__submit");
            submitRow.Add(submit);
            card.Add(submitRow);

            // 응답 요약 라벨 — collapsed 상태에서만 보임. 빌드 시 미리 만들어 답변 시 채운다.
            var answerSummary = new Label(string.Empty);
            answerSummary.AddToClassList("tool-ask__answer-summary");
            answerSummary.style.whiteSpace = WhiteSpace.Normal;
            card.Add(answerSummary);

            // 이미 응답된 카드라면 collapsed 상태로 복원 (세션 재오픈 등).
            if (msg.ToolAskAnswered)
                CollapseToolAskCard(card, msg.ToolAskAnswerSummary);
        }

        /// <summary>
        /// route_capability 의 capability_pick 카드. 호환 플러그인 타일 + "다음부터 자동" 토글.
        /// askUserQuestion 카드와 동일한 .tool-ask USS 패밀리를 재사용해 시각 일관성 유지.
        /// 응답 시 SendToolUserResponse(..., remember:true) 로 선호 저장 요청을 함께 보낸다.
        /// </summary>
        private void BuildCapabilityPickerCard(VisualElement row, ChatMessageVM msg)
        {
            var ask = msg.AskPayload;
            if (ask == null) return;

            var card = new VisualElement();
            card.AddToClassList("tool-ask");
            card.AddToClassList("tool-ask--capability");
            row.Add(card);

            // 헤더 chip — "도구 선택"
            var header = new Label(string.IsNullOrEmpty(ask.header) ? "도구 선택" : ask.header);
            header.AddToClassList("tool-ask__header");
            card.Add(header);

            // 1인칭 질문
            var question = new Label(ask.question ?? string.Empty);
            question.AddToClassList("tool-ask__question");
            question.style.whiteSpace = WhiteSpace.Normal;
            card.Add(question);

            // 플러그인 옵션 — Toggle 라디오 + 플러그인 메타 라벨
            var optionsRoot = new VisualElement();
            optionsRoot.AddToClassList("tool-ask__options");
            card.Add(optionsRoot);

            var pluginToggles = new List<Toggle>();
            var pluginById = new Dictionary<Toggle, string>();
            var fallbackOptions = ask.options ?? Array.Empty<OpenDesk.Claude.Models.AskOption>();
            var pluginCount = ask.compatible_plugins?.Length ?? 0;
            for (int i = 0; i < pluginCount; i++)
            {
                var plugin = ask.compatible_plugins[i];
                if (plugin == null || string.IsNullOrEmpty(plugin.display_name)) continue;

                var optionRow = new VisualElement();
                optionRow.AddToClassList("tool-ask__option-row");
                optionRow.AddToClassList("tool-ask__option-row--plugin");

                var toggle = new Toggle(plugin.display_name);
                toggle.userData = plugin.display_name;
                toggle.AddToClassList("tool-ask__radio");
                // 단일 선택 — 다른 토글 해제.
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue) return;
                    foreach (var other in pluginToggles)
                        if (other != toggle && other.value) other.SetValueWithoutNotify(false);
                });
                pluginToggles.Add(toggle);
                pluginById[toggle] = plugin.id ?? string.Empty;
                optionRow.Add(toggle);

                var vendor = string.IsNullOrEmpty(plugin.vendor) ? plugin.author : plugin.vendor;
                if (!string.IsNullOrEmpty(vendor))
                {
                    var meta = new Label($"플러그인 · {vendor}");
                    meta.AddToClassList("tool-ask__option-desc");
                    optionRow.Add(meta);
                }
                optionsRoot.Add(optionRow);
            }
            // 호환 플러그인이 비어 있는 비정상 케이스 — askUser 호환 options 로 fallback.
            if (pluginToggles.Count == 0 && fallbackOptions.Length > 0)
            {
                foreach (var opt in fallbackOptions)
                {
                    if (opt == null || string.IsNullOrEmpty(opt.label)) continue;
                    var optionRow = new VisualElement();
                    optionRow.AddToClassList("tool-ask__option-row");
                    var toggle = new Toggle(opt.label);
                    toggle.userData = opt.label;
                    toggle.AddToClassList("tool-ask__radio");
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (!evt.newValue) return;
                        foreach (var other in pluginToggles)
                            if (other != toggle && other.value) other.SetValueWithoutNotify(false);
                    });
                    pluginToggles.Add(toggle);
                    optionRow.Add(toggle);
                    optionsRoot.Add(optionRow);
                }
            }
            // 기본 선택 — 첫 옵션 (사용자가 곧장 "이어가기" 눌러도 의미 있는 동작).
            if (pluginToggles.Count > 0)
                pluginToggles[0].SetValueWithoutNotify(true);

            // "다음부터 자동" 체크박스. 기본 ON — 사용자가 의도적으로 끄지 않는 한 선호 저장이 자연스러움.
            var rememberToggle = new Toggle("이 동료는 다음부터 자동으로 선택");
            rememberToggle.value = true;
            rememberToggle.AddToClassList("tool-ask__remember");
            card.Add(rememberToggle);

            var rememberHint = new Label("바꾸려면 동료 설정 → 도구 매핑에서 변경 가능");
            rememberHint.AddToClassList("tool-ask__remember-hint");
            rememberHint.style.whiteSpace = WhiteSpace.Normal;
            card.Add(rememberHint);

            // CTA: "이어가기" + "나중에"
            var submitRow = new VisualElement();
            submitRow.AddToClassList("tool-ask__submit-row");
            var submit = new Button(() => SubmitCapabilityPick(card, msg, pluginToggles, pluginById, rememberToggle, skip: false))
            {
                text = "이어가기",
            };
            submit.AddToClassList("tool-ask__submit");
            submitRow.Add(submit);
            var skip = new Button(() => SubmitCapabilityPick(card, msg, pluginToggles, pluginById, rememberToggle, skip: true))
            {
                text = "나중에",
            };
            skip.AddToClassList("tool-ask__submit");
            skip.AddToClassList("tool-ask__submit--ghost");
            submitRow.Add(skip);
            card.Add(submitRow);

            // 응답 요약 라벨 — collapsed 상태에서만 보임.
            var answerSummary = new Label(string.Empty);
            answerSummary.AddToClassList("tool-ask__answer-summary");
            answerSummary.style.whiteSpace = WhiteSpace.Normal;
            card.Add(answerSummary);

            if (msg.ToolAskAnswered)
                CollapseToolAskCard(card, msg.ToolAskAnswerSummary);
        }

        private void SubmitCapabilityPick(
            VisualElement card,
            ChatMessageVM msg,
            List<Toggle> toggles,
            Dictionary<Toggle, string> pluginById,
            Toggle rememberToggle,
            bool skip)
        {
            if (msg == null || msg.ToolAskAnswered) return;
            if (_chat == null) return;

            string selectedLabel = null;
            string selectedPluginId = null;
            if (!skip)
            {
                foreach (var t in toggles)
                {
                    if (t != null && t.value && t.userData is string label)
                    {
                        selectedLabel = label;
                        pluginById.TryGetValue(t, out selectedPluginId);
                        break;
                    }
                }
            }

            var selected = string.IsNullOrEmpty(selectedLabel) ? Array.Empty<string>() : new[] { selectedLabel };
            var remember = !skip && rememberToggle != null && rememberToggle.value;
            // route_capability 가 free 입력으로 id 도 받아주므로, id 가 있다면 같이 실어 보낸다.
            var response = selectedPluginId ?? string.Empty;

            _chat.SendToolUserResponse(msg.ToolUseId, response, selected, remember);

            var summary = skip
                ? "(나중에)"
                : remember
                    ? $"{selectedLabel ?? "(선택 없음)"} · 다음부터 자동"
                    : (selectedLabel ?? "(선택 없음)");
            _vm?.MarkToolAskAnswered(msg.ToolUseId, summary);
            CollapseToolAskCard(card, summary);
        }

        private void SubmitToolAsk(VisualElement card, ChatMessageVM msg, List<Toggle> toggles, TextField freeInput)
        {
            if (msg == null || msg.ToolAskAnswered) return;
            if (_chat == null) return;

            var selected = new List<string>();
            foreach (var t in toggles)
            {
                if (t != null && t.value && t.userData is string label)
                    selected.Add(label);
            }
            var response = freeInput?.value?.Trim() ?? string.Empty;

            _chat.SendToolUserResponse(msg.ToolUseId, response, selected.ToArray());

            var summary = BuildToolAskSummary(selected, response);
            _vm?.MarkToolAskAnswered(msg.ToolUseId, summary);
            CollapseToolAskCard(card, summary);
        }

        private static string BuildToolAskSummary(List<string> selected, string response)
        {
            var hasSelected = selected != null && selected.Count > 0;
            var hasResponse = !string.IsNullOrEmpty(response);
            if (hasSelected && hasResponse)
                return $"{string.Join(", ", selected)} — {response}";
            if (hasSelected)
                return string.Join(", ", selected);
            if (hasResponse)
                return response;
            return "(응답 없음)";
        }

        private static void CollapseToolAskCard(VisualElement card, string answerSummary)
        {
            if (card == null) return;
            // SetEnabled(false) 는 자식 입력 위젯 차단 + 잠금 클래스로 시각 dim.
            card.SetEnabled(false);
            card.AddToClassList("tool-ask--locked");
            card.AddToClassList("tool-ask--collapsed");

            // collapsed view 에서 응답 요약 라벨 채우기.
            var summary = card.Q<Label>(className: "tool-ask__answer-summary");
            if (summary != null)
                summary.text = string.IsNullOrEmpty(answerSummary) ? "(응답 없음)" : $"✓ {answerSummary}";

            // 응답됨 배지 — 한 번만 추가.
            if (card.Q<Label>(className: "tool-ask__answered-badge") == null)
            {
                var badge = new Label("응답됨");
                badge.AddToClassList("tool-ask__answered-badge");
                card.Add(badge);
            }
        }

        private VisualElement BuildMessageActions(ChatMessageVM msg)
        {
            var actions = new VisualElement();
            actions.AddToClassList("message__actions");

            var copyBtn = new Button(() => OnCopyMessageClicked(msg))
            {
                text = string.Empty,
                tooltip = "복사",
            };
            copyBtn.AddToClassList("message__action-btn");
            copyBtn.Add(BuildCopyIcon());
            actions.Add(copyBtn);

            // 수정은 사용자 메시지에만 — assistant 응답 본문 수정은 의미 없음.
            if (msg.Sender == ChatSender.User)
            {
                var editBtn = new Button(() => OnEditMessageClicked(msg))
                {
                    text = string.Empty,
                    tooltip = "수정",
                };
                editBtn.AddToClassList("message__action-btn");
                editBtn.Add(BuildEditIcon());
                actions.Add(editBtn);
            }

            return actions;
        }

        // USS 도형 조합으로 그리는 아이콘 (NotoSansKR 미지원 글리프 사용 금지 제약 회피).
        private static VisualElement BuildCopyIcon()
        {
            var root = new VisualElement();
            root.AddToClassList("icon-copy");
            var back = new VisualElement();
            back.AddToClassList("icon-copy__back");
            var front = new VisualElement();
            front.AddToClassList("icon-copy__front");
            root.Add(back);
            root.Add(front);
            return root;
        }

        private static VisualElement BuildEditIcon()
        {
            var root = new VisualElement();
            root.AddToClassList("icon-edit");
            var bar = new VisualElement();
            bar.AddToClassList("icon-edit__bar");
            var tip = new VisualElement();
            tip.AddToClassList("icon-edit__tip");
            root.Add(bar);
            root.Add(tip);
            return root;
        }

        private void OnCopyMessageClicked(ChatMessageVM msg)
        {
            if (msg == null) return;
            var text = msg.Body ?? string.Empty;
            // assistant 응답에는 TMP 리치텍스트 태그가 섞여 있을 수 있어 plain text 로 정제.
            // OutboxController.StripTmpTags 와 동일 정책.
            var clean = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            GUIUtility.systemCopyBuffer = clean;
            UpdateStatus("복사됨");
            RestoreStatusAfterDelay().Forget();
        }

        private void OnEditMessageClicked(ChatMessageVM msg)
        {
            if (msg == null || _input == null) return;
            var text = msg.Body ?? string.Empty;
            _input.SetValueWithoutNotify(text);
            if (_vm != null) _vm.Draft = text;
            _input.Focus();
            // 커서를 맨 뒤로.
            var end = text.Length;
            _input.textSelection.SelectRange(end, end);
        }

        private void ScrollToBottom()
        {
            if (_scroll == null) return;
            // 메시지가 wrap 으로 다중 줄이 되면 첫 layout 패스 때 contentHeight 가 아직 한 줄 기준이라
            // 단일 schedule 로는 마지막 줄이 viewport 밖으로 가려질 수 있다 — 3 단계 재시도.
            DoScroll(20);
            DoScroll(120);
            DoScroll(300);
        }

        private void DoScroll(long delayMs)
        {
            _scroll.schedule.Execute(() =>
            {
                var content = _scroll.contentContainer;
                if (content == null) return;
                _scroll.scrollOffset = new Vector2(0, content.layout.height);
            }).StartingIn(delayMs);
        }

        // ══════════════════════════════════════════════════
        //  Utils
        // ══════════════════════════════════════════════════

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ══════════════════════════════════════════════════
        //  Sessions Drawer
        // ══════════════════════════════════════════════════

        private void ToggleSessionsDrawer()
        {
            if (_sessionsOpen) CloseSessionsDrawer();
            else OpenSessionsDrawer();
        }

        private void OpenSessionsDrawer()
        {
            if (_sessionsDrawer == null) return;
            RebuildSessionsList();
            _sessionsDrawer.AddToClassList("sessions-drawer--open");
            _sessionsOpen = true;
        }

        private void CloseSessionsDrawer()
        {
            if (_sessionsDrawer == null) return;
            _sessionsDrawer.RemoveFromClassList("sessions-drawer--open");
            _sessionsOpen = false;
        }

        private void RebuildSessionsList()
        {
            if (_sessionsScroll == null) return;
            _sessionsScroll.Clear();

            var sessions = AgentSessionStore.LoadByAgent(_currentAgentIndex);
            if (sessions == null || sessions.Count == 0)
            {
                if (_sessionsEmpty != null) _sessionsEmpty.style.display = DisplayStyle.Flex;
                return;
            }
            if (_sessionsEmpty != null) _sessionsEmpty.style.display = DisplayStyle.None;

            sessions.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));

            foreach (var session in sessions)
                _sessionsScroll.Add(BuildSessionItem(session));
        }

        private VisualElement BuildSessionItem(AgentSession session)
        {
            var item = new VisualElement();
            item.AddToClassList("session-item");
            if (session.SessionId == _currentSessionId)
                item.AddToClassList("session-item--active");

            var top = new VisualElement();
            top.AddToClassList("session-item__top");
            item.Add(top);

            var title = new Label(string.IsNullOrEmpty(session.Title) ? "새 대화" : session.Title);
            title.AddToClassList("session-item__title");
            top.Add(title);

            var time = new Label(FormatSessionTime(session.LastActivity));
            time.AddToClassList("session-item__time");
            top.Add(time);

            var preview = new Label(string.IsNullOrEmpty(session.LastMessage)
                ? "아직 대화가 없습니다"
                : Truncate(session.LastMessage, 50));
            preview.AddToClassList("session-item__preview");
            item.Add(preview);

            var captured = session.SessionId;
            item.RegisterCallback<ClickEvent>(_ => SwitchToSession(captured));
            return item;
        }

        private void SwitchToSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            _currentSessionId = sessionId;

            // 활성 세션 마킹 + 메시지 영역 재로드
            LoadHistory();
            ResumeSession();
            CloseSessionsDrawer();
        }

        // ══════════════════════════════════════════════════
        //  Session resolve — 마지막 세션 또는 새 세션 생성
        // ══════════════════════════════════════════════════

        private string ResolveOrCreateLatestSession()
        {
            var sessions = AgentSessionStore.LoadByAgent(_currentAgentIndex);
            if (sessions != null && sessions.Count > 0)
            {
                sessions.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));
                return sessions[0].SessionId;
            }

            // 신규 세션 자동 생성
            var newIdx = AgentSessionStore.CreateSession(_currentAgentIndex, _currentAgentName, _currentRole);
            var created = AgentSessionStore.Load(newIdx);
            return created?.SessionId ?? Guid.NewGuid().ToString("N");
        }

        // ══════════════════════════════════════════════════
        //  Utils
        // ══════════════════════════════════════════════════

        private static int StableIntFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            const uint Offset = 2166136261u;
            const uint Prime = 16777619u;
            uint h = Offset;
            for (int i = 0; i < id.Length; i++)
            {
                h ^= id[i];
                h *= Prime;
            }
            return (int)(h & 0x7FFFFFFF);
        }

        private static string Truncate(string text, int maxLen)
        {
            if (text == null) return string.Empty;
            if (text.Length <= maxLen) return text;
            return text[..(maxLen - 3)] + "...";
        }

        private static string FormatSessionTime(DateTime utcTime)
        {
            var local = utcTime.ToLocalTime();
            var now = DateTime.Now;
            if (local.Date == now.Date) return local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1)) return "어제";
            return local.ToString("MM/dd");
        }

        private static string RoleDisplayName(AgentRole role) => role switch
        {
            AgentRole.Planning    => "기획",
            AgentRole.Development => "개발",
            AgentRole.Design      => "디자인",
            AgentRole.Legal       => "법률",
            AgentRole.Marketing   => "마케팅",
            AgentRole.Research    => "리서치",
            AgentRole.Support     => "고객지원",
            AgentRole.Finance     => "재무",
            _                     => "에이전트",
        };
    }
}
