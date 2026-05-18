using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services.Skills;
using OpenDesk.SkillDiskette;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.SkillMarket
{
    /// <summary>
    /// [Deprecated] uGUI 기반 스킬 마켓 패널. UI Toolkit 버전(<see cref="SkillMarketView"/>)으로 대체됨.
    /// 코드 보존을 위해 남겨두지만 신규 씬은 SkillMarketView 를 사용한다.
    /// </summary>
    [System.Obsolete("uGUI 버전은 SkillMarketView (UI Toolkit) 로 대체되었습니다. 새 씬은 SkillMarketView 를 사용하세요.")]
    public class SkillMarketPanelController : MonoBehaviour
    {
        [Header("패널 컨테이너")]
        [SerializeField] private GameObject _root;            // 전체 패널 (활성/비활성 토글)
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _refreshButton;
        [SerializeField] private TMP_InputField _searchInput;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("카테고리 탭")]
        [SerializeField] private Transform _categoryTabContainer;
        [SerializeField] private GameObject _categoryTabPrefab;

        [Header("추천 섹션")]
        [SerializeField] private GameObject _recommendedSection;
        [SerializeField] private TextMeshProUGUI _recommendedHeader;
        [SerializeField] private Transform _recommendedContainer;

        [Header("카탈로그 그리드")]
        [SerializeField] private Transform _catalogContainer;
        [SerializeField] private GameObject _cardPrefab;

        [Inject] private ISkillCatalogService _catalog;
        [Inject] private ISkillInstallerService _installer;
        [Inject] private IAgentSkillLoadoutService _loadout;
        [Inject] private ISkillRecommendationService _recommendations;

        private string _currentAgentId;
        private AgentRole _currentRole = AgentRole.None;
        private SkillCategory _selectedCategory;
        private bool _categoryFilterActive;
        private string _searchQuery = string.Empty;

        private readonly Dictionary<string, SkillMarketCard> _cardsById = new();
        private readonly List<GameObject> _recommendedCards = new();
        private readonly List<CategoryTabButton> _tabs = new();
        private CategoryTabButton _allTab;
        private IDisposable _catalogSubscription;
        private IDisposable _installSubscription;
        private IDisposable _loadoutSubscription;
        private CancellationTokenSource _refreshCts;

        private static readonly SkillCategory[] CategoryOrder = new[]
        {
            SkillCategory.Scheduling, SkillCategory.Email, SkillCategory.Search,
            SkillCategory.Research, SkillCategory.Coding, SkillCategory.Design,
            SkillCategory.Analytics, SkillCategory.Social, SkillCategory.Support,
            SkillCategory.Translation,
        };

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);

            if (_closeButton != null)
                _closeButton.onClick.AddListener(Close);

            if (_refreshButton != null)
                _refreshButton.onClick.AddListener(() => RefreshCatalogAsync(forceRefresh: true).Forget());

            if (_searchInput != null)
                _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private void OnDestroy()
        {
            _catalogSubscription?.Dispose();
            _installSubscription?.Dispose();
            _loadoutSubscription?.Dispose();
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
        }

        // ══════════════════════════════════════════════
        //  오픈/클로즈
        // ══════════════════════════════════════════════

        public void Open(string agentId, AgentRole role)
        {
            _currentAgentId = agentId ?? string.Empty;
            _currentRole = role;

            if (_root != null) _root.SetActive(true);

            EnsureSubscriptions();
            BuildCategoryTabs();
            RefreshCatalogAsync(forceRefresh: false).Forget();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
        }

        // ══════════════════════════════════════════════
        //  카탈로그 새로고침
        // ══════════════════════════════════════════════

        private async UniTaskVoid RefreshCatalogAsync(bool forceRefresh)
        {
            if (_catalog == null) return;

            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            SetStatus("카탈로그 불러오는 중...");
            try
            {
                await _catalog.RefreshAsync(forceRefresh, _refreshCts.Token);
                SetStatus(string.Empty);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillMarketPanel] 새로고침 실패: {ex.Message}");
                SetStatus("새로고침 실패. 캐시 사용 중.");
            }

            RenderAll();
        }

        // ══════════════════════════════════════════════
        //  렌더링
        // ══════════════════════════════════════════════

        private void BuildCategoryTabs()
        {
            if (_categoryTabContainer == null || _categoryTabPrefab == null) return;

            foreach (Transform child in _categoryTabContainer)
                Destroy(child.gameObject);
            _tabs.Clear();
            _allTab = null;

            _allTab = InstantiateTab();
            _allTab.BindAll(OnCategorySelected);

            foreach (var category in CategoryOrder)
            {
                var tab = InstantiateTab();
                tab.BindCategory(category, OnCategorySelected);
                _tabs.Add(tab);
            }

            _categoryFilterActive = false;
            _selectedCategory = SkillCategory.General;
        }

        private CategoryTabButton InstantiateTab()
        {
            var go = Instantiate(_categoryTabPrefab, _categoryTabContainer);
            go.SetActive(true);
            return go.GetComponent<CategoryTabButton>();
        }

        private void OnCategorySelected(SkillCategory category, bool isAll)
        {
            _categoryFilterActive = !isAll;
            _selectedCategory = category;

            if (_allTab != null) _allTab.SetSelected(isAll);
            foreach (var tab in _tabs)
                tab.SetSelected(!isAll && /* CategoryTabButton 내부 비교 불가 → 부모가 일괄 적용 */ false);

            // 선택 시각화는 단순화: 활성 탭만 indicator on
            if (!isAll)
            {
                for (int i = 0; i < _tabs.Count && i < CategoryOrder.Length; i++)
                    _tabs[i].SetSelected(CategoryOrder[i] == category);
            }

            RenderCatalogGrid();
        }

        private void OnSearchChanged(string text)
        {
            _searchQuery = (text ?? string.Empty).Trim().ToLowerInvariant();
            RenderCatalogGrid();
        }

        private void RenderAll()
        {
            RenderRecommended();
            RenderCatalogGrid();
        }

        private void RenderRecommended()
        {
            ClearList(_recommendedCards);
            if (_recommendedSection == null || _recommendations == null || _currentRole == AgentRole.None)
            {
                if (_recommendedSection != null) _recommendedSection.SetActive(false);
                return;
            }

            _recommendedSection.SetActive(true);
            if (_recommendedHeader != null)
                _recommendedHeader.SetText($"{RoleDisplayName(_currentRole)} 역할 추천");

            var recommended = _recommendations.RecommendForRole(_currentRole, limit: 6);
            foreach (var descriptor in recommended)
            {
                var card = SpawnCard(descriptor, _recommendedContainer);
                if (card != null)
                    _recommendedCards.Add(card.gameObject);
            }
        }

        private void RenderCatalogGrid()
        {
            if (_catalog == null || _catalogContainer == null) return;

            // 기존 카드 모두 비활성화
            foreach (var kvp in _cardsById)
            {
                if (kvp.Value != null) kvp.Value.gameObject.SetActive(false);
            }

            var all = _catalog.GetAll();
            var filtered = ApplyFilters(all);

            foreach (var descriptor in filtered)
            {
                var card = GetOrCreateCard(descriptor, _catalogContainer);
                if (card != null)
                {
                    card.gameObject.SetActive(true);
                    card.UpdateInstalledState(descriptor.IsInstalled, IsEquipped(descriptor.Id), descriptor.InstallPath);
                }
            }
        }

        private IEnumerable<SkillDescriptor> ApplyFilters(IEnumerable<SkillDescriptor> all)
        {
            IEnumerable<SkillDescriptor> result = all;
            if (_categoryFilterActive)
                result = result.Where(d => d.Category == _selectedCategory);
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                var q = _searchQuery;
                result = result.Where(d =>
                    (d.DisplayName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (d.Description ?? string.Empty).ToLowerInvariant().Contains(q));
            }
            return result.OrderByDescending(d => d.IsInstalled)
                         .ThenByDescending(d => d.Downloads)
                         .ThenBy(d => d.DisplayName);
        }

        // ══════════════════════════════════════════════
        //  카드 핸들링
        // ══════════════════════════════════════════════

        private SkillMarketCard GetOrCreateCard(SkillDescriptor descriptor, Transform parent)
        {
            if (descriptor == null) return null;
            if (_cardsById.TryGetValue(descriptor.Id, out var existing) && existing != null)
            {
                if (existing.transform.parent != parent)
                    existing.transform.SetParent(parent, worldPositionStays: false);
                return existing;
            }

            var card = SpawnCard(descriptor, parent);
            if (card != null)
                _cardsById[descriptor.Id] = card;
            return card;
        }

        private SkillMarketCard SpawnCard(SkillDescriptor descriptor, Transform parent)
        {
            if (_cardPrefab == null || parent == null) return null;
            var go = Instantiate(_cardPrefab, parent);
            go.SetActive(true);
            var card = go.GetComponent<SkillMarketCard>();
            if (card == null) return null;

            card.Bind(
                descriptor,
                isEquipped: IsEquipped(descriptor.Id),
                onInstall: OnInstallClicked,
                onUninstall: OnUninstallClicked,
                onEquip: OnEquipClicked,
                onUnequip: OnUnequipClicked);
            return card;
        }

        private bool IsEquipped(string skillId)
        {
            if (_loadout == null || string.IsNullOrEmpty(_currentAgentId)) return false;
            var current = _loadout.GetLoadout(_currentAgentId);
            return current?.EquippedSkillIds?.Contains(skillId) ?? false;
        }

        private async void OnInstallClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null || _installer == null) return;
            var card = FindCard(descriptor.Id);
            try
            {
                var progress = card != null
                    ? Progress.Create<float>(v => card.SetProgress(v, visible: true))
                    : null;
                var ok = await _installer.InstallAsync(descriptor.Id, progress, destroyCancellationToken);
                if (card != null) card.SetProgress(0, visible: false);
                SetStatus(ok ? $"{descriptor.DisplayName} 설치 완료" : $"{descriptor.DisplayName} 설치 실패");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillMarketPanel] 설치 오류: {ex.Message}");
                if (card != null) card.SetProgress(0, visible: false);
                SetStatus("설치 중 오류 발생");
            }
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
                SetStatus("에이전트가 선택되지 않았습니다");
                return;
            }
            await _loadout.EquipAsync(_currentAgentId, descriptor.Id);
        }

        private async void OnUnequipClicked(SkillDescriptor descriptor)
        {
            if (descriptor == null || _loadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            await _loadout.UnequipAsync(_currentAgentId, descriptor.Id);
        }

        // ══════════════════════════════════════════════
        //  구독
        // ══════════════════════════════════════════════

        private void EnsureSubscriptions()
        {
            _catalogSubscription ??= _catalog?.OnCatalogChanged.Subscribe(_ => RenderAll());

            if (_installer != null)
            {
                _installSubscription ??= _installer.OnInstallChanged.Subscribe(evt =>
                {
                    if (_cardsById.TryGetValue(evt.SkillId, out var card) && card != null)
                        card.UpdateInstalledState(evt.IsInstalled, IsEquipped(evt.SkillId), evt.InstallPath);
                });
            }

            if (_loadout != null)
            {
                _loadoutSubscription ??= _loadout.OnLoadoutChanged.Subscribe(loadout =>
                {
                    if (loadout == null) return;
                    if (!string.Equals(loadout.AgentId, _currentAgentId, StringComparison.Ordinal)) return;

                    foreach (var kvp in _cardsById)
                    {
                        if (kvp.Value == null) continue;
                        var equipped = loadout.EquippedSkillIds?.Contains(kvp.Key) ?? false;
                        kvp.Value.UpdateEquippedState(equipped);
                    }
                });
            }
        }

        // ══════════════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════════════

        private SkillMarketCard FindCard(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            return _cardsById.TryGetValue(skillId, out var card) ? card : null;
        }

        private void ClearList(List<GameObject> list)
        {
            foreach (var go in list)
                if (go != null) Destroy(go);
            list.Clear();
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.SetText(text ?? string.Empty);
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

    internal static class Progress
    {
        public static IProgress<T> Create<T>(Action<T> handler)
        {
            return new ProgressDelegate<T>(handler);
        }

        private sealed class ProgressDelegate<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public ProgressDelegate(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler?.Invoke(value);
        }
    }
}
