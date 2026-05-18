using System;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.SkillMarket
{
    /// <summary>
    /// [Deprecated] uGUI 카테고리 탭. UI Toolkit 의 CategoryTabElement 로 대체됨.
    /// </summary>
    [System.Obsolete("uGUI 탭은 SkillMarketView 내부의 CategoryTabElement 로 대체되었습니다.")]
    public class CategoryTabButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private Image _stripe;
        [SerializeField] private Button _button;
        [SerializeField] private GameObject _selectedIndicator;

        private SkillCategory _category;
        private bool _isAll;
        private Action<SkillCategory, bool> _onClicked;

        public void BindCategory(SkillCategory category, Action<SkillCategory, bool> onClicked)
        {
            _category = category;
            _isAll = false;
            _onClicked = onClicked;

            if (_label != null) _label.SetText(category.DisplayName());
            if (_stripe != null) _stripe.color = category.DisplayColor();
            WireClick();
            SetSelected(false);
        }

        public void BindAll(Action<SkillCategory, bool> onClicked)
        {
            _isAll = true;
            _onClicked = onClicked;

            if (_label != null) _label.SetText("전체");
            if (_stripe != null) _stripe.color = new Color(0.5f, 0.5f, 0.5f);
            WireClick();
            SetSelected(true);
        }

        public void SetSelected(bool selected)
        {
            if (_selectedIndicator != null)
                _selectedIndicator.SetActive(selected);
        }

        private void WireClick()
        {
            if (_button == null) return;
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onClicked?.Invoke(_category, _isAll));
        }
    }
}
