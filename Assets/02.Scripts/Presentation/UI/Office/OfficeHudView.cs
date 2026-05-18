using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Services.Credits;
using OpenDesk.Presentation.Cameras;
using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.UI.Chat;
using OpenDesk.Presentation.UI.Credits;
using OpenDesk.Presentation.UI.SkillMarket;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Office
{
    /// <summary>
    /// 오피스 씬 메인 HUD — UI Toolkit.
    ///   - 우상단 메뉴: 동료 추가(AgentCreationOpener → AgentCreationScene Single 전환) / 스킬 마켓(SkillMarketView)
    ///   - 좌측 사이드: 저장된 에이전트 카드 리스트 (AgentDraftJsonStore.LoadAll)
    ///   - 새 에이전트 저장 이벤트 (AgentRosterBootstrapper.OnAgentSpawned) 구독 → 리스트 갱신
    ///
    /// 인스펙터 작업: 같은 GameObject 의 UIDocument.Source Asset 에
    /// OfficeHudView.uxml 을 연결하면 끝.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OfficeHudView : MonoBehaviour
    {
        private UIDocument _document;
        private VisualElement _root;
        private Button _addAgentButton;
        private Button _openMarketButton;
        private ScrollView _agentList;

        // ── DI ──
        private AgentDraftJsonStore _store;
        private AgentCreationOpener _opener;
        private AgentRosterBootstrapper _roster;
        private SkillMarketView _market;
        private ChatPanelView _chatPanel;
        private ICameraFocusService _cameraFocus;
        private ICreditBalanceService _credits;
        private CreditBalanceBinder _creditBinder;

        // 현재 선택된 에이전트 (스킬 마켓 진입 시 컨텍스트로 전달)
        private string _selectedAgentId;
        private AgentRole _selectedRole = AgentRole.None;

        [Inject]
        public void Construct(
            AgentDraftJsonStore store,
            AgentCreationOpener opener,
            AgentRosterBootstrapper roster,
            ICameraFocusService cameraFocus = null,
            ICreditBalanceService credits = null)
        {
            _store = store;
            _opener = opener;
            _roster = roster;
            _cameraFocus = cameraFocus;
            _credits = credits;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildView();
            if (_roster != null)
            {
                _roster.OnAgentSpawned += OnAgentSpawned;
                _roster.OnEmptyRoster += OnEmptyRoster;
            }
            BindChatPanel();
            RefreshAgentList();
        }

        private void OnDisable()
        {
            if (_roster != null)
            {
                _roster.OnAgentSpawned -= OnAgentSpawned;
                _roster.OnEmptyRoster -= OnEmptyRoster;
            }
            UnbindChatPanel();
            if (_addAgentButton != null) _addAgentButton.clicked -= OnAddAgentClicked;
            if (_openMarketButton != null) _openMarketButton.clicked -= OnOpenMarketClicked;
            _creditBinder?.Dispose();
            _creditBinder = null;
        }

        // ────────────────────────────────────────────────
        //  ChatPanelView 가시성 동기화
        //  채팅 패널이 열리면 메인 HUD(상단바 + 좌측 동료 리스트)를 숨겨
        //  3D 캐릭터/채팅 영역과 시각적 중첩을 제거한다.
        // ────────────────────────────────────────────────

        private void BindChatPanel()
        {
            if (_chatPanel == null)
                _chatPanel = FindFirstObjectByType<ChatPanelView>(FindObjectsInactive.Include);
            if (_chatPanel == null) return;

            _chatPanel.Opened += OnChatOpened;
            _chatPanel.Closed += OnChatClosed;

            // 씬 진입 시점에 이미 열려있던 케이스(드물지만) 대비.
            if (_chatPanel.IsOpen) SetHudVisible(false);
        }

        private void UnbindChatPanel()
        {
            if (_chatPanel == null) return;
            _chatPanel.Opened -= OnChatOpened;
            _chatPanel.Closed -= OnChatClosed;
        }

        private void OnChatOpened() => SetHudVisible(false);
        private void OnChatClosed() => SetHudVisible(true);

        private void SetHudVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ────────────────────────────────────────────────
        //  View build
        // ────────────────────────────────────────────────

        private void BuildView()
        {
            if (_document == null) return;
            var rootEl = _document.rootVisualElement;
            if (rootEl == null)
            {
                Debug.LogError("[OfficeHudView] rootVisualElement null — UIDocument 의 Source Asset 에 OfficeHudView.uxml 을 연결하세요.");
                return;
            }

            _root              = rootEl.Q<VisualElement>("office-hud");
            _addAgentButton    = rootEl.Q<Button>("office-hud-add-agent");
            _openMarketButton  = rootEl.Q<Button>("office-hud-open-market");
            _agentList         = rootEl.Q<ScrollView>("office-hud-agent-list");

            if (_addAgentButton != null) _addAgentButton.clicked += OnAddAgentClicked;
            if (_openMarketButton != null) _openMarketButton.clicked += OnOpenMarketClicked;

            // 잔액 배지 — opendesk_routed 모드에서만 의미. anthropic_api (BYOK) 면 0 표시.
            _creditBinder = CreditBalanceBinder.BindByPrefix(rootEl, "office-credit-badge", _credits);
        }

        // ────────────────────────────────────────────────
        //  Roster 이벤트 → 리스트 갱신
        // ────────────────────────────────────────────────

        private void OnAgentSpawned(string _) => RefreshAgentList();
        private void OnEmptyRoster() => RefreshAgentList();

        private void RefreshAgentList()
        {
            if (_agentList == null) return;
            _agentList.Clear();

            if (_store == null) return;
            var records = _store.LoadAll();
            if (records == null || records.Count == 0) return;

            foreach (var record in records)
            {
                if (record == null) continue;
                _agentList.Add(BuildAgentCard(record));
            }

            // 기본 선택 — 첫 번째 에이전트
            if (string.IsNullOrEmpty(_selectedAgentId) && records.Count > 0)
                SetSelectedAgent(records[0]);
        }

        private VisualElement BuildAgentCard(AgentDraftRecord record)
        {
            var card = new VisualElement();
            card.AddToClassList("office-hud__agent-card");
            card.pickingMode = PickingMode.Position;

            var avatar = new Label(InitialOf(record.name));
            avatar.AddToClassList("office-hud__agent-avatar");
            card.Add(avatar);

            var meta = new VisualElement();
            meta.AddToClassList("office-hud__agent-meta");
            card.Add(meta);

            var name = new Label(string.IsNullOrEmpty(record.name) ? "(이름 없음)" : record.name);
            name.AddToClassList("office-hud__agent-name");
            meta.Add(name);

            var role = new Label(string.IsNullOrEmpty(record.role) ? "" : record.role);
            role.AddToClassList("office-hud__agent-role");
            meta.Add(role);

            // 마켓 진입 단축 버튼
            var marketBtn = new Button(() =>
            {
                SetSelectedAgent(record);
                OpenSkillMarket();
            }) { text = "+" };
            marketBtn.AddToClassList("office-hud__agent-action");
            marketBtn.tooltip = "이 에이전트의 스킬 마켓 열기";
            card.Add(marketBtn);

            // 카드 클릭 → 선택 + 채팅 패널 오픈
            card.RegisterCallback<ClickEvent>(_ =>
            {
                SetSelectedAgent(record);
                OpenChatPanel(record);
            });
            return card;
        }

        private void OpenChatPanel(AgentDraftRecord record)
        {
            if (record == null) return;
            // 카드 클릭 시점까지 ChatPanelView 가 아직 활성화되지 않았다면 lazy-bind.
            if (_chatPanel == null) BindChatPanel();
            if (_chatPanel == null)
            {
                Debug.LogWarning("[OfficeHudView] ChatPanelView 미발견 — 씬에 UIDocument + ChatPanelView 를 배치하세요.");
                return;
            }
            // ChatPanelView 가 마지막 세션 자동 로드 또는 새 세션 생성.
            // record.role 은 위저드에서 사용자가 입력한 자유 텍스트 — enum 매핑 결과와 별도로 원본 전달.
            var agentId = record.id ?? record.name ?? "default";

            // 3D 캐릭터 직접 클릭 경로(AgentClickHandler)와 동일하게 카메라 포커스도 함께 호출.
            // agentId 가 AgentSpawner 의 SessionId 와 일치하지 않는 경우 FocusOn 이 내부에서 warning 만 찍고 no-op.
            _cameraFocus?.FocusOn(agentId);

            _chatPanel.OpenForAgent(agentId, record.name ?? "에이전트", ParseRole(record.role), record.role);
        }

        private void SetSelectedAgent(AgentDraftRecord record)
        {
            if (record == null) return;
            _selectedAgentId = record.id ?? record.name ?? string.Empty;
            _selectedRole = ParseRole(record.role);
        }

        // ────────────────────────────────────────────────
        //  메뉴 클릭
        // ────────────────────────────────────────────────

        private void OnAddAgentClicked()
        {
            if (_opener == null)
            {
                Debug.LogWarning("[OfficeHudView] AgentCreationOpener 미주입.");
                return;
            }
            _opener.Open();
        }

        private void OnOpenMarketClicked() => OpenSkillMarket();

        private void OpenSkillMarket()
        {
            if (_market == null)
                _market = FindFirstObjectByType<SkillMarketView>(FindObjectsInactive.Include);
            if (_market == null)
            {
                Debug.LogWarning("[OfficeHudView] SkillMarketView 미발견 — 씬에 UIDocument + SkillMarketView 를 배치하세요.");
                return;
            }
            _market.Open(_selectedAgentId ?? string.Empty, _selectedRole);
        }

        // ────────────────────────────────────────────────
        //  Utils
        // ────────────────────────────────────────────────

        private static string InitialOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return name.Substring(0, 1);
        }

        private static AgentRole ParseRole(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return AgentRole.None;
            return raw switch
            {
                "기획"     or "Planning"    => AgentRole.Planning,
                "개발"     or "Development" => AgentRole.Development,
                "디자인"   or "Design"      => AgentRole.Design,
                "법률"     or "Legal"       => AgentRole.Legal,
                "마케팅"   or "Marketing"   => AgentRole.Marketing,
                "리서치"   or "Research"    => AgentRole.Research,
                "고객지원" or "Support"     => AgentRole.Support,
                "재무"     or "Finance"     => AgentRole.Finance,
                _                            => AgentRole.None,
            };
        }
    }
}
