using System;
using OpenDesk.Core.Models.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.SkillMarket
{
    /// <summary>
    /// [Deprecated] uGUI 카드 컴포넌트. UI Toolkit 의 SkillCardElement 로 대체됨.
    /// </summary>
    [System.Obsolete("uGUI 카드는 SkillMarketView 내부의 SkillCardElement 로 대체되었습니다.")]
    public class SkillMarketCard : MonoBehaviour
    {
        [Header("UI 참조")]
        [SerializeField] private TextMeshProUGUI _displayName;
        [SerializeField] private TextMeshProUGUI _description;
        [SerializeField] private TextMeshProUGUI _author;
        [SerializeField] private TextMeshProUGUI _categoryLabel;
        [SerializeField] private TextMeshProUGUI _stats;             // "★ 4.5 · 12,345"
        [SerializeField] private Image _categoryStripe;              // 카테고리 색상
        [SerializeField] private Image _iconImage;
        [SerializeField] private Button _installButton;
        [SerializeField] private TextMeshProUGUI _installButtonLabel;
        [SerializeField] private Button _uninstallButton;
        [SerializeField] private Button _equipButton;
        [SerializeField] private TextMeshProUGUI _equipButtonLabel;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private GameObject _equippedBadge;

        private SkillDescriptor _descriptor;
        private Action<SkillDescriptor> _onInstallClicked;
        private Action<SkillDescriptor> _onUninstallClicked;
        private Action<SkillDescriptor> _onEquipClicked;
        private Action<SkillDescriptor> _onUnequipClicked;

        public string SkillId => _descriptor?.Id ?? string.Empty;

        public void Bind(
            SkillDescriptor descriptor,
            bool isEquipped,
            Action<SkillDescriptor> onInstall,
            Action<SkillDescriptor> onUninstall,
            Action<SkillDescriptor> onEquip,
            Action<SkillDescriptor> onUnequip)
        {
            _descriptor = descriptor;
            _onInstallClicked = onInstall;
            _onUninstallClicked = onUninstall;
            _onEquipClicked = onEquip;
            _onUnequipClicked = onUnequip;

            if (descriptor == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            UpdateDisplay(isEquipped);
            WireButtons();
            SetProgress(0f, visible: false);
        }

        public void UpdateInstalledState(bool isInstalled, bool isEquipped, string installPath)
        {
            if (_descriptor == null) return;
            _descriptor = _descriptor.WithInstallState(isInstalled, installPath ?? string.Empty);
            UpdateDisplay(isEquipped);
        }

        public void UpdateEquippedState(bool isEquipped)
        {
            UpdateDisplay(isEquipped);
        }

        public void SetProgress(float value, bool visible)
        {
            if (_progressBar == null) return;
            _progressBar.gameObject.SetActive(visible);
            _progressBar.value = Mathf.Clamp01(value);
        }

        // ── 내부 ──

        private void UpdateDisplay(bool isEquipped)
        {
            if (_descriptor == null) return;

            if (_displayName != null) _displayName.SetText(_descriptor.DisplayName);
            if (_description != null) _description.SetText(_descriptor.Description);
            if (_author != null) _author.SetText(string.IsNullOrEmpty(_descriptor.Author) ? "" : $"by {_descriptor.Author}");
            if (_categoryLabel != null) _categoryLabel.SetText(_descriptor.Category.DisplayName());
            if (_categoryStripe != null) _categoryStripe.color = _descriptor.Category.DisplayColor();
            if (_stats != null)
            {
                var rating = _descriptor.Rating > 0 ? $"★ {_descriptor.Rating:F1}" : "";
                var downloads = _descriptor.Downloads > 0 ? $"⬇ {_descriptor.Downloads:N0}" : "";
                _stats.SetText(string.Join(" · ", new[] { rating, downloads }).Trim(' ', '·'));
            }

            var isInstalled = _descriptor.IsInstalled;

            if (_installButton != null)
                _installButton.gameObject.SetActive(!isInstalled);
            if (_uninstallButton != null)
                _uninstallButton.gameObject.SetActive(isInstalled && !isEquipped);
            if (_equipButton != null)
                _equipButton.gameObject.SetActive(isInstalled);

            if (_installButtonLabel != null)
                _installButtonLabel.SetText("설치");
            if (_equipButtonLabel != null)
                _equipButtonLabel.SetText(isEquipped ? "해제" : "장착");
            if (_equippedBadge != null)
                _equippedBadge.SetActive(isEquipped);
        }

        private void WireButtons()
        {
            if (_installButton != null)
            {
                _installButton.onClick.RemoveAllListeners();
                _installButton.onClick.AddListener(() => _onInstallClicked?.Invoke(_descriptor));
            }
            if (_uninstallButton != null)
            {
                _uninstallButton.onClick.RemoveAllListeners();
                _uninstallButton.onClick.AddListener(() => _onUninstallClicked?.Invoke(_descriptor));
            }
            if (_equipButton != null)
            {
                _equipButton.onClick.RemoveAllListeners();
                _equipButton.onClick.AddListener(() =>
                {
                    if (_descriptor == null) return;
                    if (_equippedBadge != null && _equippedBadge.activeSelf)
                        _onUnequipClicked?.Invoke(_descriptor);
                    else
                        _onEquipClicked?.Invoke(_descriptor);
                });
            }
        }
    }
}
