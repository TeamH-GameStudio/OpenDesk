using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.UI.Chat;
using R3;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Hud
{
    /// <summary>
    /// UI Toolkit + AppUI 로 구현된 캐릭터 HUD. 단일 UIDocument 가 모든 캐릭터의 이름/상태 카드를
    /// 화면 좌표로 트래킹 (Camera.WorldToScreenPoint → RuntimePanelUtils.ScreenToPanel).
    ///
    /// 디자인 결정:
    ///  - Screen Space 트래킹 — Unity 2022.3 UI Toolkit 은 정식 World Space 미지원 + isometric 뷰에서 픽셀 퍼펙트 텍스트 유리
    ///  - 단일 UIDocument — 카드별 PanelSettings/RenderTexture 인스턴스화 비용 회피
    ///  - 이름은 항상 표시, 상태(LinearProgress + 텍스트)는 호버/강제가시(에러·완료) 시에만 페이드 인
    ///
    /// 라이프사이클:
    ///  - <see cref="AgentSpawner.Spawned"/> → 카드 생성
    ///  - <see cref="AgentSpawner.Despawned"/> → 카드 제거
    ///  - <see cref="IAgentStateService.OnStateChanged"/> → 카드 상태 갱신
    ///  - <see cref="AgentPointerService.HoverChanged"/> → SetHover
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AgentHudView : MonoBehaviour, IDisposable
    {
        [Header("Tracking")]
        [SerializeField] private Camera _trackingCamera;
        [Tooltip("캐릭터 root(발 위치) 기준 위로 얼마나 올려 anchor 점을 잡을지 (월드 단위).")]
        [SerializeField] private float _worldHeightOffset = 2.2f;

        [Header("AppUI Panel")]
        [SerializeField] private string _appUITheme = "dark";
        [SerializeField] private string _appUIScale = "medium";

        [Header("Style Sheets")]
        [Tooltip("opendesk-tokens.uss — --brand/--n*/--space-* 토큰 정의. 미할당이어도 PanelSettings 의 기본 USS 가 토큰을 제공하면 동작.")]
        [SerializeField] private StyleSheet _designTokens;
        [Tooltip("AgentHud.uss — HUD 카드 전용 클래스. 반드시 할당.")]
        [SerializeField] private StyleSheet _hudStyleSheet;

        [Header("Force-visible")]
        [Tooltip("에러/완료/연결끊김 상태 진입 시 호버와 무관하게 강제 표시 유지 시간(초).")]
        [SerializeField] private float _forceVisibleDuration = 3f;

        // ── DI ──
        private AgentSpawner _spawner;
        private IAgentStateService _stateService;
        private AgentPointerService _pointerService;
        private ChatPanelView _chatPanel;

        // ── UI ──
        private UIDocument _document;
        private VisualElement _cardsLayer;
        private readonly Dictionary<string, HudCard> _cards = new();

        // ── 구독 ──
        private IDisposable _stateSub;
        private IDisposable _hoverSub;
        private bool _disposed;
        private bool _warnedAboutNullPanel;

        [Inject]
        public void Construct(
            AgentSpawner spawner,
            IAgentStateService stateService = null,
            AgentPointerService pointerService = null,
            ChatPanelView chatPanel = null)
        {
            _spawner = spawner;
            _stateService = stateService;
            _pointerService = pointerService;
            _chatPanel = chatPanel;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildView();
        }

        private void Start()
        {
            // Construct 가 보장된 시점부터 구독.
            if (_spawner != null)
            {
                _spawner.Spawned += HandleSpawned;
                _spawner.Despawned += HandleDespawned;

                // 이미 spawn 되어 있는 에이전트가 있으면 카드 백필.
                foreach (var kv in _spawner.SpawnedAgents)
                    HandleSpawned(kv.Value);
            }

            if (_stateService != null)
                _stateSub = _stateService.OnStateChanged.Subscribe(HandleStateChanged);

            if (_pointerService != null)
                _hoverSub = _pointerService.HoverChanged.Subscribe(HandleHoverChanged);

            // 채팅 패널 활성화 동안 HUD 페이드 아웃 — 채팅 진입 시 시각 노이즈 제거.
            if (_chatPanel != null)
            {
                _chatPanel.Opened += HandleChatOpened;
                _chatPanel.Closed += HandleChatClosed;
                if (_chatPanel.IsOpen) SetVisible(false);
            }

            if (_trackingCamera == null)
                _trackingCamera = Camera.main;
        }

        private void HandleChatOpened() => SetVisible(false);
        private void HandleChatClosed() => SetVisible(true);

        /// <summary>
        /// HUD 카드 레이어 전체 페이드. USS transition (250ms, --motion-base) 이 opacity 보간을 담당.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_cardsLayer == null) return;
            if (visible) _cardsLayer.RemoveFromClassList("hud-cards-layer--hidden");
            else _cardsLayer.AddToClassList("hud-cards-layer--hidden");
        }

        private void OnDisable()
        {
            if (_spawner != null)
            {
                _spawner.Spawned -= HandleSpawned;
                _spawner.Despawned -= HandleDespawned;
            }
            if (_chatPanel != null)
            {
                _chatPanel.Opened -= HandleChatOpened;
                _chatPanel.Closed -= HandleChatClosed;
            }
            _stateSub?.Dispose(); _stateSub = null;
            _hoverSub?.Dispose(); _hoverSub = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnDisable();
            foreach (var card in _cards.Values)
                card.Root?.RemoveFromHierarchy();
            _cards.Clear();
        }

        private void OnDestroy() => Dispose();

        // ════════════════════════════════════════════════════════════
        //  View Build
        // ════════════════════════════════════════════════════════════

        private void BuildView()
        {
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogError("[AgentHudView] UIDocument 미준비");
                return;
            }
            if (_cardsLayer != null) return; // 이미 빌드됨

            var rootEl = _document.rootVisualElement;

            // 3D 씬이 보이도록 rootEl 자체와 픽킹은 투명/패스스루.
            rootEl.style.backgroundColor = new UnityEngine.Color(0, 0, 0, 0);
            rootEl.pickingMode = PickingMode.Ignore;

            // 디자인 토큰 + HUD 전용 스타일시트 부착
            if (_designTokens != null && !rootEl.styleSheets.Contains(_designTokens))
                rootEl.styleSheets.Add(_designTokens);
            if (_hudStyleSheet != null && !rootEl.styleSheets.Contains(_hudStyleSheet))
                rootEl.styleSheets.Add(_hudStyleSheet);
            else if (_hudStyleSheet == null)
                Debug.LogWarning("[AgentHudView] _hudStyleSheet 미할당 — 카드가 스타일 없이 표시됩니다. Inspector 에 AgentHud.uss 연결 필요.");

            // AppUI 테마 변수만 받기 — `.appui` 베이스 클래스는 Context.uss 에서 background-color 를 칠해
            // 풀스크린 다크로 가려버리므로 부착 금지. `.appui--<theme>` / `.appui--scale-<scale>` 는 USS 변수만 정의하므로 안전.
            _cardsLayer = new VisualElement();
            _cardsLayer.AddToClassList("hud-cards-layer");
            if (!string.IsNullOrEmpty(_appUITheme)) _cardsLayer.AddToClassList($"appui--{_appUITheme}");
            if (!string.IsNullOrEmpty(_appUIScale)) _cardsLayer.AddToClassList($"appui--scale-{_appUIScale}");
            _cardsLayer.pickingMode = PickingMode.Ignore;
            rootEl.Add(_cardsLayer);
        }

        // ════════════════════════════════════════════════════════════
        //  Spawn / Despawn → Card lifecycle
        // ════════════════════════════════════════════════════════════

        private void HandleSpawned(AgentSpawner.SpawnedAgent spawned)
        {
            if (spawned == null || spawned.Profile == null) return;
            if (_cards.ContainsKey(spawned.SessionId)) return;

            var card = BuildCard(spawned.Profile, spawned.ModelInstance != null ? spawned.ModelInstance.transform : null);
            _cards[spawned.SessionId] = card;
            _cardsLayer.Add(card.Root);
            ApplyState(card, AgentActionType.Idle);
        }

        private void HandleDespawned(string sessionId)
        {
            if (!_cards.TryGetValue(sessionId, out var card)) return;
            card.Root?.RemoveFromHierarchy();
            _cards.Remove(sessionId);
        }

        private HudCard BuildCard(AgentProfileSO profile, Transform anchor)
        {
            var root = new VisualElement();
            root.AddToClassList("hud-card");
            root.pickingMode = PickingMode.Ignore;

            // ── 이름 행 (Color dot + Name) — 항상 가시 ──
            var nameRow = new VisualElement();
            nameRow.AddToClassList("hud-card__name-row");
            nameRow.pickingMode = PickingMode.Ignore;

            // 캐릭터 식별용 컬러 닷 — AppUI Avatar 는 이 버전에 initials 미지원이라 간단한 VisualElement 로 대체.
            var dot = new VisualElement();
            dot.AddToClassList("hud-card__color-dot");
            dot.style.backgroundColor = profile.HudColor;
            dot.pickingMode = PickingMode.Ignore;
            nameRow.Add(dot);

            var nameLabel = new Label(profile.AgentName);
            nameLabel.AddToClassList("hud-card__name");
            nameLabel.style.color = profile.HudColor;
            nameLabel.pickingMode = PickingMode.Ignore;
            nameRow.Add(nameLabel);

            root.Add(nameRow);

            // ── 상태 섹션 — 호버 시에만 페이드 인 ──
            var statusSection = new VisualElement();
            statusSection.AddToClassList("hud-card__status-section");
            statusSection.pickingMode = PickingMode.Ignore;

            var statusText = new Label();
            statusText.AddToClassList("hud-card__status-text");
            statusText.pickingMode = PickingMode.Ignore;
            statusSection.Add(statusText);

            // AppUI LinearProgress — value/bufferValue 만 사용. variant 기본값(Indeterminate)이 아니라
            // Determinate 로 명시 (Progress.Variant 는 부모 클래스의 nested enum).
            var statusBar = new LinearProgress
            {
                value = 0f,
                bufferValue = 0f,
            };
            statusBar.variant = Progress.Variant.Determinate;
            statusBar.AddToClassList("hud-card__status-bar");
            statusBar.pickingMode = PickingMode.Ignore;
            statusSection.Add(statusBar);

            root.Add(statusSection);

            return new HudCard
            {
                SessionId = profile.SessionId,
                Anchor = anchor,
                Root = root,
                NameLabel = nameLabel,
                StatusText = statusText,
                StatusBar = statusBar
            };
        }


        // ════════════════════════════════════════════════════════════
        //  State / Hover
        // ════════════════════════════════════════════════════════════

        private void HandleStateChanged((string SessionId, AgentActionType State) evt)
        {
            if (!_cards.TryGetValue(evt.SessionId, out var card)) return;
            ApplyState(card, evt.State);
        }

        private void HandleHoverChanged(AgentSpawner.SpawnedAgent next)
        {
            // 이전 호버 카드 해제
            foreach (var card in _cards.Values)
                card.IsHovered = false;

            if (next != null && _cards.TryGetValue(next.SessionId, out var hovered))
                hovered.IsHovered = true;

            RefreshRevealClasses();
        }

        public void SetHover(string sessionId, bool hovered)
        {
            if (!_cards.TryGetValue(sessionId, out var card)) return;
            card.IsHovered = hovered;
            RefreshRevealClasses();
        }

        private void RefreshRevealClasses()
        {
            foreach (var card in _cards.Values)
            {
                var reveal = card.IsHovered || Time.time < card.ForceVisibleUntil;
                ToggleClass(card.Root, "hud-card--reveal", reveal);
            }
        }

        private void ApplyState(HudCard card, AgentActionType action)
        {
            var info = GetDisplayInfo(action);
            card.StatusText.text = info.Text;
            card.IsPulsing = info.Pulse;

            if (!info.Pulse)
                card.StatusBar.value = info.FillValue;

            // 상태 색상 — USS 클래스 전환
            SetStateClass(card.Root, info.StateClass);

            // 에러/완료/연결끊김은 호버 없이도 3초 강제 가시
            if (action == AgentActionType.TaskFailed
             || action == AgentActionType.TaskCompleted
             || action == AgentActionType.Disconnected)
            {
                card.ForceVisibleUntil = Time.time + _forceVisibleDuration;
                RefreshRevealClasses();
            }
        }

        private static readonly string[] StateClasses =
        {
            "hud-card--state-idle",     "hud-card--state-thinking",
            "hud-card--state-planning", "hud-card--state-executing",
            "hud-card--state-reviewing","hud-card--state-tool",
            "hud-card--state-chat",     "hud-card--state-complete",
            "hud-card--state-error"
        };

        private static void SetStateClass(VisualElement el, string newClass)
        {
            foreach (var c in StateClasses)
                el.RemoveFromClassList(c);
            if (!string.IsNullOrEmpty(newClass))
                el.AddToClassList(newClass);
        }

        private static void ToggleClass(VisualElement el, string cls, bool on)
        {
            if (on) el.AddToClassList(cls);
            else el.RemoveFromClassList(cls);
        }

        private static (string Text, float FillValue, bool Pulse, string StateClass) GetDisplayInfo(AgentActionType action)
        {
            return action switch
            {
                AgentActionType.Idle            => ("대기 중",           0f,   false, "hud-card--state-idle"),
                AgentActionType.Thinking        => ("생각 중...",        0f,   true,  "hud-card--state-thinking"),
                AgentActionType.Planning        => ("계획 수립 중...",   0f,   true,  "hud-card--state-planning"),
                AgentActionType.Executing       => ("실행 중...",        0f,   true,  "hud-card--state-executing"),
                AgentActionType.Reviewing       => ("검토 중...",        0f,   true,  "hud-card--state-reviewing"),
                AgentActionType.ToolUsing       => ("도구 호출 중...",   0.5f, false, "hud-card--state-tool"),
                AgentActionType.ToolResult      => ("도구 결과 수신",    0.75f,false, "hud-card--state-tool"),
                AgentActionType.ChatDelta       => ("응답 중...",        0f,   true,  "hud-card--state-chat"),
                AgentActionType.ChatFinal       => ("응답 완료",         1f,   false, "hud-card--state-complete"),
                AgentActionType.TaskStarted     => ("작업 시작",         0.1f, true,  "hud-card--state-executing"),
                AgentActionType.TaskCompleted   => ("작업 완료!",        1f,   false, "hud-card--state-complete"),
                AgentActionType.TaskFailed      => ("오류 발생",         1f,   false, "hud-card--state-error"),
                AgentActionType.Connected       => ("연결됨",            0f,   false, "hud-card--state-complete"),
                AgentActionType.Disconnected    => ("연결 끊김",         0f,   false, "hud-card--state-error"),
                AgentActionType.SubAgentSpawned => ("서브에이전트 생성", 0.3f, true,  "hud-card--state-executing"),
                _                               => ("...",               0f,   false, "hud-card--state-idle"),
            };
        }

        // ════════════════════════════════════════════════════════════
        //  Tracking — WorldToScreenPoint → panel space
        // ════════════════════════════════════════════════════════════

        private void LateUpdate()
        {
            if (_cardsLayer == null || _trackingCamera == null) return;

            var nowForceUpdate = false;
            foreach (var card in _cards.Values)
            {
                UpdateCardPosition(card);
                UpdateCardPulse(card);

                // ForceVisible 타이머가 만료된 카드는 reveal 클래스 제거 (호버가 아니면)
                if (card.ForceVisibleUntil > 0f && Time.time >= card.ForceVisibleUntil)
                {
                    card.ForceVisibleUntil = 0f;
                    nowForceUpdate = true;
                }
            }
            if (nowForceUpdate) RefreshRevealClasses();
        }

        private void UpdateCardPosition(HudCard card)
        {
            if (card.Anchor == null)
            {
                ToggleClass(card.Root, "hud-card--offscreen", true);
                return;
            }

            // UIDocument 가 첫 프레임 전이거나 PanelSettings 가 미할당이면 panel 이 null —
            // 다음 프레임에 다시 시도하도록 SKIP. 5초간 계속 null 이면 진단 로그 1회.
            var panel = _cardsLayer?.panel;
            var visualTree = panel?.visualTree;
            // visualTree 의 layout 이 아직 안 풀린 경우(worldBound.width 가 NaN/0) ScreenToPanel 이 내부 매트릭스 접근에서 NRE.
            var layoutReady = visualTree != null && !float.IsNaN(visualTree.worldBound.width) && visualTree.worldBound.width > 0f;
            if (!layoutReady)
            {
                if (!_warnedAboutNullPanel && Time.time > 5f)
                {
                    _warnedAboutNullPanel = true;
                    Debug.LogWarning("[AgentHudView] UIDocument panel/layout 이 5초 넘게 준비되지 않음 — UIDocument 의 PanelSettings 슬롯이 채워져 있는지, GameObject 가 활성 상태인지 확인하세요.", this);
                }
                ToggleClass(card.Root, "hud-card--offscreen", true);
                return;
            }

            var worldPos = card.Anchor.position + Vector3.up * _worldHeightOffset;
            var screen = _trackingCamera.WorldToScreenPoint(worldPos);

            // 카메라 뒤쪽이면 숨김
            if (screen.z < 0f)
            {
                ToggleClass(card.Root, "hud-card--offscreen", true);
                return;
            }
            ToggleClass(card.Root, "hud-card--offscreen", false);

            // Unity Screen 좌표(좌하단 원점) → UI Toolkit panel 좌표(좌상단 원점)
            // ScreenToPanel 이 panel.visualTree 의 행렬에 접근하다 internal state 불일치로 드물게 NRE 를 던질 수 있어 try/catch.
            try
            {
                var panelPos = RuntimePanelUtils.ScreenToPanel(
                    panel,
                    new Vector2(screen.x, Screen.height - screen.y));
                card.Root.style.left = panelPos.x;
                card.Root.style.top = panelPos.y;
            }
            catch (NullReferenceException)
            {
                // 한 프레임 SKIP — 다음 LateUpdate 에서 다시 시도.
                ToggleClass(card.Root, "hud-card--offscreen", true);
            }
        }

        private void UpdateCardPulse(HudCard card)
        {
            if (!card.IsPulsing) return;
            // 0.3 ~ 0.7 사이를 PingPong — 기존 AgentHUDController 와 동일한 톤
            card.StatusBar.value = 0.3f + 0.4f * Mathf.PingPong(Time.time * 1.5f, 1f);
        }

        // ════════════════════════════════════════════════════════════
        //  Internal card state
        // ════════════════════════════════════════════════════════════

        private sealed class HudCard
        {
            public string SessionId;
            public Transform Anchor;
            public VisualElement Root;
            public Label NameLabel;
            public Label StatusText;
            public LinearProgress StatusBar;
            public bool IsHovered;
            public bool IsPulsing;
            public float ForceVisibleUntil;
        }
    }
}
