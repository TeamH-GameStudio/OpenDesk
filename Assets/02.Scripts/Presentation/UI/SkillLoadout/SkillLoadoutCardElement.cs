using System;
using OpenDesk.Core.Models.Skills;
using UnityEngine.UIElements;

namespace OpenDesk.Presentation.UI.SkillLoadout
{
    /// <summary>
    /// 우측 인벤토리의 보유 스킬 행. RPG 액션바 디자인의 인벤토리 row 한 줄.
    ///
    /// 구성: 아이콘(카테고리 색칩) + 이름 + "카테고리 · 설명" 한 줄 +
    ///       장착 상태 표시(장착됨 + 슬롯 번호 핫키 / 또는 "장착" 버튼).
    ///
    /// 슬롯이 가득 찼을 때 장착 요청은 외부(SkillLoadoutView)가 가로채서
    /// 교체 확인 모달로 라우팅한다. 이 element 는 이벤트만 발행.
    /// </summary>
    internal sealed class SkillLoadoutCardElement : VisualElement
    {
        private readonly VisualElement _icon;
        private readonly Label _iconLetter;
        private readonly Label _title;
        private readonly Label _desc;
        private readonly Button _equipBtn;
        private readonly VisualElement _equippedPill;
        private readonly Label _equippedSlotNum;
        private readonly Label _equippedText;

        private SkillDescriptor _descriptor;
        private int _equippedSlot = -1;

        public event Action EquipRequested;
        public event Action UnequipRequested;

        public string SkillId => _descriptor?.Id ?? string.Empty;
        public bool IsEquipped => _equippedSlot >= 0;

        public SkillLoadoutCardElement(SkillDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            _descriptor = descriptor;

            AddToClassList("loadout-row");
            pickingMode = PickingMode.Position;

            _icon = new VisualElement();
            _icon.AddToClassList("loadout-row__icon");
            _icon.style.backgroundColor = descriptor.Category.DisplayColor();
            _iconLetter = new Label(GetIconLetter(descriptor.DisplayName));
            _iconLetter.AddToClassList("loadout-row__icon-letter");
            _icon.Add(_iconLetter);
            Add(_icon);

            var body = new VisualElement();
            body.AddToClassList("loadout-row__body");
            Add(body);

            var titleLine = new VisualElement();
            titleLine.AddToClassList("loadout-row__title-line");
            body.Add(titleLine);

            _title = new Label(string.IsNullOrEmpty(descriptor.DisplayName) ? descriptor.Id : descriptor.DisplayName);
            _title.AddToClassList("loadout-row__title");
            _title.AddToClassList("od-body-md");
            titleLine.Add(_title);

            var category = descriptor.Category.DisplayName();
            var desc = descriptor.Description ?? string.Empty;
            _desc = new Label(string.IsNullOrEmpty(desc) ? category : $"{category} · {desc}");
            _desc.AddToClassList("loadout-row__desc");
            _desc.AddToClassList("od-caption");
            body.Add(_desc);

            // 우측 액션 — 장착됨 pill OR 장착 버튼
            _equippedPill = new VisualElement();
            _equippedPill.AddToClassList("loadout-row__equipped");
            _equippedPill.RegisterCallback<ClickEvent>(_ => UnequipRequested?.Invoke());
            _equippedSlotNum = new Label();
            _equippedSlotNum.AddToClassList("loadout-row__equipped-num");
            _equippedPill.Add(_equippedSlotNum);
            _equippedText = new Label("장착됨");
            _equippedPill.Add(_equippedText);
            Add(_equippedPill);

            _equipBtn = new Button(() => EquipRequested?.Invoke()) { text = "장착" };
            _equipBtn.AddToClassList("loadout-row__equip-btn");
            Add(_equipBtn);

            SetEquipped(-1);
        }

        /// <summary>장착 상태와 슬롯 인덱스를 반영. -1 이면 미장착.</summary>
        public void SetEquipped(int slotIndex)
        {
            _equippedSlot = slotIndex;
            if (slotIndex >= 0)
            {
                _equippedPill.style.display = DisplayStyle.Flex;
                _equipBtn.style.display = DisplayStyle.None;
                _equippedSlotNum.text = (slotIndex + 1).ToString();
            }
            else
            {
                _equippedPill.style.display = DisplayStyle.None;
                _equipBtn.style.display = DisplayStyle.Flex;
            }
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
    }
}
