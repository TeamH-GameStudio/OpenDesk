using System;
using System.Collections.Generic;
using OpenDesk.Core.Models.Skills;
using OpenDesk.SkillDiskette;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI
{
    /// <summary>
    /// 에이전트 장착 스킬 슬롯 UI.
    /// 슬롯 무제한 — 장착된 스킬만 동적 리스트로 표시. 빈 슬롯 placeholder 없음.
    /// 헤더에 "스킬 추가" 버튼이 있으면 OnAddRequested 이벤트 발행 → 마켓 패널 오픈.
    /// </summary>
    public class EquipmentSlotUI : MonoBehaviour
    {
        [Header("UI 참조")]
        [SerializeField] private Transform _slotContainer;
        [SerializeField] private GameObject _slotItemPrefab;
        [SerializeField] private TextMeshProUGUI _headerText;
        [SerializeField] private Button _addSkillButton;

        // slotItemPrefab 구조:
        //   Image (스킬 카테고리 색상) + Text (스킬 이름) + Button (해제)

        private AgentEquipmentManager _equipment;
        private DisketteShelfUI _shelfUI;
        private readonly List<GameObject> _slotItems = new();

        /// <summary>"스킬 추가" 버튼 클릭 시 발행. SkillMarketPanelController 가 구독.</summary>
        public event Action OnAddRequested;

        public void Bind(AgentEquipmentManager equipment, DisketteShelfUI shelfUI = null)
        {
            _equipment = equipment;
            _shelfUI = shelfUI;

            equipment.OnEquipmentChanged
                .Subscribe(_ => RefreshSlots())
                .AddTo(this);

            // 레거시 호환: 해제된 디스켓을 선반으로 복귀
            if (_shelfUI != null)
            {
                equipment.OnUnequippedDisk
                    .Subscribe(disk => { if (disk != null) _shelfUI.ReturnDiskette(disk); })
                    .AddTo(this);
            }

            if (_addSkillButton != null)
            {
                _addSkillButton.onClick.RemoveAllListeners();
                _addSkillButton.onClick.AddListener(() => OnAddRequested?.Invoke());
            }

            RefreshSlots();
        }

        public void Unbind()
        {
            _equipment = null;
            ClearSlotItems();
        }

        private void RefreshSlots()
        {
            ClearSlotItems();
            if (_equipment == null) return;

            var equipped = _equipment.EquippedDescriptors;
            var count = equipped?.Count ?? 0;

            if (_headerText != null)
                _headerText.SetText($"장착 스킬 ({count}개)");

            if (equipped == null) return;
            foreach (var descriptor in equipped)
            {
                var item = CreateSlotItem(descriptor);
                if (item != null) _slotItems.Add(item);
            }
        }

        private GameObject CreateSlotItem(SkillDescriptor descriptor)
        {
            if (_slotItemPrefab == null || _slotContainer == null || descriptor == null) return null;

            var item = Instantiate(_slotItemPrefab, _slotContainer);
            item.SetActive(true);

            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.SetText(descriptor.DisplayName);

            var img = item.GetComponentInChildren<Image>();
            if (img != null)
                img.color = descriptor.Category.DisplayColor();

            var button = item.GetComponentInChildren<Button>();
            if (button != null)
            {
                var capturedId = descriptor.Id;
                button.onClick.AddListener(() =>
                {
                    _equipment?.TryUnequip(capturedId);
                });
            }

            return item;
        }

        private void ClearSlotItems()
        {
            foreach (var item in _slotItems)
            {
                if (item != null) Destroy(item);
            }
            _slotItems.Clear();
        }
    }
}
