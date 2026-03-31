using OpenDesk.SkillDiskette;
using R3;
using R3.Triggers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI
{
    /// <summary>
    /// 에이전트 장착 디스켓 슬롯 UI.
    /// AgentEquipmentManager의 상태를 실시간 표시.
    /// </summary>
    public class EquipmentSlotUI : MonoBehaviour
    {
        [Header("UI 참조")]
        [SerializeField] private Transform _slotContainer;
        [SerializeField] private GameObject _slotItemPrefab;
        [SerializeField] private TextMeshProUGUI _headerText;

        // slotItemPrefab 구조:
        //   Image (디스켓 색상) + Text (디스켓 이름) + Button (해제)

        private AgentEquipmentManager _equipment;
        private DisketteShelfUI _shelfUI;
        private readonly System.Collections.Generic.List<GameObject> _slotItems = new();

        /// <summary>장착 관리자 바인딩</summary>
        public void Bind(AgentEquipmentManager equipment, DisketteShelfUI shelfUI = null)
        {
            _equipment = equipment;
            _shelfUI = shelfUI;

            equipment.OnEquipmentChanged
                .Subscribe(_ => RefreshSlots())
                .AddTo(this);

            // 해제 시 선반에 복귀
            if (_shelfUI != null)
            {
                equipment.OnUnequipped
                    .Subscribe(disk => _shelfUI.ReturnDiskette(disk))
                    .AddTo(this);
            }

            RefreshSlots();
        }

        /// <summary>장착 관리자 해제</summary>
        public void Unbind()
        {
            _equipment = null;
            ClearSlotItems();
        }

        private void RefreshSlots()
        {
            ClearSlotItems();
            if (_equipment == null) return;

            // 헤더
            if (_headerText != null)
                _headerText.SetText($"장착 스킬 ({_equipment.EquippedDisks.Count}/{_equipment.MaxSlots})");

            // 장착된 디스켓 표시
            foreach (var disk in _equipment.EquippedDisks)
            {
                var item = CreateSlotItem(disk.DisplayName, disk.Color, disk.SkillId);
                _slotItems.Add(item);
            }

            // 빈 슬롯 표시
            for (int i = 0; i < _equipment.RemainingSlots; i++)
            {
                var item = CreateEmptySlotItem();
                _slotItems.Add(item);
            }
        }

        private GameObject CreateSlotItem(string name, Color color, string skillId)
        {
            if (_slotItemPrefab == null || _slotContainer == null) return null;

            var item = Instantiate(_slotItemPrefab, _slotContainer);
            item.SetActive(true);

            // 이름 텍스트
            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.SetText(name);

            // 색상 이미지
            var img = item.GetComponentInChildren<Image>();
            if (img != null)
                img.color = color;

            // 해제 버튼
            var button = item.GetComponentInChildren<Button>();
            if (button != null)
            {
                var capturedId = skillId;
                button.onClick.AddListener(() =>
                {
                    _equipment?.TryUnequip(capturedId);
                });
            }

            return item;
        }

        private GameObject CreateEmptySlotItem()
        {
            if (_slotItemPrefab == null || _slotContainer == null) return null;

            var item = Instantiate(_slotItemPrefab, _slotContainer);
            item.SetActive(true);

            var label = item.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.SetText("(빈 슬롯)");

            var img = item.GetComponentInChildren<Image>();
            if (img != null)
                img.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);

            var button = item.GetComponentInChildren<Button>();
            if (button != null)
                button.gameObject.SetActive(false);

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
