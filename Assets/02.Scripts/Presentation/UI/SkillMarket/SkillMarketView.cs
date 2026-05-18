using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services.Skills;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.SkillMarket
{
    /// <summary>
    /// 스킬 마켓 — UI Toolkit (UXML/USS + UIDocument) 기반 메인 패널.
    /// "OpenDesk Skill Market v2 (B+C 하이브리드)" 디자인 구현:
    ///   - 상단 가로 탭바 (추천 + 카테고리, 활성 시 웜클레이 언더라인)
    ///   - 추천 탭: 히어로 + 추천 그리드 + 신규 그리드
    ///   - 카테고리 탭: 좌측 리스트 + 우측 상세 (두 패널)
    ///
    /// Inspector 작업:
    ///   같은 GameObject 의 UIDocument.Source Asset → SkillMarketView.uxml
    ///   (UXML 내부의 Style src 가 USS 자동 참조)
    ///
    /// 진입: SkillsPanelController / EquipmentSlotUI 의 추가 버튼이
    /// SkillMarketView.Open(agentId, role) 을 호출.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillMarketView : MonoBehaviour
    {
        private const string FeaturedTabId = "__featured__";

        private static readonly SkillCategory[] CategoryOrder =
        {
            SkillCategory.Scheduling, SkillCategory.Email, SkillCategory.Search,
            SkillCategory.Research, SkillCategory.Coding, SkillCategory.Design,
            SkillCategory.Analytics, SkillCategory.Social, SkillCategory.Support,
            SkillCategory.Translation,
        };

        // ── DI ─────────────────────────────────────────────
        private ISkillCatalogService _catalog;
        private ISkillInstallerService _installer;
        private IAgentSkillLoadoutService _loadout;
        private ISkillRecommendationService _recommendations;

        // v4: 플러그인 통합 — 이전 PluginsMarketView 가 담당하던 모든 플러그인 노출/설치 흐름을 흡수.
        // 인터페이스(IPluginCatalogService/IAgentPluginLoadoutService/IPluginCredentialService) 는
        // CoreInstaller 에서 항상 등록되므로 [Inject] 로 안전하게 받는다.
        // PluginCredentialModal 은 씬 component 라 미배치 케이스가 있어 lazy lookup 으로 분리
        //  — VContainer 가 C# default(null) param 을 무시하고 강제 resolve 하면서 미등록 fail 이 나기 때문.
        private OpenDesk.Core.Services.Plugins.IPluginCatalogService _pluginCatalog;
        private OpenDesk.Core.Services.Plugins.IAgentPluginLoadoutService _pluginLoadout;
        private OpenDesk.Core.Services.Plugins.IPluginCredentialService _pluginCredentials;
        private OpenDesk.Presentation.UI.Plugins.PluginCredentialModal _pluginCredModal; // lazy
        private OpenDesk.Core.Services.IAiChatService _chat;

        [Inject]
        public void Construct(
            ISkillCatalogService catalog,
            ISkillInstallerService installer,
            IAgentSkillLoadoutService loadout,
            ISkillRecommendationService recommendations,
            OpenDesk.Core.Services.Plugins.IPluginCatalogService pluginCatalog,
            OpenDesk.Core.Services.Plugins.IAgentPluginLoadoutService pluginLoadout,
            OpenDesk.Core.Services.Plugins.IPluginCredentialService pluginCredentials,
            OpenDesk.Core.Services.IAiChatService chat)
        {
            _catalog = catalog;
            _installer = installer;
            _loadout = loadout;
            _recommendations = recommendations;
            _pluginCatalog = pluginCatalog;
            _pluginLoadout = pluginLoadout;
            _pluginCredentials = pluginCredentials;
            _chat = chat;
        }

        // PluginCredentialModal lazy lookup — 씬에 UIDocument 가 배치된 경우만 발견됨.
        // SkillMarketView 자체가 동일 패턴(SkillLoadoutView 내 _marketView lazy lookup)을 SkillMarket 측에서도 쓴다.
        private OpenDesk.Presentation.UI.Plugins.PluginCredentialModal ResolvePluginCredModal()
        {
            if (_pluginCredModal == null)
                _pluginCredModal = FindFirstObjectByType<OpenDesk.Presentation.UI.Plugins.PluginCredentialModal>(FindObjectsInactive.Include);
            return _pluginCredModal;
        }

        // ── UIDocument refs ────────────────────────────────
        private UIDocument _document;
        private VisualElement _root;

        // Header
        private Button _refreshButton;
        private Button _closeButton;

        // Toast (우측 상단 알림)
        private VisualElement _toast;
        private Label _toastText;
        private CancellationTokenSource _toastCts;

        // Permission modal
        private VisualElement _permModal;
        private VisualElement _permVeil;
        private Label _permMono;
        private Label _permTitle;
        private Label _permAuthor;
        private VisualElement _permList;
        private Button _permCancelBtn;
        private Button _permConfirmBtn;
        private SkillDescriptor _pendingInstall;

        // Tab bar
        private ScrollView _tabBar;

        // Body — featured panel
        private VisualElement _featuredPanel;
        private TextField _searchFieldFeatured;
        private ScrollView _featuredScroll;
        private VisualElement _hero;
        private Label _heroKicker;
        private Label _heroMonoChar;
        private Label _heroTitle;
        private Label _heroMeta;
        private Label _heroDesc;
        private VisualElement _recommendedSection;
        private Label _recommendedHeader;
        private Label _recommendedHint;
        private VisualElement _recommendedGrid;
        private Label _newlyHeader;
        private Label _newlyHint;
        private VisualElement _newlyGrid;

        // Body — category two-pane
        private VisualElement _categoryPanel;
        private TextField _searchFieldList;
        private ScrollView _listScroll;
        private Label _listEmpty;
        private ScrollView _detailScroll;
        private VisualElement _detailBody;
        private Label _detailEmpty;

        // ── State ──────────────────────────────────────────
        private string _currentAgentId;
        private AgentRole _currentRole = AgentRole.None;
        private string _selectedTabId = FeaturedTabId;
        private string _searchQuery = string.Empty;
        private string _selectedSkillId;
        private SkillDetailElement _detailElement;

        private readonly Dictionary<string, SkillCardElement> _cards = new();
        private readonly Dictionary<string, SkillRowElement> _rows = new();
        private readonly List<TopTabElement> _tabs = new();
        private TopTabElement _featuredTabButton;

        // v4: 마켓 종류 필터 (전체 · 스킬 · 플러그인). featured 패널 상단 칩으로 노출.
        private enum MarketKindFilter { All, Skill, Plugin }
        private MarketKindFilter _kindFilter = MarketKindFilter.All;
        private VisualElement _kindFilterBar;            // 동적 생성, featured 패널 최상단에 inject
        private readonly Dictionary<MarketKindFilter, Button> _kindChips = new();

        // 플러그인 카드 캐시 (loadout/install 상태 변경 시 부분 갱신).
        private readonly Dictionary<string, PluginCardElement> _pluginCards = new();

        private IDisposable _catalogSubscription;
        private IDisposable _installSubscription;
        private IDisposable _loadoutSubscription;
        private IDisposable _pluginCatalogSubscription;
        private IDisposable _pluginLoadoutSubscription;
        private CancellationTokenSource _refreshCts;

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
            SetVisible(false);
        }

        private void OnDisable()
        {
            UnwireRoot();
            DisposeSubscriptions();
        }

        private void OnDestroy()
        {
            DisposeSubscriptions();
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _toastCts?.Cancel();
            _toastCts?.Dispose();
        }

        // ══════════════════════════════════════════════════
        //  Public API — 외부 진입점
        // ══════════════════════════════════════════════════

        public void Open(string agentId, AgentRole role)
        {
            _currentAgentId = agentId ?? string.Empty;
            _currentRole = role;

            if (_root == null) BuildView();
            SetVisible(true);

            BuildTabs();
            EnsureSubscriptions();
            RefreshCatalogAsync(forceRefresh: false).Forget();
            // v4: 오픈 시 현재 카탈로그를 미들웨어에 즉시 push — route_capability 가 곧장 매칭 가능하도록.
            PushPluginRegistryToMiddleware();
        }

        public void Close()
        {
            SetVisible(false);
        }

        // ══════════════════════════════════════════════════
        //  View build
        // ══════════════════════════════════════════════════

        private void BuildView()
        {
            if (_document == null)
            {
                Debug.LogError("[SkillMarketView] UIDocument 컴포넌트 누락");
                return;
            }

            var rootEl = _document.rootVisualElement;
            if (rootEl == null)
            {
                Debug.LogError("[SkillMarketView] rootVisualElement 가 null — UIDocument 의 Source Asset 에 SkillMarketView.uxml 을 연결하세요");
                return;
            }

            _root = rootEl.Q<VisualElement>("skill-market");
            if (_root == null)
            {
                Debug.LogError("[SkillMarketView] UXML 트리에서 'skill-market' 미발견");
                return;
            }

            _refreshButton = rootEl.Q<Button>("skill-market-refresh");
            _closeButton   = rootEl.Q<Button>("skill-market-close");

            _toast     = rootEl.Q<VisualElement>("skill-market-toast");
            _toastText = rootEl.Q<Label>("skill-market-toast-text");

            _permModal      = rootEl.Q<VisualElement>("skill-market-perm-modal");
            _permVeil       = rootEl.Q<VisualElement>("skill-market-perm-veil");
            _permMono       = rootEl.Q<Label>("skill-market-perm-mono");
            _permTitle      = rootEl.Q<Label>("skill-market-perm-title");
            _permAuthor     = rootEl.Q<Label>("skill-market-perm-author");
            _permList       = rootEl.Q<VisualElement>("skill-market-perm-list");
            _permCancelBtn  = rootEl.Q<Button>("skill-market-perm-cancel");
            _permConfirmBtn = rootEl.Q<Button>("skill-market-perm-confirm");

            _tabBar = rootEl.Q<ScrollView>("skill-market-tab-bar");

            _featuredPanel       = rootEl.Q<VisualElement>("skill-market-featured-panel");
            _searchFieldFeatured = rootEl.Q<TextField>("skill-market-search-featured");
            _featuredScroll      = rootEl.Q<ScrollView>("skill-market-featured-scroll");
            _hero                = rootEl.Q<VisualElement>("skill-market-hero");
            _heroKicker          = rootEl.Q<Label>("skill-market-hero-kicker") ?? new Label();
            _heroMonoChar        = rootEl.Q<Label>("skill-market-hero-mono-char");
            _heroTitle           = rootEl.Q<Label>("skill-market-hero-title");
            _heroMeta            = rootEl.Q<Label>("skill-market-hero-meta");
            _heroDesc            = rootEl.Q<Label>("skill-market-hero-desc");
            _recommendedSection  = rootEl.Q<VisualElement>("skill-market-recommended-section");
            _recommendedHeader   = rootEl.Q<Label>("skill-market-recommended-header");
            _recommendedHint     = rootEl.Q<Label>("skill-market-recommended-hint");
            _recommendedGrid     = rootEl.Q<VisualElement>("skill-market-recommended");
            _newlyHeader         = rootEl.Q<Label>("skill-market-newly-header");
            _newlyHint           = rootEl.Q<Label>("skill-market-newly-hint");
            _newlyGrid           = rootEl.Q<VisualElement>("skill-market-newly");

            _categoryPanel   = rootEl.Q<VisualElement>("skill-market-category-panel");
            _searchFieldList = rootEl.Q<TextField>("skill-market-search-list");
            _listScroll      = rootEl.Q<ScrollView>("skill-market-list");
            _listEmpty       = rootEl.Q<Label>("skill-market-list-empty");
            _detailScroll    = rootEl.Q<ScrollView>("skill-market-detail-scroll");
            _detailBody      = rootEl.Q<VisualElement>("skill-market-detail");
            _detailEmpty     = rootEl.Q<Label>("skill-market-detail-empty");

            // Detail element lives inside _detailBody
            _detailElement = new SkillDetailElement
            {
                InstallClicked   = OnInstallClicked,
                UninstallClicked = OnUninstallClicked,
                EquipClicked     = OnEquipClicked,
                UnequipClicked   = OnUnequipClicked,
            };
            _detailBody?.Add(_detailElement);

            WireRoot();
        }

        private void WireRoot()
        {
            if (_closeButton != null) _closeButton.clicked += OnCloseClicked;
            if (_refreshButton != null) _refreshButton.clicked += OnRefreshClicked;
            if (_searchFieldFeatured != null)
                _searchFieldFeatured.RegisterValueChangedCallback(OnSearchChanged);
            if (_searchFieldList != null)
                _searchFieldList.RegisterValueChangedCallback(OnSearchChanged);

            if (_permCancelBtn != null) _permCancelBtn.clicked += OnPermCancel;
            if (_permConfirmBtn != null) _permConfirmBtn.clicked += OnPermConfirm;
            if (_permVeil != null) _permVeil.RegisterCallback<ClickEvent>(OnPermVeilClicked);
        }

        private void UnwireRoot()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_refreshButton != null) _refreshButton.clicked -= OnRefreshClicked;
            if (_searchFieldFeatured != null)
                _searchFieldFeatured.UnregisterValueChangedCallback(OnSearchChanged);
            if (_searchFieldList != null)
                _searchFieldList.UnregisterValueChangedCallback(OnSearchChanged);

            if (_permCancelBtn != null) _permCancelBtn.clicked -= OnPermCancel;
            if (_permConfirmBtn != null) _permConfirmBtn.clicked -= OnPermConfirm;
            if (_permVeil != null) _permVeil.UnregisterCallback<ClickEvent>(OnPermVeilClicked);
        }

        private void OnPermVeilClicked(ClickEvent evt) => HidePermissionModal();
        private void OnPermCancel() => HidePermissionModal();
        private void OnPermConfirm()
        {
            var d = _pendingInstall;
            // dep toggle 스냅샷은 HidePermissionModal 전에 떠야 — Hide 가 _pluginDepToggles 를 비운다.
            // 시그너처 차이를 최소화하려고 별도 state 로 옮기지 않고 lambda 클로저로 캡처.
            var checkedPluginIds = new List<string>();
            foreach (var kv in _pluginDepToggles)
                if (kv.Value != null && kv.Value.value) checkedPluginIds.Add(kv.Key);

            HidePermissionModal();
            if (d != null)
            {
                RunInstallAsync(d).Forget();
                if (checkedPluginIds.Count > 0)
                    InstallExtraPluginsAsync(checkedPluginIds).Forget();
            }
        }

        // OnPermConfirm 클로저 헬퍼 — 체크된 플러그인을 순차 설치/연결.
        private async UniTaskVoid InstallExtraPluginsAsync(List<string> pluginIds)
        {
            if (pluginIds == null || pluginIds.Count == 0) return;
            if (string.IsNullOrEmpty(_currentAgentId) || _pluginLoadout == null) return;

            foreach (var id in pluginIds)
            {
                var plugin = _pluginCatalog?.GetById(id);
                if (plugin == null) continue;

                if (_pluginCredentials != null && !await _pluginCredentials.HasAllRequiredAsync(plugin))
                {
                    var modal = ResolvePluginCredModal();
                    if (modal == null) { ShowToast($"{plugin.DisplayName}: 자격증명 모달 미등록", ToastTone.Error); continue; }
                    var saved = await modal.AskAsync(plugin);
                    if (!saved) { ShowToast($"{plugin.DisplayName}: 자격증명 입력 취소", ToastTone.Info); continue; }
                }

                var ok = await _pluginLoadout.EquipAsync(_currentAgentId, id);
                ShowToast(
                    ok ? $"{plugin.DisplayName} 플러그인 연결됨" : $"{plugin.DisplayName} 연결 실패",
                    ok ? ToastTone.Success : ToastTone.Error);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnCloseClicked() => Close();
        private void OnRefreshClicked() => RefreshCatalogAsync(forceRefresh: true).Forget();

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            var next = (evt.newValue ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(next, _searchQuery, StringComparison.Ordinal)) return;
            _searchQuery = next;

            // 두 검색 필드 동기화 (사용자가 어디 입력하든 동일 상태)
            SyncSearchFields(evt.newValue ?? string.Empty);
            RenderActivePanel();
        }

        private void SyncSearchFields(string raw)
        {
            if (_searchFieldFeatured != null && _searchFieldFeatured.value != raw)
                _searchFieldFeatured.SetValueWithoutNotify(raw);
            if (_searchFieldList != null && _searchFieldList.value != raw)
                _searchFieldList.SetValueWithoutNotify(raw);
        }

        // ══════════════════════════════════════════════════
        //  Subscriptions
        // ══════════════════════════════════════════════════

        private void EnsureSubscriptions()
        {
            if (_catalog != null && _catalogSubscription == null)
                _catalogSubscription = _catalog.OnCatalogChanged.Subscribe(_ => RenderAll());

            if (_installer != null && _installSubscription == null)
                _installSubscription = _installer.OnInstallChanged.Subscribe(OnInstallStateChanged);

            if (_loadout != null && _loadoutSubscription == null)
                _loadoutSubscription = _loadout.OnLoadoutChanged.Subscribe(OnLoadoutChanged);

            // v4: 플러그인 카탈로그/장착 변경 → 카드 부분 갱신 + 미들웨어로 plugin registry push.
            if (_pluginCatalog != null && _pluginCatalogSubscription == null)
                _pluginCatalogSubscription = _pluginCatalog.OnCatalogChanged.Subscribe(_ =>
                {
                    RenderActivePanel();
                    PushPluginRegistryToMiddleware();
                });

            if (_pluginLoadout != null && _pluginLoadoutSubscription == null)
                _pluginLoadoutSubscription = _pluginLoadout.OnLoadoutChanged.Subscribe(OnPluginLoadoutChanged);
        }

        private void DisposeSubscriptions()
        {
            _catalogSubscription?.Dispose();
            _catalogSubscription = null;
            _installSubscription?.Dispose();
            _installSubscription = null;
            _loadoutSubscription?.Dispose();
            _loadoutSubscription = null;
            _pluginCatalogSubscription?.Dispose();
            _pluginCatalogSubscription = null;
            _pluginLoadoutSubscription?.Dispose();
            _pluginLoadoutSubscription = null;
        }

        private void OnPluginLoadoutChanged(OpenDesk.Core.Models.Plugins.AgentPluginLoadout loadout)
        {
            if (loadout == null) return;
            if (!string.Equals(loadout.AgentId, _currentAgentId, StringComparison.Ordinal)) return;

            var equippedSet = new HashSet<string>(loadout.EquippedPluginIds ?? (IEnumerable<string>)Array.Empty<string>());
            foreach (var kv in _pluginCards)
                kv.Value.SetEquipped(equippedSet.Contains(kv.Key));
        }

        private void OnInstallStateChanged(SkillInstallEvent evt)
        {
            ApplyInstallStateToCards(evt.SkillId, evt.IsInstalled, evt.InstallPath);
        }

        private void OnLoadoutChanged(AgentSkillLoadout loadout)
        {
            if (loadout == null) return;
            if (!string.Equals(loadout.AgentId, _currentAgentId, StringComparison.Ordinal)) return;

            var equippedSet = new HashSet<string>(loadout.EquippedSkillIds ?? Array.Empty<string>());
            foreach (var kv in _cards)
                kv.Value.SetEquipped(equippedSet.Contains(kv.Key));
            foreach (var kv in _rows)
                kv.Value.SetEquipped(equippedSet.Contains(kv.Key));

            if (!string.IsNullOrEmpty(_selectedSkillId))
                _detailElement?.SetEquipped(equippedSet.Contains(_selectedSkillId));
        }

        // ══════════════════════════════════════════════════
        //  Catalog refresh
        // ══════════════════════════════════════════════════

        private async UniTaskVoid RefreshCatalogAsync(bool forceRefresh)
        {
            if (_catalog == null) return;

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            try
            {
                await _catalog.RefreshAsync(forceRefresh, _refreshCts.Token);
                if (forceRefresh) ShowToast("카탈로그 새로고침 완료", ToastTone.Success, 1800);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillMarketView] 새로고침 실패: {ex.Message}");
                ShowToast("새로고침 실패 — 캐시 사용 중", ToastTone.Error);
            }
            RenderAll();
        }

        // ══════════════════════════════════════════════════
        //  Tabs
        // ══════════════════════════════════════════════════

        private void BuildTabs()
        {
            if (_tabBar == null) return;
            if (_tabs.Count > 0 && _featuredTabButton != null) return;

            _tabBar.Clear();
            _tabs.Clear();

            _featuredTabButton = new TopTabElement("추천", null, () => SelectTab(FeaturedTabId));
            _tabBar.Add(_featuredTabButton);

            foreach (var category in CategoryOrder)
            {
                var captured = category;
                var tab = new TopTabElement(
                    category.DisplayName(),
                    string.Empty,
                    () => SelectTab(captured.ToSerializedKey()));
                _tabs.Add(tab);
                _tabBar.Add(tab);
            }

            SelectTab(FeaturedTabId);
        }

        private void SelectTab(string tabId)
        {
            _selectedTabId = tabId ?? FeaturedTabId;
            _selectedSkillId = null;

            // visual selection
            var isFeatured = string.Equals(_selectedTabId, FeaturedTabId, StringComparison.Ordinal);
            _featuredTabButton?.SetSelected(isFeatured);
            for (int i = 0; i < _tabs.Count && i < CategoryOrder.Length; i++)
            {
                var key = CategoryOrder[i].ToSerializedKey();
                _tabs[i].SetSelected(!isFeatured &&
                                     string.Equals(_selectedTabId, key, StringComparison.Ordinal));
            }

            // panel toggle
            if (_featuredPanel != null)
                _featuredPanel.style.display = isFeatured ? DisplayStyle.Flex : DisplayStyle.None;
            if (_categoryPanel != null)
                _categoryPanel.style.display = isFeatured ? DisplayStyle.None : DisplayStyle.Flex;

            RenderActivePanel();
        }

        private bool TryGetSelectedCategory(out SkillCategory category)
        {
            category = SkillCategory.General;
            if (string.Equals(_selectedTabId, FeaturedTabId, StringComparison.Ordinal)) return false;
            foreach (var c in CategoryOrder)
            {
                if (string.Equals(c.ToSerializedKey(), _selectedTabId, StringComparison.Ordinal))
                {
                    category = c;
                    return true;
                }
            }
            return false;
        }

        // ══════════════════════════════════════════════════
        //  Render orchestration
        // ══════════════════════════════════════════════════

        private void RenderAll()
        {
            UpdateTabCounts();
            RenderActivePanel();
        }

        private void RenderActivePanel()
        {
            if (TryGetSelectedCategory(out var category))
                RenderCategoryPane(category);
            else
                RenderFeaturedPanel();
        }

        private void UpdateTabCounts()
        {
            if (_catalog == null) return;
            var all = _catalog.GetAll();
            if (all == null) return;

            for (int i = 0; i < _tabs.Count && i < CategoryOrder.Length; i++)
            {
                var count = all.Count(d => d.Category == CategoryOrder[i]);
                _tabs[i].SetCount(count.ToString());
            }
        }

        // ══════════════════════════════════════════════════
        //  Featured panel
        // ══════════════════════════════════════════════════

        private void RenderFeaturedPanel()
        {
            if (_catalog == null) return;
            EnsureKindFilterBar();

            var all = _catalog.GetAll() ?? new List<SkillDescriptor>();
            var filteredSkills = string.IsNullOrEmpty(_searchQuery)
                ? all
                : all.Where(MatchesQuery).ToList();

            // v4: kind 필터 — All/Skill 이면 스킬 섹션 표시, Plugin 이면 숨김.
            var showSkills = _kindFilter != MarketKindFilter.Plugin;
            if (showSkills)
            {
                RenderHero(filteredSkills);
                RenderRecommendedGrid(filteredSkills);
                RenderNewlyGrid(filteredSkills);
            }
            else
            {
                if (_hero != null) _hero.style.display = DisplayStyle.None;
                if (_recommendedSection != null) _recommendedSection.style.display = DisplayStyle.None;
                if (_newlyGrid != null) _newlyGrid.Clear();
                if (_newlyHeader != null) _newlyHeader.text = string.Empty;
                if (_newlyHint != null) _newlyHint.text = string.Empty;
            }

            // v4: All/Plugin 이면 플러그인 섹션 표시. Skill 이면 숨김.
            RenderPluginsSection(showVisible: _kindFilter != MarketKindFilter.Skill);
        }

        // ── v4: kind 필터 (전체 · 스킬 · 플러그인) ─────────────────────

        private void EnsureKindFilterBar()
        {
            if (_kindFilterBar != null) return;
            if (_featuredScroll == null) return;

            // featured scroll 의 content container 맨 앞에 chip row 를 inject.
            _kindFilterBar = new VisualElement();
            _kindFilterBar.AddToClassList("skill-market__kind-bar");

            void AddChip(MarketKindFilter kind, string label)
            {
                var btn = new Button(() => SetKindFilter(kind)) { text = label };
                btn.AddToClassList("skill-market__kind-chip");
                if (_kindFilter == kind) btn.AddToClassList("skill-market__kind-chip--active");
                _kindFilterBar.Add(btn);
                _kindChips[kind] = btn;
            }

            AddChip(MarketKindFilter.All, "전체");
            AddChip(MarketKindFilter.Skill, "스킬");
            AddChip(MarketKindFilter.Plugin, "플러그인");

            // ScrollView 의 contentContainer 맨 앞에 삽입.
            var container = _featuredScroll.contentContainer;
            container.Insert(0, _kindFilterBar);
        }

        private void SetKindFilter(MarketKindFilter kind)
        {
            if (_kindFilter == kind) return;
            _kindFilter = kind;
            foreach (var kv in _kindChips)
                kv.Value.EnableInClassList("skill-market__kind-chip--active", kv.Key == kind);
            RenderActivePanel();
        }

        // ── v4: 플러그인 섹션 ────────────────────────────────────────

        private VisualElement _pluginsSection;
        private Label _pluginsSectionHeader;
        private Label _pluginsSectionHint;
        private VisualElement _pluginsGrid;
        private Label _pluginsEmpty;

        private void RenderPluginsSection(bool showVisible)
        {
            EnsurePluginsSectionElements();
            if (_pluginsSection == null) return;

            if (!showVisible || _pluginCatalog == null)
            {
                _pluginsSection.style.display = DisplayStyle.None;
                return;
            }
            _pluginsSection.style.display = DisplayStyle.Flex;

            var all = _pluginCatalog.GetAll() ?? Array.Empty<PluginDescriptor>();
            var filtered = string.IsNullOrEmpty(_searchQuery)
                ? all
                : all.Where(MatchesQueryPlugin).ToList();

            var equippedIds = (_pluginLoadout != null && !string.IsNullOrEmpty(_currentAgentId))
                ? (_pluginLoadout.GetLoadout(_currentAgentId)?.EquippedPluginIds ?? (IEnumerable<string>)Array.Empty<string>())
                : Array.Empty<string>();
            var equippedSet = new HashSet<string>(equippedIds);

            _pluginsGrid.Clear();
            _pluginCards.Clear();

            if (_pluginsEmpty != null)
                _pluginsEmpty.style.display = filtered.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_pluginsSectionHeader != null) _pluginsSectionHeader.text = "플러그인";
            if (_pluginsSectionHint != null)
                _pluginsSectionHint.text = filtered.Count == 0 ? string.Empty : $"{filtered.Count}개";

            foreach (var descriptor in filtered.OrderByDescending(d => d.IsInstalled).ThenBy(d => d.DisplayName))
            {
                var card = new PluginCardElement(descriptor);
                card.SetEquipped(equippedSet.Contains(descriptor.Id));
                card.InstallClicked = OnPluginInstallClicked;
                card.UninstallClicked = OnPluginUninstallClicked;
                card.EquipClicked = OnPluginEquipClicked;
                card.UnequipClicked = OnPluginUnequipClicked;
                _pluginsGrid.Add(card);
                _pluginCards[descriptor.Id] = card;
            }
        }

        private void EnsurePluginsSectionElements()
        {
            if (_pluginsSection != null) return;
            if (_featuredScroll == null) return;

            _pluginsSection = new VisualElement();
            _pluginsSection.AddToClassList("skill-market__plugins-section");

            var head = new VisualElement();
            head.AddToClassList("skill-market__plugins-head");
            _pluginsSection.Add(head);

            _pluginsSectionHeader = new Label("플러그인");
            _pluginsSectionHeader.AddToClassList("skill-market__plugins-title");
            _pluginsSectionHeader.AddToClassList("od-heading-md");
            head.Add(_pluginsSectionHeader);

            _pluginsSectionHint = new Label(string.Empty);
            _pluginsSectionHint.AddToClassList("skill-market__plugins-hint");
            _pluginsSectionHint.AddToClassList("od-caption");
            head.Add(_pluginsSectionHint);

            _pluginsGrid = new VisualElement();
            _pluginsGrid.AddToClassList("skill-market__plugins-grid");
            _pluginsSection.Add(_pluginsGrid);

            _pluginsEmpty = new Label("아직 연결된 플러그인이 없어요.");
            _pluginsEmpty.AddToClassList("skill-market__plugins-empty");
            _pluginsEmpty.AddToClassList("od-caption");
            _pluginsSection.Add(_pluginsEmpty);

            _featuredScroll.contentContainer.Add(_pluginsSection);
        }

        private bool MatchesQueryPlugin(PluginDescriptor d)
        {
            if (d == null) return false;
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            var q = _searchQuery;
            return (d.DisplayName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                   (d.Description ?? string.Empty).ToLowerInvariant().Contains(q);
        }

        // ── v4: 플러그인 install / equip 핸들러 ───────────────────────

        private async void OnPluginInstallClicked(PluginDescriptor descriptor)
        {
            if (descriptor == null || _pluginLoadout == null || string.IsNullOrEmpty(_currentAgentId))
            {
                ShowToast("에이전트가 선택되지 않았습니다", ToastTone.Error);
                return;
            }

            // 1) 자격증명 누락 시 모달 호출 (PluginCredentialModal — 씬에 배치된 경우만 lazy lookup).
            if (_pluginCredentials != null && !await _pluginCredentials.HasAllRequiredAsync(descriptor))
            {
                var modal = ResolvePluginCredModal();
                if (modal == null)
                {
                    ShowToast("자격증명 모달이 등록되지 않았습니다", ToastTone.Error);
                    return;
                }
                var saved = await modal.AskAsync(descriptor);
                if (!saved)
                {
                    ShowToast("자격증명 입력이 취소되었습니다", ToastTone.Info);
                    return;
                }
            }

            // 2) 장착 = 설치 (PluginCatalogService 는 별도 install API 가 없고 loadout equip 이 곧 사용).
            var ok = await _pluginLoadout.EquipAsync(_currentAgentId, descriptor.Id);
            ShowToast(
                ok ? $"{descriptor.DisplayName} 플러그인 연결됨" : $"{descriptor.DisplayName} 연결 실패",
                ok ? ToastTone.Success : ToastTone.Error);
        }

        private async void OnPluginUninstallClicked(PluginDescriptor descriptor)
        {
            if (descriptor == null || _pluginLoadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            await _pluginLoadout.UnequipAsync(_currentAgentId, descriptor.Id);
        }

        private async void OnPluginEquipClicked(PluginDescriptor descriptor) =>
            await EnsurePluginEquipped(descriptor);

        private async void OnPluginUnequipClicked(PluginDescriptor descriptor)
        {
            if (descriptor == null || _pluginLoadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            await _pluginLoadout.UnequipAsync(_currentAgentId, descriptor.Id);
        }

        private async UniTask EnsurePluginEquipped(PluginDescriptor descriptor)
        {
            if (descriptor == null || _pluginLoadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            if (_pluginCredentials != null && !await _pluginCredentials.HasAllRequiredAsync(descriptor))
            {
                var modal = ResolvePluginCredModal();
                if (modal == null) { ShowToast("자격증명 모달이 등록되지 않았습니다", ToastTone.Error); return; }
                var saved = await modal.AskAsync(descriptor);
                if (!saved) return;
            }
            await _pluginLoadout.EquipAsync(_currentAgentId, descriptor.Id);
        }

        // ── v4: 미들웨어로 plugin registry push ───────────────────────

        private void PushPluginRegistryToMiddleware()
        {
            if (_chat == null || _pluginCatalog == null) return;
            var all = _pluginCatalog.GetAll() ?? Array.Empty<PluginDescriptor>();
            var entries = all
                .Where(d => d != null && d.IsInstalled)
                .Select(d => new OpenDesk.Claude.Models.PluginRegistryEntry
                {
                    id = d.Id,
                    display_name = d.DisplayName ?? d.Id,
                    vendor = d.Vendor.ToString(),
                    author = d.Vendor.ToString(),
                    capabilities = d.Capabilities != null ? d.Capabilities.ToArray() : Array.Empty<string>(),
                })
                .ToArray();
            _chat.SendPluginRegistry(_currentAgentId ?? string.Empty, entries);
        }

        private void RenderHero(IReadOnlyList<SkillDescriptor> pool)
        {
            if (_hero == null) return;

            var hero = PickHero(pool);
            if (hero == null)
            {
                _hero.style.display = DisplayStyle.None;
                return;
            }

            _hero.style.display = DisplayStyle.Flex;
            if (_heroKicker != null) _heroKicker.text = "이주의 추천";
            if (_heroMonoChar != null) _heroMonoChar.text = MonogramFor(hero);
            if (_heroTitle != null) _heroTitle.text = hero.DisplayName ?? string.Empty;
            if (_heroMeta != null)
            {
                var author = string.IsNullOrEmpty(hero.Author) ? "" : $"by {hero.Author}";
                var catName = hero.Category.DisplayName();
                _heroMeta.text = string.IsNullOrEmpty(author) ? catName : $"{author} · {catName}";
            }
            if (_heroDesc != null)
                _heroDesc.text = string.IsNullOrEmpty(hero.Description)
                    ? "설치 후 채팅창에서 호출하거나 동료에게 직접 요청할 수 있어요."
                    : hero.Description;
        }

        private SkillDescriptor PickHero(IReadOnlyList<SkillDescriptor> pool)
        {
            if (pool == null || pool.Count == 0) return null;

            if (_recommendations != null && _currentRole != AgentRole.None)
            {
                var roleRecs = _recommendations.RecommendForRole(_currentRole, limit: 3);
                if (roleRecs != null)
                {
                    foreach (var r in roleRecs)
                    {
                        if (r != null && pool.Any(p => p.Id == r.Id)) return r;
                    }
                }
            }
            return pool
                .OrderByDescending(d => d.Downloads)
                .ThenByDescending(d => d.Rating)
                .FirstOrDefault();
        }

        private void RenderRecommendedGrid(IReadOnlyList<SkillDescriptor> pool)
        {
            if (_recommendedGrid == null || _recommendedSection == null) return;

            _recommendedGrid.Clear();
            ClearCardCacheForContainer(_recommendedGrid);

            List<SkillDescriptor> picks = null;
            string header;
            string hint;

            if (_recommendations != null && _currentRole != AgentRole.None)
            {
                picks = _recommendations
                    .RecommendForRole(_currentRole, limit: 4)
                    ?.Where(d => pool.Any(p => p.Id == d.Id))
                    .ToList();
                header = $"{RoleDisplayName(_currentRole)} 역할 추천";
                hint = "역할에 맞춰 큐레이션";
            }
            else
            {
                picks = pool
                    .OrderByDescending(d => d.Downloads)
                    .ThenByDescending(d => d.Rating)
                    .Take(4)
                    .ToList();
                header = "지금 많이 쓰는 스킬";
                hint = "이번 주 설치 기준";
            }

            if (_recommendedHeader != null) _recommendedHeader.text = header;
            if (_recommendedHint != null) _recommendedHint.text = hint;

            if (picks == null || picks.Count == 0)
            {
                _recommendedSection.style.display = DisplayStyle.None;
                return;
            }
            _recommendedSection.style.display = DisplayStyle.Flex;

            foreach (var d in picks)
            {
                var card = CreateCard(d, gridColumns: 4);
                _recommendedGrid.Add(card);
                _cards[d.Id] = card;
            }
        }

        private void RenderNewlyGrid(IReadOnlyList<SkillDescriptor> pool)
        {
            if (_newlyGrid == null) return;

            _newlyGrid.Clear();
            ClearCardCacheForContainer(_newlyGrid);

            var picks = pool
                .OrderByDescending(d => d.PublishedAt)
                .ThenBy(d => d.DisplayName)
                .Take(6)
                .ToList();

            if (_newlyHeader != null) _newlyHeader.text = "새로 추가된 스킬";
            if (_newlyHint != null) _newlyHint.text = picks.Count == 0 ? string.Empty : $"{picks.Count}개";

            foreach (var d in picks)
            {
                var card = CreateCard(d, gridColumns: 3);
                _newlyGrid.Add(card);
                _cards[d.Id] = card;
            }
        }

        // ══════════════════════════════════════════════════
        //  Category two-pane
        // ══════════════════════════════════════════════════

        private void RenderCategoryPane(SkillCategory category)
        {
            if (_listScroll == null || _catalog == null) return;

            // v4: kind 필터에 따라 스킬/플러그인 row 를 함께 노출.
            var includeSkills = _kindFilter != MarketKindFilter.Plugin;
            var includePlugins = _kindFilter != MarketKindFilter.Skill;

            var skillList = includeSkills
                ? (_catalog.GetAll() ?? new List<SkillDescriptor>())
                    .Where(d => d.Category == category)
                    .Where(MatchesQuery)
                    .OrderByDescending(d => d.IsInstalled)
                    .ThenByDescending(d => d.Downloads)
                    .ThenBy(d => d.DisplayName)
                    .ToList()
                : new List<SkillDescriptor>();

            var pluginList = includePlugins && _pluginCatalog != null
                ? (_pluginCatalog.GetAll() ?? Array.Empty<PluginDescriptor>())
                    .Where(p => PluginMatchesCategory(p, category))
                    .Where(MatchesQueryPlugin)
                    .OrderByDescending(p => p.IsInstalled)
                    .ThenBy(p => p.DisplayName)
                    .ToList()
                : new List<PluginDescriptor>();

            _listScroll.Clear();
            _rows.Clear();

            if (skillList.Count == 0 && pluginList.Count == 0)
            {
                _listEmpty?.AddToClassList("skill-market__list-empty--visible");
                _detailEmpty?.AddToClassList("skill-market__detail-empty--visible");
                if (_detailElement != null) _detailElement.style.display = DisplayStyle.None;
                return;
            }

            _listEmpty?.RemoveFromClassList("skill-market__list-empty--visible");

            // 스킬 row 들 (있을 때만 헤더 라벨 1줄 + row 들)
            if (skillList.Count > 0)
            {
                _listScroll.Add(BuildListSectionHeader($"스킬 {skillList.Count}"));
                foreach (var d in skillList)
                {
                    var row = new SkillRowElement(d, () => OnRowSelected(d.Id));
                    row.SetEquipped(IsEquipped(d.Id));
                    _listScroll.Add(row);
                    _rows[d.Id] = row;
                }
            }

            // 플러그인 row 들 (cool slate; 클릭 시 install/equip 인라인)
            if (pluginList.Count > 0)
            {
                _listScroll.Add(BuildListSectionHeader($"플러그인 {pluginList.Count}"));
                foreach (var p in pluginList)
                    _listScroll.Add(BuildCategoryPaneInlinePluginRow(p));
            }

            // 스킬이 있을 때만 detail 선택. 없으면 detail empty.
            if (skillList.Count > 0)
            {
                var nextSelectedId = skillList.Any(d => d.Id == _selectedSkillId)
                    ? _selectedSkillId
                    : skillList[0].Id;
                OnRowSelected(nextSelectedId);
            }
            else
            {
                _detailEmpty?.AddToClassList("skill-market__detail-empty--visible");
                if (_detailElement != null) _detailElement.style.display = DisplayStyle.None;
            }
        }

        // ── v4 카테고리 pane 헬퍼 ───────────────────────────────────

        private static VisualElement BuildListSectionHeader(string text)
        {
            var label = new Label(text);
            label.AddToClassList("skill-market__list-section-head");
            label.AddToClassList("od-caption");
            return label;
        }

        // 카테고리 pane 안 인라인 플러그인 row — 작은 cool slate 카드.
        // 클릭하면 OnPluginInstallClicked 핸들러 호출 → 자격증명 모달 → equip 흐름.
        private VisualElement BuildCategoryPaneInlinePluginRow(PluginDescriptor plugin)
        {
            var row = new VisualElement();
            row.AddToClassList("skill-market__plugin-list-row");

            var mono = new Label(plugin.DisplayName != null && plugin.DisplayName.Length > 0
                ? char.ToUpperInvariant(plugin.DisplayName[0]).ToString()
                : "?");
            mono.AddToClassList("skill-market__plugin-list-mono");
            row.Add(mono);

            var body = new VisualElement();
            body.AddToClassList("skill-market__plugin-list-body");
            row.Add(body);

            var nameLine = new VisualElement();
            nameLine.AddToClassList("skill-market__plugin-list-name-line");
            body.Add(nameLine);

            var name = new Label(plugin.DisplayName ?? plugin.Id ?? string.Empty);
            name.AddToClassList("skill-market__plugin-list-name");
            nameLine.Add(name);

            var typeBadge = new Label("플러그인");
            typeBadge.AddToClassList("skill-market__plugin-list-badge");
            nameLine.Add(typeBadge);

            var desc = new Label(plugin.Description ?? string.Empty);
            desc.AddToClassList("skill-market__plugin-list-desc");
            body.Add(desc);

            var actionBtn = new Button(() =>
            {
                if (plugin.IsInstalled) OnPluginUnequipClicked(plugin);
                else OnPluginInstallClicked(plugin);
            })
            {
                text = plugin.IsInstalled ? "연결됨" : "연결",
            };
            actionBtn.AddToClassList("skill-market__plugin-list-action");
            if (plugin.IsInstalled) actionBtn.AddToClassList("skill-market__plugin-list-action--installed");
            row.Add(actionBtn);

            return row;
        }

        // capability 기반으로 플러그인이 어떤 SkillCategory 와 어울리는지 추론.
        // capabilities 가 비어 있으면 vendor 기반 fallback (Custom 은 General).
        private static bool PluginMatchesCategory(PluginDescriptor plugin, SkillCategory category)
        {
            if (plugin == null) return false;
            var caps = plugin.Capabilities ?? Array.Empty<string>();
            foreach (var cap in caps)
            {
                if (cap == null) continue;
                var prefix = cap.Contains('.') ? cap.Substring(0, cap.IndexOf('.')) : cap;
                var mapped = CapabilityPrefixToCategory(prefix);
                if (mapped == category) return true;
            }
            // capabilities 가 빈 경우 vendor fallback
            return caps.Count == 0 && VendorToCategoryFallback(plugin.Vendor) == category;
        }

        private static SkillCategory CapabilityPrefixToCategory(string prefix) => prefix switch
        {
            "mail"     => SkillCategory.Email,
            "calendar" => SkillCategory.Scheduling,
            "doc"      => SkillCategory.Document,
            "file"     => SkillCategory.Document,
            "chat"     => SkillCategory.Social,
            "message"  => SkillCategory.Social,
            "issue"    => SkillCategory.Development,
            "code"     => SkillCategory.Coding,
            "web"      => SkillCategory.Search,
            "search"   => SkillCategory.Search,
            "design"   => SkillCategory.Design,
            "trans"    => SkillCategory.Translation,
            "analytics" or "analysis" or "data" => SkillCategory.Analysis,
            _          => SkillCategory.General,
        };

        private static SkillCategory VendorToCategoryFallback(PluginVendor vendor) => vendor switch
        {
            PluginVendor.Gmail          => SkillCategory.Email,
            PluginVendor.GoogleCalendar => SkillCategory.Scheduling,
            PluginVendor.GoogleDrive    => SkillCategory.Document,
            PluginVendor.Notion         => SkillCategory.Document,
            PluginVendor.Figma          => SkillCategory.Design,
            PluginVendor.Slack          => SkillCategory.Social,
            PluginVendor.Linear         => SkillCategory.Development,
            PluginVendor.GitHub         => SkillCategory.Development,
            _                           => SkillCategory.General,
        };

        private void OnRowSelected(string skillId)
        {
            _selectedSkillId = skillId;
            foreach (var kv in _rows)
                kv.Value.SetSelected(kv.Key == skillId);

            var descriptor = _catalog?.GetAll()?.FirstOrDefault(d => d.Id == skillId);
            if (descriptor == null)
            {
                _detailEmpty?.AddToClassList("skill-market__detail-empty--visible");
                if (_detailElement != null) _detailElement.style.display = DisplayStyle.None;
                return;
            }

            _detailEmpty?.RemoveFromClassList("skill-market__detail-empty--visible");
            if (_detailElement != null)
            {
                _detailElement.style.display = DisplayStyle.Flex;
                _detailElement.Bind(descriptor, IsEquipped(skillId));
            }
        }

        // ══════════════════════════════════════════════════
        //  Card factory + actions
        // ══════════════════════════════════════════════════

        private SkillCardElement CreateCard(SkillDescriptor descriptor, int gridColumns)
        {
            var card = new SkillCardElement(descriptor, gridColumns);
            card.SetEquipped(IsEquipped(descriptor.Id));
            card.InstallClicked    = OnInstallClicked;
            card.UninstallClicked  = OnUninstallClicked;
            card.EquipClicked      = OnEquipClicked;
            card.UnequipClicked    = OnUnequipClicked;
            return card;
        }

        private void ClearCardCacheForContainer(VisualElement container)
        {
            if (container == null) return;
            var ids = container.Children().OfType<SkillCardElement>().Select(c => c.SkillId).ToList();
            foreach (var id in ids) _cards.Remove(id);
        }

        private bool IsEquipped(string skillId)
        {
            if (_loadout == null || string.IsNullOrEmpty(_currentAgentId)) return false;
            var current = _loadout.GetLoadout(_currentAgentId);
            return current?.EquippedSkillIds?.Contains(skillId) ?? false;
        }

        private bool MatchesQuery(SkillDescriptor descriptor)
        {
            if (descriptor == null) return false;
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            var q = _searchQuery;
            return (descriptor.DisplayName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                   (descriptor.Description ?? string.Empty).ToLowerInvariant().Contains(q);
        }

        private void OnInstallClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null) return;
            ShowPermissionModal(descriptor);
        }

        private async UniTaskVoid RunInstallAsync(SkillDescriptor descriptor)
        {
            if (descriptor == null || _installer == null) return;

            ApplyToCards(descriptor.Id, card => card.SetProgress(0f, visible: true));
            var isSelected = string.Equals(descriptor.Id, _selectedSkillId, StringComparison.Ordinal);
            if (isSelected) _detailElement?.SetInstallProgress("downloading", 0f);

            try
            {
                var progress = ProgressFactory.Create<float>(value =>
                {
                    ApplyToCards(descriptor.Id, card => card.SetProgress(value, visible: true));
                    if (isSelected) _detailElement?.SetInstallProgress(PhaseForPercent(value), value);
                });

                var ok = await _installer.InstallAsync(descriptor.Id, progress, destroyCancellationToken);
                ShowToast(
                    ok ? $"{descriptor.DisplayName} 설치 완료" : $"{descriptor.DisplayName} 설치 실패",
                    ok ? ToastTone.Success : ToastTone.Error);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillMarketView] 설치 오류: {ex.Message}");
                ShowToast("설치 중 오류 발생", ToastTone.Error);
            }
            finally
            {
                ApplyToCards(descriptor.Id, card => card.SetProgress(0f, visible: false));
                if (isSelected) _detailElement?.SetInstallProgress(null, 0f);
            }
        }

        private static string PhaseForPercent(float p)
        {
            if (p < 0.6f) return "downloading";
            if (p < 0.9f) return "verifying";
            return "configuring";
        }

        private async void OnUninstallClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null || _installer == null) return;
            await _installer.UninstallAsync(descriptor.Id, destroyCancellationToken);
        }

        private async void OnEquipClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null || _loadout == null || string.IsNullOrEmpty(_currentAgentId))
            {
                ShowToast("에이전트가 선택되지 않았습니다", ToastTone.Error);
                return;
            }
            await _loadout.EquipAsync(_currentAgentId, descriptor.Id);
        }

        private async void OnUnequipClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null || _loadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            await _loadout.UnequipAsync(_currentAgentId, descriptor.Id);
        }

        // ══════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════

        private void ApplyInstallStateToCards(string skillId, bool isInstalled, string installPath)
        {
            ApplyToCards(skillId, card => card.SetInstalled(isInstalled, installPath));
            if (_rows.TryGetValue(skillId, out var row))
                row.SetInstalled(isInstalled);
            if (string.Equals(skillId, _selectedSkillId, StringComparison.Ordinal))
                _detailElement?.SetInstalled(isInstalled, installPath);
        }

        private void ApplyToCards(string skillId, Action<SkillCardElement> action)
        {
            if (string.IsNullOrEmpty(skillId) || action == null) return;
            if (_cards.TryGetValue(skillId, out var c)) action(c);
        }

        private enum ToastTone { Info, Success, Error }

        private void ShowToast(string text, ToastTone tone = ToastTone.Info, int durationMs = 2800)
        {
            if (_toast == null || _toastText == null) return;
            if (string.IsNullOrEmpty(text)) { HideToast(); return; }

            _toastText.text = text;

            _toast.RemoveFromClassList("skill-market__toast--success");
            _toast.RemoveFromClassList("skill-market__toast--error");
            switch (tone)
            {
                case ToastTone.Success: _toast.AddToClassList("skill-market__toast--success"); break;
                case ToastTone.Error:   _toast.AddToClassList("skill-market__toast--error");   break;
            }
            _toast.AddToClassList("skill-market__toast--visible");

            _toastCts?.Cancel();
            _toastCts?.Dispose();
            _toastCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            AutoHideToastAsync(durationMs, _toastCts.Token).Forget();
        }

        private void HideToast()
        {
            _toastCts?.Cancel();
            _toastCts?.Dispose();
            _toastCts = null;
            if (_toast != null) _toast.RemoveFromClassList("skill-market__toast--visible");
        }

        private async UniTaskVoid AutoHideToastAsync(int durationMs, CancellationToken token)
        {
            try
            {
                await UniTask.Delay(durationMs, cancellationToken: token);
                if (_toast != null) _toast.RemoveFromClassList("skill-market__toast--visible");
            }
            catch (OperationCanceledException) { }
        }

        // ══════════════════════════════════════════════════
        //  Permission Modal
        // ══════════════════════════════════════════════════

        private void ShowPermissionModal(SkillDescriptor d)
        {
            if (_permModal == null || d == null) return;

            _pendingInstall = d;

            if (_permMono != null) _permMono.text = MonogramFor(d);
            // v4 카피: "X 스킬 설치" — "스킬" 단어 명시.
            if (_permTitle != null) _permTitle.text = $"{d.DisplayName} 스킬 설치";
            if (_permAuthor != null)
                _permAuthor.text = string.IsNullOrEmpty(d.Author) ? string.Empty : $"by {d.Author}";

            BuildPermissionRows(d);
            // v4: 필요/선택 플러그인 의존성 섹션 + CTA 카운트 갱신.
            BuildPluginDependencySections(d);
            UpdateInstallCtaLabel(d);

            _permModal.AddToClassList("skill-market__modal--visible");
        }

        private void HidePermissionModal()
        {
            _pendingInstall = null;
            ClearPluginDependencySections();
            if (_permConfirmBtn != null) _permConfirmBtn.text = "설치";
            _permModal?.RemoveFromClassList("skill-market__modal--visible");
        }

        private void BuildPermissionRows(SkillDescriptor d)
        {
            if (_permList == null) return;
            _permList.Clear();

            var perms = PermissionCatalog.ResolveFor(d);
            if (perms == null || perms.Count == 0)
            {
                var net = PermissionCatalog.Get("net");
                _permList.Add(BuildPermRow(net, isFirst: true));
                return;
            }

            for (int i = 0; i < perms.Count; i++)
                _permList.Add(BuildPermRow(perms[i], isFirst: i == 0));
        }

        // ── v4: 스킬 설치 모달 안의 "필요한 플러그인 / 선택 플러그인" 섹션 ──────

        // 모달이 매번 다시 빌드되므로 dependency 섹션도 동적으로 생성/제거한다.
        private VisualElement _pluginDepSection;
        // pluginId → "checked?" — 사용자가 '함께 설치' 토글한 상태.
        private readonly Dictionary<string, Toggle> _pluginDepToggles = new();

        private void BuildPluginDependencySections(SkillDescriptor d)
        {
            ClearPluginDependencySections();
            if (d == null || _permList == null) return;

            var required = d.RequiredPlugins ?? Array.Empty<string>();
            var optional = d.OptionalPlugins ?? Array.Empty<string>();
            if (required.Count == 0 && optional.Count == 0) return;

            _pluginDepSection = new VisualElement();
            _pluginDepSection.AddToClassList("skill-market__plugin-deps");

            if (required.Count > 0)
                _pluginDepSection.Add(BuildPluginDepGroup("필요한 플러그인", required, isRequired: true));

            if (optional.Count > 0)
                _pluginDepSection.Add(BuildPluginDepGroup("선택 플러그인 (있으면 더 잘 작동)", optional, isRequired: false));

            // _permList 와 modal-foot 사이에 inject.
            var parent = _permList.parent;
            if (parent == null) return;
            int insertAt = parent.IndexOf(_permList) + 1;
            parent.Insert(insertAt, _pluginDepSection);
        }

        private void ClearPluginDependencySections()
        {
            _pluginDepToggles.Clear();
            if (_pluginDepSection?.parent != null)
                _pluginDepSection.parent.Remove(_pluginDepSection);
            _pluginDepSection = null;
        }

        private VisualElement BuildPluginDepGroup(string title, IReadOnlyList<string> pluginIds, bool isRequired)
        {
            var group = new VisualElement();
            group.AddToClassList("skill-market__plugin-dep-group");

            var label = new Label(title);
            label.AddToClassList("skill-market__plugin-dep-title");
            label.AddToClassList("od-caption");
            group.Add(label);

            foreach (var pluginId in pluginIds)
            {
                var plugin = _pluginCatalog?.GetById(pluginId);
                group.Add(BuildPluginDepRow(pluginId, plugin, isRequired));
            }
            return group;
        }

        private VisualElement BuildPluginDepRow(string pluginId, PluginDescriptor plugin, bool isRequired)
        {
            var row = new VisualElement();
            row.AddToClassList("skill-market__plugin-dep-row");

            // 좌측 mono — 플러그인 톤 (쿨 슬레이트).
            var mono = new Label(plugin != null
                ? char.ToUpperInvariant((plugin.DisplayName ?? plugin.Id ?? "?")[0]).ToString()
                : "?");
            mono.AddToClassList("skill-market__plugin-dep-mono");
            row.Add(mono);

            var body = new VisualElement();
            body.AddToClassList("skill-market__plugin-dep-body");
            row.Add(body);

            var name = new Label(plugin != null ? (plugin.DisplayName ?? pluginId) : pluginId);
            name.AddToClassList("skill-market__plugin-dep-name");
            body.Add(name);

            var sub = new Label(plugin != null
                ? (plugin.IsInstalled ? "이미 연결됨" : (isRequired ? "함께 설치할 수 있어요" : "선택 설치"))
                : "미등록 플러그인 — 플러그인 마켓에서 먼저 확인이 필요해요");
            sub.AddToClassList("skill-market__plugin-dep-sub");
            sub.AddToClassList("od-caption");
            body.Add(sub);

            // 미설치 + 카탈로그에 존재 → 함께 설치 체크박스 (required: default ON, optional: default OFF).
            if (plugin != null && !plugin.IsInstalled)
            {
                var toggle = new Toggle();
                toggle.value = isRequired;
                toggle.AddToClassList("skill-market__plugin-dep-toggle");
                toggle.RegisterValueChangedCallback(_ => UpdateInstallCtaLabel(_pendingInstall));
                row.Add(toggle);
                _pluginDepToggles[pluginId] = toggle;
            }
            else if (plugin != null && plugin.IsInstalled)
            {
                var badge = new Label("[OK]");
                badge.AddToClassList("skill-market__plugin-dep-ok");
                row.Add(badge);
            }

            return row;
        }

        private void UpdateInstallCtaLabel(SkillDescriptor d)
        {
            if (_permConfirmBtn == null) return;
            int checkedCount = 0;
            foreach (var kv in _pluginDepToggles)
                if (kv.Value != null && kv.Value.value) checkedCount++;

            _permConfirmBtn.text = checkedCount > 0
                ? $"스킬 + 필요 플러그인 함께 설치 ({checkedCount})"
                : (d != null && (d.RequiredPlugins?.Count ?? 0) > 0
                    ? "스킬만 설치"
                    : "스킬 설치");
        }

        // 설치 확정 시 호출 — 체크된 플러그인 ID 들을 함께 install/equip.
        // RunInstallAsync 가 끝난 뒤 background 로 처리해 모달 흐름은 막지 않는다.
        private async UniTaskVoid CoInstallCheckedPluginsAsync()
        {
            if (string.IsNullOrEmpty(_currentAgentId) || _pluginLoadout == null) return;
            var ids = new List<string>();
            foreach (var kv in _pluginDepToggles)
                if (kv.Value != null && kv.Value.value) ids.Add(kv.Key);
            if (ids.Count == 0) return;

            foreach (var id in ids)
            {
                var plugin = _pluginCatalog?.GetById(id);
                if (plugin == null) continue;

                // 자격증명 누락 시 모달 호출 (PluginCredentialModal 가 등록된 경우만).
                if (_pluginCredentials != null && !await _pluginCredentials.HasAllRequiredAsync(plugin))
                {
                    var modal = ResolvePluginCredModal();
                    if (modal == null) { ShowToast($"{plugin.DisplayName}: 자격증명 모달 미등록", ToastTone.Error); continue; }
                    var saved = await modal.AskAsync(plugin);
                    if (!saved) { ShowToast($"{plugin.DisplayName}: 자격증명 입력 취소", ToastTone.Info); continue; }
                }

                var ok = await _pluginLoadout.EquipAsync(_currentAgentId, id);
                ShowToast(
                    ok ? $"{plugin.DisplayName} 플러그인 연결됨" : $"{plugin.DisplayName} 연결 실패",
                    ok ? ToastTone.Success : ToastTone.Error);
            }
        }

        private static VisualElement BuildPermRow(PermissionSpec spec, bool isFirst)
        {
            var row = new VisualElement();
            row.AddToClassList("skill-market__perm-row");
            if (isFirst) row.AddToClassList("skill-market__perm-row--first");

            var icon = new VisualElement();
            icon.AddToClassList("skill-market__perm-icon");
            var iconChar = new Label(spec?.Glyph ?? "·");
            iconChar.AddToClassList("skill-market__perm-icon-char");
            icon.Add(iconChar);
            row.Add(icon);

            var body = new VisualElement();
            body.AddToClassList("skill-market__perm-body");
            row.Add(body);

            var head = new VisualElement();
            head.AddToClassList("skill-market__perm-row-head");
            body.Add(head);

            var lbl = new Label(spec?.Label ?? string.Empty);
            lbl.AddToClassList("skill-market__perm-label");
            head.Add(lbl);

            var riskWrap = new VisualElement();
            riskWrap.AddToClassList("skill-market__risk");
            head.Add(riskWrap);

            var dot = new VisualElement();
            dot.AddToClassList("skill-market__risk-dot");
            dot.AddToClassList($"skill-market__risk-dot--{spec?.Risk ?? "medium"}");
            riskWrap.Add(dot);

            // v4: 위험도 + 재확인 주기 (예: "보통 · 3시간 윈도우")
            var riskLbl = new Label(RiskDisplay(spec?.Risk));
            riskLbl.AddToClassList("skill-market__risk-label");
            riskWrap.Add(riskLbl);

            // v4: 비가역 액션은 위험도와 무관하게 항상 [X] 매번 확인 칩.
            // (사용자 instruction: NotoSansKR 미지원 글리프 금지 → ASCII 'X' 사용)
            if (spec?.Irreversible == true)
            {
                var lock_ = new Label("[X] 매번 확인");
                lock_.AddToClassList("skill-market__perm-lock");
                head.Add(lock_);
            }

            var det = new Label(spec?.Detail ?? string.Empty);
            det.AddToClassList("skill-market__perm-detail");
            body.Add(det);

            return row;
        }

        // v4: 위험도 라벨에 재확인 주기 텍스트를 함께 노출.
        // 낮음 = 설치 시 한 번 / 보통 = 3시간 윈도우 / 높음 = 매번 확인.
        private static string RiskDisplay(string risk) => risk switch
        {
            "low"    => "낮음 · 설치 시 한 번",
            "medium" => "보통 · 3시간 윈도우",
            "high"   => "높음 · 매번 확인",
            _        => "보통 · 3시간 윈도우",
        };

        internal static string MonogramFor(SkillDescriptor d)
        {
            var name = d?.DisplayName;
            if (string.IsNullOrWhiteSpace(name)) return "·";
            // 첫 단어의 첫 글자 (한글/영문 모두 지원)
            var ch = name.Trim()[0];
            return char.ToUpperInvariant(ch).ToString();
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

    // ══════════════════════════════════════════════════════
    //  Top tab (horizontal) — 추천 + 카테고리
    // ══════════════════════════════════════════════════════

    internal sealed class TopTabElement : VisualElement
    {
        private readonly Label _label;
        private readonly Label _count;
        private readonly VisualElement _underline;
        private readonly Action _onClick;
        private bool _selected;

        public TopTabElement(string title, string count, Action onClick)
        {
            AddToClassList("skill-market__tab");
            pickingMode = PickingMode.Position;

            _label = new Label(title);
            _label.AddToClassList("skill-market__tab-label");
            Add(_label);

            _count = new Label(count ?? string.Empty);
            _count.AddToClassList("skill-market__tab-count");
            if (string.IsNullOrEmpty(count)) _count.style.display = DisplayStyle.None;
            Add(_count);

            _underline = new VisualElement();
            _underline.AddToClassList("skill-market__tab-underline");
            Add(_underline);

            _onClick = onClick;
            RegisterCallback<ClickEvent>(_ => _onClick?.Invoke());
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;
            if (selected) AddToClassList("skill-market__tab--selected");
            else RemoveFromClassList("skill-market__tab--selected");
        }

        public void SetCount(string count)
        {
            if (_count == null) return;
            _count.text = count ?? string.Empty;
            _count.style.display = string.IsNullOrEmpty(count) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    // ══════════════════════════════════════════════════════
    //  Skill Card (compact, monogram tile)
    // ══════════════════════════════════════════════════════

    internal sealed class SkillCardElement : VisualElement
    {
        private SkillDescriptor _descriptor;
        private readonly Label _monoChar;
        private readonly Label _name;
        private readonly Label _author;
        private readonly Label _desc;
        private readonly Label _rating;
        private readonly Label _installs;
        private readonly Button _installBtn;
        private readonly Button _equipBtn;
        private readonly VisualElement _progress;
        private readonly VisualElement _progressFill;

        private bool _isEquipped;

        public Action<SkillDescriptor> InstallClicked;
        public Action<SkillDescriptor> UninstallClicked;
        public Action<SkillDescriptor> EquipClicked;
        public Action<SkillDescriptor> UnequipClicked;

        public string SkillId => _descriptor?.Id ?? string.Empty;

        public SkillCardElement(SkillDescriptor descriptor, int gridColumns)
        {
            AddToClassList("skill-card");
            pickingMode = PickingMode.Position;

            // Pixel-based basis + flex-grow lets the row decide how many cards fit
            // (UI Toolkit's flex-wrap on percent basis was unreliable inside a ScrollView).
            const float gap = 12f;
            var basisPx = gridColumns == 4 ? 260f : 320f;
            style.flexBasis = new StyleLength(new Length(basisPx, LengthUnit.Pixel));
            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minWidth = new StyleLength(new Length(220, LengthUnit.Pixel));
            style.marginRight = new StyleLength(new Length(gap, LengthUnit.Pixel));
            style.marginBottom = new StyleLength(new Length(gap, LengthUnit.Pixel));

            // Head: monogram + name/author
            var head = new VisualElement();
            head.AddToClassList("skill-card__head");
            Add(head);

            var mono = new VisualElement();
            mono.AddToClassList("skill-card__mono");
            head.Add(mono);

            _monoChar = new Label();
            _monoChar.AddToClassList("skill-card__mono-char");
            mono.Add(_monoChar);

            var headText = new VisualElement();
            headText.AddToClassList("skill-card__head-text");
            head.Add(headText);

            _name = new Label();
            _name.AddToClassList("skill-card__name");
            headText.Add(_name);

            _author = new Label();
            _author.AddToClassList("skill-card__author");
            headText.Add(_author);

            // Desc
            _desc = new Label();
            _desc.AddToClassList("skill-card__desc");
            Add(_desc);

            // Progress (hidden by default)
            _progress = new VisualElement();
            _progress.AddToClassList("skill-card__progress");
            Add(_progress);

            _progressFill = new VisualElement();
            _progressFill.AddToClassList("skill-card__progress-fill");
            _progress.Add(_progressFill);

            // Foot: metrics + install/equip buttons
            var foot = new VisualElement();
            foot.AddToClassList("skill-card__foot");
            Add(foot);

            var metrics = new VisualElement();
            metrics.AddToClassList("skill-card__metrics");
            foot.Add(metrics);

            _rating = new Label();
            _rating.AddToClassList("skill-card__metric");
            metrics.Add(_rating);

            var dot = new Label("·");
            dot.AddToClassList("skill-card__dot");
            metrics.Add(dot);

            _installs = new Label();
            _installs.AddToClassList("skill-card__metric");
            metrics.Add(_installs);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            foot.Add(actions);

            _installBtn = new Button(OnInstallButtonClicked) { text = "설치" };
            _installBtn.AddToClassList("skill-card__install");
            actions.Add(_installBtn);

            _equipBtn = new Button(OnEquipButtonClicked) { text = "장착" };
            _equipBtn.AddToClassList("skill-card__equip");
            actions.Add(_equipBtn);

            ApplyDescriptor(descriptor);
        }

        public void ApplyDescriptor(SkillDescriptor descriptor)
        {
            _descriptor = descriptor;
            if (descriptor == null) return;

            _monoChar.text = SkillMarketView.MonogramFor(descriptor);
            _name.text = descriptor.DisplayName ?? string.Empty;
            _author.text = string.IsNullOrEmpty(descriptor.Author) ? string.Empty : $"by {descriptor.Author}";
            _desc.text = descriptor.Description ?? string.Empty;
            _rating.text = descriptor.Rating > 0 ? $"★ {descriptor.Rating:F1}" : "★ —";
            _installs.text = descriptor.Downloads > 0 ? FormatCount(descriptor.Downloads) : "0";

            UpdateButtonStates();
        }

        public void SetInstalled(bool isInstalled, string installPath)
        {
            if (_descriptor == null) return;
            _descriptor = _descriptor.WithInstallState(isInstalled, installPath ?? string.Empty);
            UpdateButtonStates();
        }

        public void SetEquipped(bool isEquipped)
        {
            _isEquipped = isEquipped;
            UpdateButtonStates();
        }

        public void SetProgress(float value, bool visible)
        {
            if (visible) _progress.AddToClassList("skill-card__progress--visible");
            else _progress.RemoveFromClassList("skill-card__progress--visible");
            _progressFill.style.width = new StyleLength(new Length(Mathf.Clamp01(value) * 100f, LengthUnit.Percent));
        }

        private void OnInstallButtonClicked()
        {
            if (_descriptor == null) return;
            if (_descriptor.IsInstalled) UninstallClicked?.Invoke(_descriptor);
            else InstallClicked?.Invoke(_descriptor);
        }

        private void OnEquipButtonClicked()
        {
            if (_descriptor == null) return;
            if (_isEquipped) UnequipClicked?.Invoke(_descriptor);
            else EquipClicked?.Invoke(_descriptor);
        }

        private void UpdateButtonStates()
        {
            var isInstalled = _descriptor?.IsInstalled ?? false;

            if (isInstalled)
            {
                _installBtn.text = "설치됨";
                _installBtn.AddToClassList("skill-card__install--installed");
                _equipBtn.AddToClassList("skill-card__equip--visible");
                _equipBtn.text = _isEquipped ? "해제" : "장착";
                if (_isEquipped) _equipBtn.AddToClassList("skill-card__equip--equipped");
                else _equipBtn.RemoveFromClassList("skill-card__equip--equipped");
            }
            else
            {
                _installBtn.text = "설치";
                _installBtn.RemoveFromClassList("skill-card__install--installed");
                _equipBtn.RemoveFromClassList("skill-card__equip--visible");
                _equipBtn.RemoveFromClassList("skill-card__equip--equipped");
            }
        }

        private static string FormatCount(int n)
        {
            if (n >= 1000)
            {
                var k = n / 1000f;
                return (n % 1000 == 0) ? $"{(int)k}K" : $"{k:F1}K";
            }
            return n.ToString();
        }
    }

    // ══════════════════════════════════════════════════════
    //  Skill Row (left pane in category two-pane)
    // ══════════════════════════════════════════════════════

    internal sealed class SkillRowElement : VisualElement
    {
        private readonly Label _monoChar;
        private readonly Label _name;
        private readonly Label _desc;
        private readonly Label _rating;
        private readonly Label _installedBadge;
        private readonly Action _onClick;
        private bool _selected;
        private bool _isInstalled;

        public SkillRowElement(SkillDescriptor descriptor, Action onClick)
        {
            AddToClassList("skill-row");
            pickingMode = PickingMode.Position;
            _onClick = onClick;

            var mono = new VisualElement();
            mono.AddToClassList("skill-row__mono");
            Add(mono);

            _monoChar = new Label(SkillMarketView.MonogramFor(descriptor));
            _monoChar.AddToClassList("skill-row__mono-char");
            mono.Add(_monoChar);

            var text = new VisualElement();
            text.AddToClassList("skill-row__text");
            Add(text);

            _name = new Label(descriptor?.DisplayName ?? string.Empty);
            _name.AddToClassList("skill-row__name");
            text.Add(_name);

            _desc = new Label(descriptor?.Description ?? string.Empty);
            _desc.AddToClassList("skill-row__desc");
            text.Add(_desc);

            var right = new VisualElement();
            right.AddToClassList("skill-row__right");
            Add(right);

            _rating = new Label(descriptor?.Rating > 0 ? $"★ {descriptor.Rating:F1}" : "★ —");
            _rating.AddToClassList("skill-row__rating");
            right.Add(_rating);

            _installedBadge = new Label("설치됨");
            _installedBadge.AddToClassList("skill-row__installed");
            right.Add(_installedBadge);

            _isInstalled = descriptor?.IsInstalled ?? false;
            UpdateInstalledBadge();

            RegisterCallback<ClickEvent>(_ => _onClick?.Invoke());
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;
            if (selected) AddToClassList("skill-row--selected");
            else RemoveFromClassList("skill-row--selected");
        }

        public void SetEquipped(bool isEquipped)
        {
            // List row only shows installed badge; equipped state is reflected in the detail panel.
            _ = isEquipped;
        }

        public void SetInstalled(bool isInstalled)
        {
            _isInstalled = isInstalled;
            UpdateInstalledBadge();
        }

        private void UpdateInstalledBadge()
        {
            if (_isInstalled) _installedBadge.AddToClassList("skill-row__installed--visible");
            else _installedBadge.RemoveFromClassList("skill-row__installed--visible");
        }
    }

    // ══════════════════════════════════════════════════════
    //  Skill Detail (right pane in category two-pane)
    // ══════════════════════════════════════════════════════

    internal sealed class SkillDetailElement : VisualElement
    {
        private SkillDescriptor _descriptor;
        private readonly Label _monoChar;
        private readonly Label _title;
        private readonly Label _meta;
        private readonly Label _desc;
        private readonly Button _installBtn;
        private readonly Button _equipBtn;
        private readonly VisualElement _permissionsChips;
        private readonly Label _versionLine;
        // Install-progress inline (replaces install button while running)
        private readonly VisualElement _installProgress;
        private readonly VisualElement _progressSpinner;
        private readonly Label _progressPhase;
        private readonly Label _progressPercent;
        private readonly VisualElement _progressBarFill;
        private bool _isEquipped;

        public Action<SkillDescriptor> InstallClicked;
        public Action<SkillDescriptor> UninstallClicked;
        public Action<SkillDescriptor> EquipClicked;
        public Action<SkillDescriptor> UnequipClicked;

        public SkillDetailElement()
        {
            // Head
            var head = new VisualElement();
            head.AddToClassList("skill-detail__head");
            Add(head);

            var mono = new VisualElement();
            mono.AddToClassList("skill-detail__mono");
            head.Add(mono);

            _monoChar = new Label("·");
            _monoChar.AddToClassList("skill-detail__mono-char");
            mono.Add(_monoChar);

            var titleWrap = new VisualElement();
            titleWrap.AddToClassList("skill-detail__title-wrap");
            head.Add(titleWrap);

            _title = new Label();
            _title.AddToClassList("skill-detail__title");
            titleWrap.Add(_title);

            _meta = new Label();
            _meta.AddToClassList("skill-detail__meta");
            titleWrap.Add(_meta);

            var actions = new VisualElement();
            actions.AddToClassList("skill-detail__actions");
            head.Add(actions);

            _installBtn = new Button(OnInstallClicked) { text = "설치" };
            _installBtn.AddToClassList("skill-detail__install");
            actions.Add(_installBtn);

            _equipBtn = new Button(OnEquipClicked) { text = "장착" };
            _equipBtn.AddToClassList("skill-detail__equip");
            actions.Add(_equipBtn);

            // Inline install progress (replaces install button while running)
            _installProgress = new VisualElement();
            _installProgress.AddToClassList("skill-detail__install-progress");
            actions.Add(_installProgress);

            var progRow = new VisualElement();
            progRow.AddToClassList("skill-detail__progress-row");
            _installProgress.Add(progRow);

            _progressSpinner = new VisualElement();
            _progressSpinner.AddToClassList("skill-detail__progress-spinner");
            progRow.Add(_progressSpinner);

            _progressPhase = new Label("다운로드 중");
            _progressPhase.AddToClassList("skill-detail__progress-phase");
            progRow.Add(_progressPhase);

            _progressPercent = new Label("· 0%");
            _progressPercent.AddToClassList("skill-detail__progress-percent");
            progRow.Add(_progressPercent);

            var progBar = new VisualElement();
            progBar.AddToClassList("skill-detail__progress-bar");
            _installProgress.Add(progBar);

            _progressBarFill = new VisualElement();
            _progressBarFill.AddToClassList("skill-detail__progress-bar-fill");
            progBar.Add(_progressBarFill);

            // Long description
            _desc = new Label();
            _desc.AddToClassList("skill-detail__desc");
            Add(_desc);

            // Preview placeholder
            var preview = new VisualElement();
            preview.AddToClassList("skill-detail__preview");
            Add(preview);
            var previewLbl = new Label("스크린샷 · 동작 예시");
            previewLbl.AddToClassList("skill-detail__preview-label");
            preview.Add(previewLbl);

            // Section: 이 스킬은 무엇을 하나요
            var aboutSection = new VisualElement();
            aboutSection.AddToClassList("skill-detail__section");
            Add(aboutSection);
            var aboutTitle = new Label("이 스킬은 무엇을 하나요");
            aboutTitle.AddToClassList("skill-detail__section-title");
            aboutSection.Add(aboutTitle);
            var bullet1 = new Label("자연어 질의로 작업을 처리합니다.");
            bullet1.AddToClassList("skill-detail__bullet");
            aboutSection.Add(bullet1);
            var bullet2 = new Label("결과는 동료의 답장에 첨부됩니다.");
            bullet2.AddToClassList("skill-detail__bullet");
            aboutSection.Add(bullet2);

            // Section: 필요한 권한
            var permSection = new VisualElement();
            permSection.AddToClassList("skill-detail__section");
            Add(permSection);
            var permTitle = new Label("필요한 권한");
            permTitle.AddToClassList("skill-detail__section-title");
            permSection.Add(permTitle);
            _permissionsChips = new VisualElement();
            _permissionsChips.AddToClassList("skill-detail__chips");
            permSection.Add(_permissionsChips);

            // Section: 버전
            var versionSection = new VisualElement();
            versionSection.AddToClassList("skill-detail__section");
            Add(versionSection);
            var versionTitle = new Label("버전");
            versionTitle.AddToClassList("skill-detail__section-title");
            versionSection.Add(versionTitle);
            _versionLine = new Label();
            _versionLine.AddToClassList("skill-detail__bullet");
            versionSection.Add(_versionLine);
        }

        public void Bind(SkillDescriptor descriptor, bool isEquipped)
        {
            _descriptor = descriptor;
            _isEquipped = isEquipped;
            if (descriptor == null) return;

            _monoChar.text = SkillMarketView.MonogramFor(descriptor);
            _title.text = descriptor.DisplayName ?? string.Empty;

            var author = string.IsNullOrEmpty(descriptor.Author) ? "" : $"by {descriptor.Author}";
            var rating = descriptor.Rating > 0 ? $"★ {descriptor.Rating:F1}" : "★ —";
            var installs = descriptor.Downloads > 0 ? $"{descriptor.Downloads:N0}" : "0";
            _meta.text = string.Join(" · ", new[] { author, rating, installs }
                .Where(s => !string.IsNullOrEmpty(s)));

            _desc.text = string.IsNullOrEmpty(descriptor.Description)
                ? $"설치 후 채팅창에서 \"{descriptor.DisplayName}\"를 호출하거나, 동료에게 직접 요청할 수 있어요."
                : $"{descriptor.Description}. 설치 후 채팅창에서 \"{descriptor.DisplayName}\"를 호출하거나, 동료에게 직접 요청할 수 있어요.";

            // Permissions (chips)
            _permissionsChips.Clear();
            var tokens = descriptor.RequiredTokens;
            if (tokens != null && tokens.Count > 0)
            {
                foreach (var t in tokens)
                {
                    var chip = new Label(t);
                    chip.AddToClassList("skill-detail__chip");
                    _permissionsChips.Add(chip);
                }
            }
            else
            {
                var none = new Label("추가 권한 없음");
                none.AddToClassList("skill-detail__chip");
                _permissionsChips.Add(none);
            }

            var version = string.IsNullOrEmpty(descriptor.Version) ? "v0.0.0" : $"v{descriptor.Version}";
            var published = descriptor.PublishedAt == DateTime.MinValue
                ? "출시일 미정"
                : descriptor.PublishedAt.ToString("yyyy-MM-dd");
            _versionLine.text = $"{version}  ·  {published}";

            UpdateButtonStates();
        }

        public void SetInstalled(bool isInstalled, string installPath)
        {
            if (_descriptor == null) return;
            _descriptor = _descriptor.WithInstallState(isInstalled, installPath ?? string.Empty);
            UpdateButtonStates();
        }

        public void SetEquipped(bool isEquipped)
        {
            _isEquipped = isEquipped;
            UpdateButtonStates();
        }

        /// <summary>
        /// 인라인 진행바 표시. phase=null 이면 숨기고 설치 버튼 복귀.
        /// phase: "downloading" | "verifying" | "configuring" | "done"
        /// </summary>
        public void SetInstallProgress(string phase, float percent)
        {
            var visible = !string.IsNullOrEmpty(phase);
            if (!visible)
            {
                _installProgress.RemoveFromClassList("skill-detail__install-progress--visible");
                UpdateButtonStates();
                return;
            }

            _installProgress.AddToClassList("skill-detail__install-progress--visible");
            _installBtn.style.display = DisplayStyle.None;
            _equipBtn.style.display = DisplayStyle.None;

            var isDone = phase == "done";
            if (isDone)
            {
                _installProgress.AddToClassList("skill-detail__install-progress--done");
                _progressSpinner.AddToClassList("skill-detail__progress-spinner--done");
                _progressPhase.AddToClassList("skill-detail__progress-phase--done");
                _progressBarFill.AddToClassList("skill-detail__progress-bar-fill--done");
            }
            else
            {
                _installProgress.RemoveFromClassList("skill-detail__install-progress--done");
                _progressSpinner.RemoveFromClassList("skill-detail__progress-spinner--done");
                _progressPhase.RemoveFromClassList("skill-detail__progress-phase--done");
                _progressBarFill.RemoveFromClassList("skill-detail__progress-bar-fill--done");
            }

            _progressPhase.text = phase switch
            {
                "downloading" => "다운로드 중",
                "verifying"   => "서명 확인 중",
                "configuring" => "권한 설정 중",
                "done"        => "설치 완료",
                _             => "설치 중",
            };

            var pct = Mathf.RoundToInt(Mathf.Clamp01(percent) * 100f);
            _progressPercent.text = isDone ? string.Empty : $"· {pct}%";
            _progressBarFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
        }

        private void OnInstallClicked()
        {
            if (_descriptor == null) return;
            if (_descriptor.IsInstalled) UninstallClicked?.Invoke(_descriptor);
            else InstallClicked?.Invoke(_descriptor);
        }

        private void OnEquipClicked()
        {
            if (_descriptor == null) return;
            if (_isEquipped) UnequipClicked?.Invoke(_descriptor);
            else EquipClicked?.Invoke(_descriptor);
        }

        private void UpdateButtonStates()
        {
            var isInstalled = _descriptor?.IsInstalled ?? false;

            if (isInstalled)
            {
                _installBtn.text = "설치됨";
                _installBtn.AddToClassList("skill-detail__install--installed");
                _equipBtn.AddToClassList("skill-detail__equip--visible");
                _equipBtn.text = _isEquipped ? "해제" : "장착";
                if (_isEquipped) _equipBtn.AddToClassList("skill-detail__equip--equipped");
                else _equipBtn.RemoveFromClassList("skill-detail__equip--equipped");
            }
            else
            {
                _installBtn.text = "설치";
                _installBtn.RemoveFromClassList("skill-detail__install--installed");
                _equipBtn.RemoveFromClassList("skill-detail__equip--visible");
                _equipBtn.RemoveFromClassList("skill-detail__equip--equipped");
            }
        }
    }

    internal static class ProgressFactory
    {
        public static IProgress<T> Create<T>(Action<T> handler) => new Delegate<T>(handler);

        private sealed class Delegate<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public Delegate(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler?.Invoke(value);
        }
    }

    // ══════════════════════════════════════════════════════
    //  v4: Plugin card — 사각 둥근 타일 + 차가운 슬레이트 톤. SkillCardElement 와 시각 패밀리 분리.
    //  "스킬" 단어와 "플러그인" 단어가 절대 섞이지 않도록 카피와 톤 모두 분리.
    // ══════════════════════════════════════════════════════

    internal sealed class PluginCardElement : VisualElement
    {
        private PluginDescriptor _descriptor;
        private readonly Label _monoChar;
        private readonly Label _name;
        private readonly Label _vendor;
        private readonly Label _desc;
        private readonly Label _typeBadge;
        private readonly Button _installBtn;
        private readonly Button _equipBtn;

        private bool _isEquipped;

        public Action<PluginDescriptor> InstallClicked;
        public Action<PluginDescriptor> UninstallClicked;
        public Action<PluginDescriptor> EquipClicked;
        public Action<PluginDescriptor> UnequipClicked;

        public string PluginId => _descriptor?.Id ?? string.Empty;

        public PluginCardElement(PluginDescriptor descriptor)
        {
            AddToClassList("plugin-card");
            pickingMode = PickingMode.Position;

            // 그리드 안에서 적당히 늘어나도록 — SkillCardElement 와 동일 패턴.
            style.flexBasis = new StyleLength(new Length(260f, LengthUnit.Pixel));
            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minWidth = new StyleLength(new Length(220, LengthUnit.Pixel));
            style.marginRight = new StyleLength(new Length(12, LengthUnit.Pixel));
            style.marginBottom = new StyleLength(new Length(12, LengthUnit.Pixel));

            // Type badge — "플러그인"
            _typeBadge = new Label("플러그인");
            _typeBadge.AddToClassList("plugin-card__type-badge");
            Add(_typeBadge);

            var head = new VisualElement();
            head.AddToClassList("plugin-card__head");
            Add(head);

            var mono = new VisualElement();
            mono.AddToClassList("plugin-card__mono");
            head.Add(mono);

            _monoChar = new Label();
            _monoChar.AddToClassList("plugin-card__mono-char");
            mono.Add(_monoChar);

            var headText = new VisualElement();
            headText.AddToClassList("plugin-card__head-text");
            head.Add(headText);

            _name = new Label();
            _name.AddToClassList("plugin-card__name");
            headText.Add(_name);

            _vendor = new Label();
            _vendor.AddToClassList("plugin-card__vendor");
            headText.Add(_vendor);

            _desc = new Label();
            _desc.AddToClassList("plugin-card__desc");
            Add(_desc);

            var foot = new VisualElement();
            foot.AddToClassList("plugin-card__foot");
            Add(foot);

            _installBtn = new Button(OnInstallButtonClicked) { text = "연결" };
            _installBtn.AddToClassList("plugin-card__install");
            foot.Add(_installBtn);

            _equipBtn = new Button(OnEquipButtonClicked) { text = "장착" };
            _equipBtn.AddToClassList("plugin-card__equip");
            foot.Add(_equipBtn);

            ApplyDescriptor(descriptor);
        }

        public void ApplyDescriptor(PluginDescriptor descriptor)
        {
            _descriptor = descriptor;
            if (descriptor == null) return;

            _monoChar.text = MonogramFor(descriptor.DisplayName);
            _name.text = descriptor.DisplayName ?? descriptor.Id ?? string.Empty;
            _vendor.text = descriptor.Vendor != PluginVendor.Custom
                ? $"by {descriptor.Vendor}"
                : string.Empty;
            _desc.text = descriptor.Description ?? string.Empty;

            UpdateButtonStates();
        }

        public void SetEquipped(bool isEquipped)
        {
            _isEquipped = isEquipped;
            UpdateButtonStates();
        }

        private void OnInstallButtonClicked()
        {
            if (_descriptor == null) return;
            if (_descriptor.IsInstalled) UninstallClicked?.Invoke(_descriptor);
            else InstallClicked?.Invoke(_descriptor);
        }

        private void OnEquipButtonClicked()
        {
            if (_descriptor == null) return;
            if (_isEquipped) UnequipClicked?.Invoke(_descriptor);
            else EquipClicked?.Invoke(_descriptor);
        }

        private void UpdateButtonStates()
        {
            var isInstalled = _descriptor?.IsInstalled ?? false;

            if (isInstalled)
            {
                _installBtn.text = "연결됨";
                _installBtn.AddToClassList("plugin-card__install--installed");
                _equipBtn.AddToClassList("plugin-card__equip--visible");
                _equipBtn.text = _isEquipped ? "해제" : "장착";
                if (_isEquipped) _equipBtn.AddToClassList("plugin-card__equip--equipped");
                else _equipBtn.RemoveFromClassList("plugin-card__equip--equipped");
            }
            else
            {
                _installBtn.text = "연결";
                _installBtn.RemoveFromClassList("plugin-card__install--installed");
                _equipBtn.RemoveFromClassList("plugin-card__equip--visible");
                _equipBtn.RemoveFromClassList("plugin-card__equip--equipped");
            }
        }

        private static string MonogramFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "·";
            var ch = name.Trim()[0];
            return char.ToUpperInvariant(ch).ToString();
        }
    }

    // ══════════════════════════════════════════════════════
    //  Permission catalog — Claude Design "SM_PERMISSIONS" 의 OpenDesk 포팅
    //  스킬 카테고리별 권한 세트 + RequiredTokens 키워드 override.
    //  Glyph 는 NotoSansKR 가 안정 렌더하는 영문 1글자만 사용 (이모지/한글 X).
    // ══════════════════════════════════════════════════════

    internal sealed record PermissionSpec(
        string Id,
        string Label,
        string Detail,
        string Risk,            // "low" | "medium" | "high"
        string Glyph,           // 28×28 박스 안 영문 1글자
        bool Irreversible = false);  // 발송 · 삭제 · 결제 · 게시 등 — 위험도와 무관 항상 "매번 확인"

    internal static class PermissionCatalog
    {
        // v4: 3단계 위험도 = 낮음/보통/높음 + 비가역 액션 (mail-w / cal-w(삭제) / fs(쓰기) / exec / browser / db / pay-w 등)
        // 은 Irreversible=true 로 표시 — UI 에서 항상 🔒 "매번 확인" 칩이 붙는다.
        private static readonly Dictionary<string, PermissionSpec> Defs = new()
        {
            ["net"]    = new("net",    "네트워크 접근",      "외부 API 호출 및 응답 수신",          "medium", "N"),
            ["cache"]  = new("cache",  "로컬 캐시",           "반복 질의 속도 향상을 위해 7일간 저장", "low",    "L"),
            ["fs"]     = new("fs",     "파일 시스템 읽기/쓰기", "동의한 폴더 안에서만",                "high",   "F", Irreversible: true),
            ["meta"]   = new("meta",   "파일 메타데이터",      "수정 시각, 크기, 확장자",             "low",    "F"),
            ["mail-r"] = new("mail-r", "메일 읽기",            "받은편지함, 보낸편지함, 라벨",         "medium", "M"),
            ["mail-w"] = new("mail-w", "메일 발송",            "사용자가 보내기 누른 뒤에만 전송",      "high",   "M", Irreversible: true),
            ["cal-r"]  = new("cal-r",  "캘린더 조회",          "다음 14일 일정과 참석자 목록",         "low",    "C"),
            ["cal-w"]  = new("cal-w",  "캘린더 생성 · 삭제",   "새 일정 추가, 기존 일정 취소",         "high",   "C", Irreversible: true),
            ["oauth"]  = new("oauth",  "OAuth 자격 증명",      "토큰 발급 · 갱신",                    "medium", "O"),
            ["exec"]   = new("exec",   "코드 실행",            "샌드박스 내 Python / JS / Shell 실행", "high",   "X", Irreversible: true),
            ["browser"]= new("browser","브라우저 제어",        "웹페이지 자동화 · 데이터 추출",         "high",   "B", Irreversible: true),
            ["db"]     = new("db",     "데이터베이스 접근",     "쿼리 실행 및 결과 수집",               "high",   "D", Irreversible: true),
            ["trans"]  = new("trans",  "번역 서비스",          "외부 번역 API 호출",                  "low",    "T"),
            ["chat"]   = new("chat",   "메시지 보내기",        "동료를 대신해 발송",                  "high",   "S", Irreversible: true),
        };

        public static PermissionSpec Get(string id) =>
            id != null && Defs.TryGetValue(id, out var p) ? p : Defs["net"];

        public static IReadOnlyList<PermissionSpec> ResolveFor(SkillDescriptor skill)
        {
            if (skill == null) return Array.Empty<PermissionSpec>();

            // 1) 알려진 스킬 ID 가 있으면 정밀 매핑 우선
            var byId = ResolveByKnownId(skill.Id);
            if (byId != null) return byId;

            // 2) RequiredTokens 키워드 매칭 (있는 경우)
            var byTokens = ResolveFromTokens(skill.RequiredTokens);
            if (byTokens.Count > 0) return byTokens;

            // 3) 카테고리 기반 fallback
            return ResolveFromCategory(skill.Category);
        }

        private static IReadOnlyList<PermissionSpec> ResolveByKnownId(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            var k = skillId.ToLowerInvariant();
            return k switch
            {
                "web-search"     or "websearch"        => new[] { Defs["net"], Defs["cache"] },
                "file-mgr"       or "filemanager"      or "file-manager" => new[] { Defs["fs"], Defs["meta"] },
                "gmail"          or "google-mail"      => new[] { Defs["mail-r"], Defs["mail-w"], Defs["net"], Defs["oauth"] },
                "google-calendar" or "calendar"        => new[] { Defs["cal-r"], Defs["cal-w"], Defs["net"], Defs["oauth"] },
                "code-runner"    or "code-executor"    => new[] { Defs["exec"], Defs["fs"] },
                "browser-control" or "browser"         => new[] { Defs["browser"], Defs["net"] },
                "database-query" or "datalab"          => new[] { Defs["db"], Defs["net"] },
                "translator"                           => new[] { Defs["trans"], Defs["net"] },
                "slack"                                => new[] { Defs["chat"], Defs["net"], Defs["oauth"] },
                "notion"                               => new[] { Defs["net"], Defs["oauth"], Defs["cache"] },
                "linear"                               => new[] { Defs["net"], Defs["oauth"] },
                "figma"          or "figma-reader"     => new[] { Defs["net"], Defs["oauth"] },
                _                                       => null,
            };
        }

        private static IReadOnlyList<PermissionSpec> ResolveFromTokens(IReadOnlyList<string> tokens)
        {
            var result = new List<PermissionSpec>();
            if (tokens == null) return result;
            foreach (var t in tokens)
            {
                var key = (t ?? string.Empty).ToLowerInvariant().Trim();
                if (string.IsNullOrEmpty(key)) continue;
                if (Defs.TryGetValue(key, out var direct)) { Append(result, direct); continue; }
                if (key.Contains("mail") || key.Contains("gmail")) { Append(result, Defs["mail-r"]); Append(result, Defs["mail-w"]); }
                else if (key.Contains("calendar")) { Append(result, Defs["cal-r"]); Append(result, Defs["cal-w"]); }
                else if (key.Contains("file") || key.Contains("fs")) Append(result, Defs["fs"]);
                else if (key.Contains("network") || key.Contains("http") || key == "net") Append(result, Defs["net"]);
                else if (key.Contains("cache") || key.Contains("storage")) Append(result, Defs["cache"]);
                else if (key.Contains("oauth") || key.Contains("token")) Append(result, Defs["oauth"]);
                else if (key.Contains("exec") || key.Contains("shell")) Append(result, Defs["exec"]);
                else if (key.Contains("browser")) Append(result, Defs["browser"]);
                else if (key.Contains("db") || key.Contains("sql")) Append(result, Defs["db"]);
                else if (key.Contains("trans")) Append(result, Defs["trans"]);
                else if (key.Contains("slack") || key.Contains("chat")) Append(result, Defs["chat"]);
            }
            return result;
        }

        private static IReadOnlyList<PermissionSpec> ResolveFromCategory(SkillCategory cat)
        {
            return cat switch
            {
                SkillCategory.Email       => new[] { Defs["mail-r"], Defs["mail-w"], Defs["net"] },
                SkillCategory.Scheduling  => new[] { Defs["cal-r"],  Defs["cal-w"],  Defs["net"] },
                SkillCategory.Search      => new[] { Defs["net"],    Defs["cache"] },
                SkillCategory.Research    => new[] { Defs["net"],    Defs["cache"] },
                SkillCategory.Coding      => new[] { Defs["exec"],   Defs["fs"] },
                SkillCategory.Design      => new[] { Defs["net"],    Defs["oauth"] },
                SkillCategory.Analytics   => new[] { Defs["db"],     Defs["net"] },
                SkillCategory.Social      => new[] { Defs["chat"],   Defs["net"], Defs["oauth"] },
                SkillCategory.Support     => new[] { Defs["net"],    Defs["mail-r"] },
                SkillCategory.Translation => new[] { Defs["trans"],  Defs["net"] },
                SkillCategory.Document    => new[] { Defs["net"],    Defs["fs"] },
                SkillCategory.Analysis    => new[] { Defs["db"],     Defs["net"] },
                SkillCategory.ExternalTool => new[] { Defs["net"],   Defs["oauth"] },
                _                          => new[] { Defs["net"] },
            };
        }

        private static void Append(List<PermissionSpec> list, PermissionSpec p)
        {
            if (p == null) return;
            foreach (var existing in list)
                if (string.Equals(existing.Id, p.Id, StringComparison.Ordinal)) return;
            list.Add(p);
        }
    }
}
