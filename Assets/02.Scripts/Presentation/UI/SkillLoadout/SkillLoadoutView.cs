using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Characters.Wardrobe;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services.Skills;
using OpenDesk.Presentation.UI.SkillMarket;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.SkillLoadout
{
    /// <summary>
    /// 스킬 장착 모달 (UI Toolkit). "RPG 액션바" 메타포.
    ///
    /// 구성:
    ///   - 헤더: 에이전트 이름 + 로드아웃 프리셋(시각 placeholder) + 닫기
    ///   - 좌측: 캐릭터 프리뷰 + 장착 게이지 (n/6) + 6개 번호 슬롯
    ///   - 우측: 보유 스킬 검색 + 카테고리 칩 + 인벤토리 행 + 마켓 CTA
    ///   - 교체 모달: 6개가 모두 차 있을 때 장착 시도하면 slot 6 의 스킬을 교체할지 묻는다.
    ///
    /// 슬롯 인덱스 = AgentSkillLoadout.EquippedSkillIds 의 리스트 인덱스.
    /// 서비스는 슬롯 cap 을 강제하지 않으므로 cap 은 이 View 가 책임진다.
    /// 6개 초과 (외부 경로로 장착된 경우) 잔여분은 인벤토리에서 "장착됨" 라벨 + "-" 슬롯 번호로 표시.
    ///
    /// Inspector 작업:
    ///   - 같은 GameObject 의 UIDocument.Source Asset → SkillLoadoutView.uxml
    ///   - PanelSettings.sortingOrder 를 ChatPanel 위, SkillMarketView 아래로
    ///   - _previewBinder : 씬 preview rig 의 AgentLoadoutPreviewBinder 참조 (선택)
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillLoadoutView : MonoBehaviour
    {
        private const int SlotCount = 6;
        private const string CategoryAllKey = "__all__";

        // 인벤토리 카테고리 필터 옵션 (SkillMarketView 와 통일된 순서)
        private static readonly SkillCategory[] CategoryOrder =
        {
            SkillCategory.Scheduling, SkillCategory.Email, SkillCategory.Search,
            SkillCategory.Research, SkillCategory.Coding, SkillCategory.Design,
            SkillCategory.Analytics, SkillCategory.Social, SkillCategory.Support,
            SkillCategory.Translation, SkillCategory.Development, SkillCategory.Document,
            SkillCategory.Analysis, SkillCategory.ExternalTool,
        };

        // 디자인의 "기본 / 리서치 모드 / 글쓰기 모드" 프리셋은 현재 영속 모델이 없어 시각 placeholder.
        // 추후 LoadoutPresetService 도입 시 이 배열을 서비스 데이터로 교체.
        private static readonly (string id, string label)[] PresetMocks =
        {
            ("base", "기본"),
            ("research", "리서치 모드"),
            ("writing", "글쓰기 모드"),
        };

        [SerializeField] private AgentLoadoutPreviewBinder _previewBinder;

        private ISkillCatalogService _catalog;
        private IAgentSkillLoadoutService _loadout;
        private SkillMarketView _marketView;

        private UIDocument _document;
        private VisualElement _root;
        private Label _agentNameLabel;
        private Label _agentSubtitleLabel;
        private VisualElement _previewFrame;
        private VisualElement _presetBar;
        private VisualElement _slotList;
        private Label _slotEmptyHint;
        private Label _gaugeUsedLabel;
        private Label _gaugeTotalLabel;
        private VisualElement _gaugeFill;
        private TextField _searchField;
        private ScrollView _categoryBar;
        private ScrollView _inventoryScroll;
        private Label _inventoryCount;
        private Label _inventoryEmpty;
        private Button _closeButton;
        private Button _openMarketButton;

        // Replace modal
        private VisualElement _replaceModal;
        private VisualElement _replaceVeil;
        private Label _replaceSubtitle;
        private VisualElement _replaceOldIcon;
        private Label _replaceOldLetter;
        private Label _replaceOldTag;
        private Label _replaceOldName;
        private VisualElement _replaceNewIcon;
        private Label _replaceNewLetter;
        private Label _replaceNewName;
        private Button _replaceCancelBtn;
        private Button _replaceConfirmBtn;

        private string _currentAgentId;
        private AgentRole _currentRole = AgentRole.None;
        private string _searchQuery = string.Empty;
        private string _selectedCategoryKey = CategoryAllKey;
        private string _activePresetId = "base";
        private string _pendingNewSkillId;
        private int _pendingReplaceSlot = -1;
        private CancellationTokenSource _bindCts;

        private readonly List<SkillLoadoutSlotElement> _slots = new(SlotCount);
        private readonly Dictionary<string, SkillLoadoutCardElement> _rows = new();
        private readonly Dictionary<string, Button> _categoryChips = new();
        private readonly Dictionary<string, Button> _presetPills = new();
        private IDisposable _catalogSub;
        private IDisposable _loadoutSub;

        [Inject]
        public void Construct(
            ISkillCatalogService catalog,
            IAgentSkillLoadoutService loadout)
        {
            _catalog = catalog;
            _loadout = loadout;
        }

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
            HideReplaceModal();
        }

        private void OnDisable()
        {
            UnwireRoot();
            DisposeSubscriptions();
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;
        }

        // ══════════════════════════════════════════════════
        //  Public API
        // ══════════════════════════════════════════════════

        public void Open(string agentId, string agentDisplayName = null, AgentRole role = AgentRole.None)
        {
            _currentAgentId = agentId ?? string.Empty;
            _currentRole = role;

            if (_root == null) BuildView();
            SetVisible(true);
            HideReplaceModal();

            if (_agentNameLabel != null)
                _agentNameLabel.text = string.IsNullOrEmpty(agentDisplayName) ? "에이전트" : agentDisplayName;
            if (_agentSubtitleLabel != null)
            {
                var roleName = role != AgentRole.None ? RoleDisplayName(role) : null;
                _agentSubtitleLabel.text = string.IsNullOrEmpty(roleName) ? "스킬 장착" : $"{roleName} · 스킬 장착";
            }

            EnsureSubscriptions();
            Rebuild();
            BindPreviewAsync().Forget();
        }

        public void Close()
        {
            HideReplaceModal();
            SetVisible(false);
        }

        // ══════════════════════════════════════════════════
        //  View build
        // ══════════════════════════════════════════════════

        private void BuildView()
        {
            if (_document == null)
            {
                Debug.LogError("[SkillLoadoutView] UIDocument 컴포넌트 누락");
                return;
            }

            var rootEl = _document.rootVisualElement;
            if (rootEl == null)
            {
                Debug.LogError("[SkillLoadoutView] rootVisualElement null — UIDocument.Source Asset 에 SkillLoadoutView.uxml 을 연결하세요");
                return;
            }

            _root = rootEl.Q<VisualElement>("loadout-root");
            if (_root == null)
            {
                Debug.LogError("[SkillLoadoutView] UXML 트리에서 'loadout-root' 미발견");
                return;
            }

            _agentNameLabel       = rootEl.Q<Label>("agent-name");
            _agentSubtitleLabel   = rootEl.Q<Label>("agent-subtitle");
            _previewFrame         = rootEl.Q<VisualElement>("preview-frame");
            _presetBar            = rootEl.Q<VisualElement>("preset-bar");
            _slotList             = rootEl.Q<VisualElement>("slot-list");
            _slotEmptyHint        = rootEl.Q<Label>("loadout-empty-hint");
            _gaugeUsedLabel       = rootEl.Q<Label>("gauge-used");
            _gaugeTotalLabel      = rootEl.Q<Label>("gauge-total");
            _gaugeFill            = rootEl.Q<VisualElement>("gauge-fill");
            _searchField          = rootEl.Q<TextField>("search-field");
            _categoryBar          = rootEl.Q<ScrollView>("category-bar");
            _inventoryScroll      = rootEl.Q<ScrollView>("inventory-scroll");
            _inventoryCount       = rootEl.Q<Label>("inventory-count");
            _inventoryEmpty       = rootEl.Q<Label>("inventory-empty");
            _closeButton          = rootEl.Q<Button>("close-btn");
            _openMarketButton     = rootEl.Q<Button>("open-market-btn");

            _replaceModal         = rootEl.Q<VisualElement>("replace-modal");
            _replaceVeil          = rootEl.Q<VisualElement>("replace-veil");
            _replaceSubtitle      = rootEl.Q<Label>("replace-subtitle");
            _replaceOldIcon       = rootEl.Q<VisualElement>("replace-old-icon");
            _replaceOldLetter     = rootEl.Q<Label>("replace-old-letter");
            _replaceOldTag        = rootEl.Q<Label>("replace-old-tag");
            _replaceOldName       = rootEl.Q<Label>("replace-old-name");
            _replaceNewIcon       = rootEl.Q<VisualElement>("replace-new-icon");
            _replaceNewLetter     = rootEl.Q<Label>("replace-new-letter");
            _replaceNewName       = rootEl.Q<Label>("replace-new-name");
            _replaceCancelBtn     = rootEl.Q<Button>("replace-cancel-btn");
            _replaceConfirmBtn    = rootEl.Q<Button>("replace-confirm-btn");

            BuildSlots();
            BuildPresetPills();
            WireRoot();
        }

        private void BuildSlots()
        {
            if (_slotList == null) return;
            _slotList.Clear();
            _slots.Clear();

            for (int i = 0; i < SlotCount; i++)
            {
                var slot = new SkillLoadoutSlotElement(i);
                slot.UnequipRequested += () => OnSlotUnequip(slot);
                slot.Clicked += () => OnSlotClicked(slot);
                _slots.Add(slot);
                _slotList.Add(slot);
            }
        }

        private void BuildPresetPills()
        {
            if (_presetBar == null) return;
            _presetBar.Clear();
            _presetPills.Clear();

            foreach (var preset in PresetMocks)
            {
                var (id, label) = preset;
                var pill = new Button(() => SetActivePreset(id)) { text = label };
                pill.AddToClassList("loadout-preset-pill");
                if (string.Equals(id, _activePresetId, StringComparison.Ordinal))
                    pill.AddToClassList("loadout-preset-pill--active");
                _presetBar.Add(pill);
                _presetPills[id] = pill;
            }
        }

        private void SetActivePreset(string id)
        {
            _activePresetId = id ?? "base";
            foreach (var kv in _presetPills)
                kv.Value.EnableInClassList("loadout-preset-pill--active",
                    string.Equals(kv.Key, _activePresetId, StringComparison.Ordinal));
            // 영속 모델이 도입되면 여기서 LoadoutPresetService.ApplyAsync(id) 호출.
        }

        private void WireRoot()
        {
            if (_closeButton != null) _closeButton.clicked += OnCloseClicked;
            if (_openMarketButton != null) _openMarketButton.clicked += OnOpenMarketClicked;
            if (_searchField != null) _searchField.RegisterValueChangedCallback(OnSearchChanged);
            if (_replaceVeil != null) _replaceVeil.RegisterCallback<ClickEvent>(OnReplaceVeilClicked);
            if (_replaceCancelBtn != null) _replaceCancelBtn.clicked += OnReplaceCancelClicked;
            if (_replaceConfirmBtn != null) _replaceConfirmBtn.clicked += OnReplaceConfirmClicked;
        }

        private void UnwireRoot()
        {
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_openMarketButton != null) _openMarketButton.clicked -= OnOpenMarketClicked;
            if (_searchField != null) _searchField.UnregisterValueChangedCallback(OnSearchChanged);
            if (_replaceVeil != null) _replaceVeil.UnregisterCallback<ClickEvent>(OnReplaceVeilClicked);
            if (_replaceCancelBtn != null) _replaceCancelBtn.clicked -= OnReplaceCancelClicked;
            if (_replaceConfirmBtn != null) _replaceConfirmBtn.clicked -= OnReplaceConfirmClicked;
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnCloseClicked() => Close();

        private void OnOpenMarketClicked()
        {
            // SkillMarketView 는 씬에 UIDocument 가 배치된 경우만 존재 — DI 가 아닌 lazy lookup.
            if (_marketView == null)
                _marketView = FindFirstObjectByType<SkillMarketView>(FindObjectsInactive.Include);

            if (_marketView == null)
            {
                Debug.LogWarning("[SkillLoadoutView] SkillMarketView 미발견 — 씬에 UIDocument + SkillMarketView 를 배치하세요.");
                return;
            }

            _marketView.Open(_currentAgentId, _currentRole);
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            var next = (evt.newValue ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(next, _searchQuery, StringComparison.Ordinal)) return;
            _searchQuery = next;
            Rebuild();
        }

        // ══════════════════════════════════════════════════
        //  Subscriptions
        // ══════════════════════════════════════════════════

        private void EnsureSubscriptions()
        {
            if (_catalog != null && _catalogSub == null)
                _catalogSub = _catalog.OnCatalogChanged.Subscribe(_ => Rebuild());

            if (_loadout != null && _loadoutSub == null)
                _loadoutSub = _loadout.OnLoadoutChanged.Subscribe(OnLoadoutChanged);
        }

        private void DisposeSubscriptions()
        {
            _catalogSub?.Dispose();
            _catalogSub = null;
            _loadoutSub?.Dispose();
            _loadoutSub = null;
        }

        private void OnLoadoutChanged(AgentSkillLoadout changed)
        {
            if (changed == null) return;
            if (!string.Equals(changed.AgentId, _currentAgentId, StringComparison.Ordinal)) return;
            Rebuild();
        }

        // ══════════════════════════════════════════════════
        //  Rebuild — slots + inventory + gauge + categories
        // ══════════════════════════════════════════════════

        private void Rebuild()
        {
            var installed = GetInstalledSkills();
            var equippedIds = _loadout?.GetLoadout(_currentAgentId)?.EquippedSkillIds
                              ?? Array.Empty<string>();

            BuildCategoryChips(installed);
            UpdateSlots(equippedIds);
            UpdateGauge(equippedIds.Count);
            UpdateInventoryRows(installed, equippedIds);
        }

        private IReadOnlyList<SkillDescriptor> GetInstalledSkills()
        {
            var all = _catalog?.GetAll();
            if (all == null) return Array.Empty<SkillDescriptor>();
            return all.Where(d => d != null && d.IsInstalled).ToList();
        }

        private void UpdateSlots(IReadOnlyList<string> equippedIds)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var id = i < equippedIds.Count ? equippedIds[i] : null;
                var descriptor = !string.IsNullOrEmpty(id) ? FindDescriptor(id) : null;
                slot.SetSkill(descriptor);
                slot.SetDropTarget(false);
            }

            if (_slotEmptyHint != null)
                _slotEmptyHint.style.display = equippedIds.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateGauge(int equippedCount)
        {
            var visibleCount = Mathf.Min(equippedCount, SlotCount);
            if (_gaugeUsedLabel != null) _gaugeUsedLabel.text = visibleCount.ToString();
            if (_gaugeTotalLabel != null) _gaugeTotalLabel.text = $" / {SlotCount}";

            if (_gaugeFill != null)
            {
                var ratio = SlotCount == 0 ? 0f : Mathf.Clamp01(visibleCount / (float)SlotCount);
                _gaugeFill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
                _gaugeFill.EnableInClassList("loadout-gauge__fill--full", visibleCount >= SlotCount);
            }
        }

        private void UpdateInventoryRows(
            IReadOnlyList<SkillDescriptor> installed,
            IReadOnlyList<string> equippedIds)
        {
            if (_inventoryScroll == null) return;

            // 위치 매핑 (id → slot index). 6 초과분은 -1 처리하지 않고 그대로 노출 (인덱스 그대로 표시).
            var equippedAt = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < equippedIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(equippedIds[i])) equippedAt[equippedIds[i]] = i;
            }

            var filtered = installed
                .Where(MatchesFilters)
                .OrderBy(d => d.DisplayName ?? d.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _inventoryScroll.Clear();
            _rows.Clear();

            foreach (var descriptor in filtered)
            {
                var row = new SkillLoadoutCardElement(descriptor);
                var capturedId = descriptor.Id;
                row.EquipRequested   += () => OnRowEquip(capturedId);
                row.UnequipRequested += () => OnRowUnequip(capturedId);
                row.SetEquipped(equippedAt.TryGetValue(descriptor.Id, out var idx) ? idx : -1);
                _inventoryScroll.Add(row);
                _rows[descriptor.Id] = row;
            }

            if (_inventoryCount != null)
                _inventoryCount.text = installed.Count == 0
                    ? string.Empty
                    : $"{filtered.Count}개 · 설치한 것만 보임";

            if (_inventoryEmpty != null)
                _inventoryEmpty.style.display = installed.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool MatchesFilters(SkillDescriptor descriptor)
        {
            if (descriptor == null) return false;

            if (!string.Equals(_selectedCategoryKey, CategoryAllKey, StringComparison.Ordinal))
            {
                if (descriptor.Category.ToSerializedKey() != _selectedCategoryKey) return false;
            }

            if (string.IsNullOrEmpty(_searchQuery)) return true;
            var q = _searchQuery;
            return (descriptor.DisplayName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                   (descriptor.Description ?? string.Empty).ToLowerInvariant().Contains(q);
        }

        // ══════════════════════════════════════════════════
        //  Category chips
        // ══════════════════════════════════════════════════

        private void BuildCategoryChips(IReadOnlyList<SkillDescriptor> installed)
        {
            if (_categoryBar == null) return;

            // 설치된 스킬이 보유한 카테고리만 칩으로 노출 (+ 항상 "전체" 포함).
            var present = new HashSet<SkillCategory>(installed.Select(d => d.Category));
            var categoriesToShow = new List<(string key, string label)>
            {
                (CategoryAllKey, "전체")
            };
            foreach (var c in CategoryOrder)
            {
                if (present.Contains(c)) categoriesToShow.Add((c.ToSerializedKey(), c.DisplayName()));
            }

            _categoryBar.Clear();
            _categoryChips.Clear();

            // 활성 카테고리가 더 이상 존재하지 않으면 "전체"로 리셋.
            var stillValid = categoriesToShow.Any(t =>
                string.Equals(t.key, _selectedCategoryKey, StringComparison.Ordinal));
            if (!stillValid) _selectedCategoryKey = CategoryAllKey;

            foreach (var (key, label) in categoriesToShow)
            {
                var capturedKey = key;
                var chip = new Button(() => OnCategoryChipClicked(capturedKey)) { text = label };
                chip.AddToClassList("loadout-cat-chip");
                if (string.Equals(key, _selectedCategoryKey, StringComparison.Ordinal))
                    chip.AddToClassList("loadout-cat-chip--active");
                _categoryBar.Add(chip);
                _categoryChips[key] = chip;
            }

            _categoryBar.style.display = installed.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnCategoryChipClicked(string key)
        {
            if (string.Equals(key, _selectedCategoryKey, StringComparison.Ordinal)) return;
            _selectedCategoryKey = key ?? CategoryAllKey;

            foreach (var kv in _categoryChips)
                kv.Value.EnableInClassList("loadout-cat-chip--active",
                    string.Equals(kv.Key, _selectedCategoryKey, StringComparison.Ordinal));

            Rebuild();
        }

        // ══════════════════════════════════════════════════
        //  Slot / Row interactions
        // ══════════════════════════════════════════════════

        private void OnSlotUnequip(SkillLoadoutSlotElement slot)
        {
            if (slot == null || string.IsNullOrEmpty(_currentAgentId) || _loadout == null) return;
            var skillId = slot.SkillId;
            if (string.IsNullOrEmpty(skillId)) return;
            _loadout.UnequipAsync(_currentAgentId, skillId).Forget();
            // 결과는 OnLoadoutChanged → Rebuild() 로 반영.
        }

        private void OnSlotClicked(SkillLoadoutSlotElement slot)
        {
            // 빈 슬롯을 클릭하면 마켓을 연다 (보유 스킬이 있다면 인벤토리에서 선택 유도).
            if (slot == null || !slot.IsEmpty) return;

            var installed = GetInstalledSkills();
            if (installed.Count == 0) OnOpenMarketClicked();
            // 보유 스킬이 있는 경우 별도 동작 없음 — 사용자가 우측 인벤토리에서 선택하도록.
        }

        private void OnRowEquip(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || _loadout == null || string.IsNullOrEmpty(_currentAgentId)) return;

            var equippedIds = _loadout.GetLoadout(_currentAgentId)?.EquippedSkillIds
                              ?? Array.Empty<string>();

            if (equippedIds.Count >= SlotCount)
            {
                // 슬롯이 가득 찼다 — 마지막 슬롯(인덱스 5)을 교체 대상으로 모달 표시.
                ShowReplaceModal(skillId, replaceSlot: SlotCount - 1, equippedIds);
                return;
            }

            _loadout.EquipAsync(_currentAgentId, skillId).Forget();
        }

        private void OnRowUnequip(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || _loadout == null || string.IsNullOrEmpty(_currentAgentId)) return;
            _loadout.UnequipAsync(_currentAgentId, skillId).Forget();
        }

        // ══════════════════════════════════════════════════
        //  Replace modal
        // ══════════════════════════════════════════════════

        private void ShowReplaceModal(string newSkillId, int replaceSlot, IReadOnlyList<string> equippedIds)
        {
            if (_replaceModal == null) return;
            if (replaceSlot < 0 || replaceSlot >= equippedIds.Count) return;

            var newSkill = FindDescriptor(newSkillId);
            var oldSkill = FindDescriptor(equippedIds[replaceSlot]);
            if (newSkill == null || oldSkill == null) return;

            _pendingNewSkillId = newSkillId;
            _pendingReplaceSlot = replaceSlot;

            if (_replaceSubtitle != null)
                _replaceSubtitle.text = $"슬롯 {replaceSlot + 1}의 스킬을 새 스킬로 교체할까요?";

            if (_replaceOldTag != null)   _replaceOldTag.text = $"현재 슬롯 {replaceSlot + 1}";
            if (_replaceOldName != null)  _replaceOldName.text = oldSkill.DisplayName ?? oldSkill.Id;
            if (_replaceOldLetter != null) _replaceOldLetter.text = GetIconLetter(oldSkill.DisplayName);
            if (_replaceOldIcon != null)  _replaceOldIcon.style.backgroundColor = oldSkill.Category.DisplayColor();

            if (_replaceNewName != null)  _replaceNewName.text = newSkill.DisplayName ?? newSkill.Id;
            if (_replaceNewLetter != null) _replaceNewLetter.text = GetIconLetter(newSkill.DisplayName);
            if (_replaceNewIcon != null)  _replaceNewIcon.style.backgroundColor = newSkill.Category.DisplayColor();

            _replaceModal.AddToClassList("loadout-replace--visible");
        }

        private void HideReplaceModal()
        {
            _pendingNewSkillId = null;
            _pendingReplaceSlot = -1;
            _replaceModal?.RemoveFromClassList("loadout-replace--visible");
        }

        private void OnReplaceVeilClicked(ClickEvent _) => HideReplaceModal();
        private void OnReplaceCancelClicked() => HideReplaceModal();

        private void OnReplaceConfirmClicked()
        {
            if (string.IsNullOrEmpty(_pendingNewSkillId) || _pendingReplaceSlot < 0 || _loadout == null) return;

            var newSkillId = _pendingNewSkillId;
            var slot = _pendingReplaceSlot;
            HideReplaceModal();
            RunReplaceAsync(newSkillId, slot).Forget();
        }

        private async UniTaskVoid RunReplaceAsync(string newSkillId, int slot)
        {
            if (_loadout == null || string.IsNullOrEmpty(_currentAgentId)) return;

            var equippedIds = _loadout.GetLoadout(_currentAgentId)?.EquippedSkillIds
                              ?? Array.Empty<string>();
            if (slot < 0 || slot >= equippedIds.Count) return;

            var oldId = equippedIds[slot];
            if (string.IsNullOrEmpty(oldId)) return;

            // 서비스에 atomic swap 이 없으므로 unequip → equip 두 단계로 처리.
            // 두 번째 단계 실패 시 첫 단계 결과만 반영되어 슬롯이 비게 된다 (드물지만 가능).
            var unequipped = await _loadout.UnequipAsync(_currentAgentId, oldId);
            if (!unequipped) return;
            await _loadout.EquipAsync(_currentAgentId, newSkillId);
        }

        // ══════════════════════════════════════════════════
        //  Preview
        // ══════════════════════════════════════════════════

        private async UniTask BindPreviewAsync()
        {
            if (_previewBinder == null) return;

            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = new CancellationTokenSource();

            try
            {
                _previewBinder.AttachToFrame(_previewFrame);
                await _previewBinder.BindAsync(_currentAgentId, _bindCts.Token);
            }
            catch (OperationCanceledException) { /* 모달 닫힘 — 정상 */ }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillLoadoutView] preview bind 실패: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════

        private SkillDescriptor FindDescriptor(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            return _catalog?.GetById(skillId);
        }

        private static string GetIconLetter(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return "?";
            foreach (var c in displayName)
            {
                if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
            }
            return displayName.Substring(0, 1);
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
            _                     => string.Empty,
        };
    }
}
