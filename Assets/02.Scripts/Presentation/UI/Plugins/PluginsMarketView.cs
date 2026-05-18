using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Services.Plugins;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Plugins
{
    /// <summary>
    /// (Deprecated v4) 플러그인 마켓 — SkillMarketView 가 스킬+플러그인 통합 뷰를 담당하므로 더 이상 사용하지 않는다.
    /// 가역 보존 (project 의 "legacy deprecation preference" 정책) — 파일/UXML/USS 모두 남기고,
    /// AgentOfficeInstaller 의 DI 등록만 코멘트 처리.
    /// 새 코드에서 이 클래스를 참조하지 말 것. 플러그인 install/equip 흐름은 SkillMarketView.OnPlugin* 핸들러를 사용.
    /// </summary>
    [Obsolete("PluginsMarketView 는 SkillMarketView 의 v4 통합 마켓으로 대체되었다. AgentOfficeInstaller 의 DI 등록도 비활성. 가역 보존 — 새 코드 작성 시 참조 금지.")]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PluginsMarketView : MonoBehaviour
    {
        // ── DI ─────────────────────────────────────────────
        private IPluginCatalogService _catalog;
        private IPluginCredentialService _credentials;
        private IAgentPluginLoadoutService _loadout;
        private PluginCredentialModal _credentialModal;

        [Inject]
        public void Construct(
            IPluginCatalogService catalog,
            IPluginCredentialService credentials,
            IAgentPluginLoadoutService loadout,
            PluginCredentialModal credentialModal = null)
        {
            _catalog = catalog;
            _credentials = credentials;
            _loadout = loadout;
            _credentialModal = credentialModal;
        }

        // ── UI refs ───────────────────────────────────────
        private UIDocument _document;
        private VisualElement _root;
        private TextField _searchField;
        private ScrollView _vendorRail;
        private ScrollView _catalogScroll;
        private Label _statusLabel;
        private Button _closeButton;
        private Button _refreshButton;

        private string _currentAgentId;
        private PluginVendor? _selectedVendor;
        private string _searchQuery = string.Empty;
        private readonly CompositeDisposable _disposables = new();

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement?.Q<VisualElement>("plugins-market");
            if (_root == null)
            {
                Debug.LogError("[PluginsMarketView] UXML 루트 'plugins-market' 를 찾지 못함");
                return;
            }

            _searchField = _root.Q<TextField>("plugins-market-search");
            _vendorRail = _root.Q<ScrollView>("plugins-market-vendor-rail");
            _catalogScroll = _root.Q<ScrollView>("plugins-market-catalog");
            _statusLabel = _root.Q<Label>("plugins-market-status");
            _closeButton = _root.Q<Button>("plugins-market-close");
            _refreshButton = _root.Q<Button>("plugins-market-refresh");

            if (_closeButton != null) _closeButton.clicked += Close;
            if (_refreshButton != null) _refreshButton.clicked += () => Refresh().Forget();
            if (_searchField != null)
                _searchField.RegisterValueChangedCallback(e =>
                {
                    _searchQuery = e.newValue ?? string.Empty;
                    Render();
                });

            if (_catalog != null)
            {
                _catalog.OnCatalogChanged.Subscribe(_ => Render()).AddTo(_disposables);
            }
            if (_loadout != null)
            {
                _loadout.OnLoadoutChanged.Subscribe(_ => Render()).AddTo(_disposables);
            }

            Hide();
        }

        private void OnDisable()
        {
            _disposables.Clear();
            if (_closeButton != null) _closeButton.clicked -= Close;
        }

        public void Open(string agentId)
        {
            _currentAgentId = agentId ?? string.Empty;
            _root.style.display = DisplayStyle.Flex;
            Refresh().Forget();
        }

        public void Close()
        {
            Hide();
        }

        private void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }

        private async UniTask Refresh()
        {
            SetStatus("로드 중...");
            try
            {
                await _catalog.RefreshAsync(forceRefresh: true, destroyCancellationToken);
                Render();
                SetStatus(string.Empty);
            }
            catch (Exception ex)
            {
                SetStatus($"오류: {ex.Message}");
            }
        }

        private void Render()
        {
            if (_catalog == null || _catalogScroll == null) return;

            RenderVendorRail();

            _catalogScroll.Clear();
            var all = _catalog.GetAll();
            var filtered = ApplyFilters(all);

            if (filtered.Count == 0)
            {
                var empty = new Label("표시할 플러그인이 없습니다.") { name = "plugins-empty" };
                empty.AddToClassList("od-caption");
                _catalogScroll.Add(empty);
                return;
            }

            foreach (var descriptor in filtered)
            {
                _catalogScroll.Add(BuildCard(descriptor));
            }
        }

        private void RenderVendorRail()
        {
            if (_vendorRail == null) return;
            _vendorRail.Clear();

            void AddTab(string label, PluginVendor? vendor)
            {
                var btn = new Button(() =>
                {
                    _selectedVendor = vendor;
                    Render();
                })
                { text = label };
                btn.AddToClassList("plugins-market__tab");
                if (_selectedVendor == vendor) btn.AddToClassList("plugins-market__tab--active");
                _vendorRail.Add(btn);
            }

            AddTab("전체", null);
            foreach (PluginVendor v in Enum.GetValues(typeof(PluginVendor)))
            {
                AddTab(v.DisplayName(), v);
            }
        }

        private List<PluginDescriptor> ApplyFilters(IReadOnlyList<PluginDescriptor> source)
        {
            IEnumerable<PluginDescriptor> seq = source;
            if (_selectedVendor.HasValue)
                seq = seq.Where(d => d.Vendor == _selectedVendor.Value);
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim();
                seq = seq.Where(d =>
                    (d.DisplayName != null && d.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (d.Description != null && d.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            return seq.ToList();
        }

        private VisualElement BuildCard(PluginDescriptor descriptor)
        {
            var card = new VisualElement();
            card.AddToClassList("plugin-card");

            var icon = new VisualElement();
            icon.AddToClassList("plugin-card__icon");
            icon.style.backgroundColor = descriptor.Vendor.DisplayColor();
            card.Add(icon);

            var info = new VisualElement();
            info.AddToClassList("plugin-card__info");

            var nameLabel = new Label(descriptor.DisplayName) { name = "name" };
            nameLabel.AddToClassList("plugin-card__name");
            nameLabel.AddToClassList("od-heading-sm");
            info.Add(nameLabel);

            var vendorLabel = new Label(descriptor.Vendor.DisplayName()) { name = "vendor" };
            vendorLabel.AddToClassList("plugin-card__vendor");
            vendorLabel.AddToClassList("od-caption");
            info.Add(vendorLabel);

            card.Add(info);

            var equipped = IsEquipped(descriptor.Id);
            var action = new Button(() => HandleAction(descriptor, equipped).Forget())
            {
                text = equipped ? "해제" : "장착",
            };
            action.AddToClassList("plugin-card__action");
            card.Add(action);

            return card;
        }

        private bool IsEquipped(string pluginId)
        {
            if (_loadout == null || string.IsNullOrEmpty(_currentAgentId)) return false;
            var loadout = _loadout.GetLoadout(_currentAgentId);
            return loadout != null && loadout.EquippedPluginIds.Contains(pluginId);
        }

        private async UniTask HandleAction(PluginDescriptor descriptor, bool currentlyEquipped)
        {
            if (string.IsNullOrEmpty(_currentAgentId))
            {
                SetStatus("에이전트를 먼저 선택해주세요.");
                return;
            }

            if (currentlyEquipped)
            {
                await _loadout.UnequipAsync(_currentAgentId, descriptor.Id);
                SetStatus($"{descriptor.DisplayName} 해제됨");
                return;
            }

            // 자격증명 확인 → 누락 시 모달 호출
            if (_credentials != null && !await _credentials.HasAllRequiredAsync(descriptor))
            {
                if (_credentialModal != null)
                {
                    var saved = await _credentialModal.AskAsync(descriptor);
                    if (!saved)
                    {
                        SetStatus("자격증명 입력이 취소되었습니다.");
                        return;
                    }
                }
                else
                {
                    SetStatus("자격증명이 필요합니다. 자격증명 모달이 등록되지 않았습니다.");
                    return;
                }
            }

            await _loadout.EquipAsync(_currentAgentId, descriptor.Id);
            SetStatus($"{descriptor.DisplayName} 장착됨");
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? string.Empty;
        }
    }
}
