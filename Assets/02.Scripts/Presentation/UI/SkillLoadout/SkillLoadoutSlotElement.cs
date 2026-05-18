using System;
using OpenDesk.Core.Models.Skills;
using UnityEngine.UIElements;

namespace OpenDesk.Presentation.UI.SkillLoadout
{
    /// <summary>
    /// 좌측 장착 패널의 번호가 매겨진 RPG 핫바 슬롯 한 칸.
    ///
    /// 두 가지 상태:
    ///   - 비어있음 : 점선 느낌 보더 + "비어있는 슬롯" 안내 + + 아이콘
    ///   - 채워짐 : 카테고리 색 아이콘 + 스킬명 + 카테고리/설명 한 줄 + hover 시 "해제" 인라인 버튼
    ///
    /// 외부에서는 Clicked / UnequipRequested 이벤트만 구독한다.
    /// 슬롯 번호 = SlotIndex + 1 표시 (0-based → 1-based UI).
    /// </summary>
    internal sealed class SkillLoadoutSlotElement : VisualElement
    {
        private readonly Label _numberBadge;
        private readonly VisualElement _icon;
        private readonly Label _iconLetter;
        private readonly Label _title;
        private readonly Label _sub;
        private readonly Button _unequipBtn;
        private readonly Label _plusGlyph;

        private SkillDescriptor _descriptor;

        public int SlotIndex { get; }
        public string SkillId => _descriptor?.Id;
        public bool IsEmpty => _descriptor == null;

        public event Action Clicked;
        public event Action UnequipRequested;

        public SkillLoadoutSlotElement(int slotIndex)
        {
            SlotIndex = slotIndex;
            AddToClassList("loadout-slot");
            focusable = true;
            pickingMode = PickingMode.Position;

            _numberBadge = new Label((slotIndex + 1).ToString());
            _numberBadge.AddToClassList("loadout-slot__num");
            Add(_numberBadge);

            _icon = new VisualElement();
            _icon.AddToClassList("loadout-slot__icon");
            _iconLetter = new Label();
            _iconLetter.AddToClassList("loadout-slot__icon-letter");
            _icon.Add(_iconLetter);
            Add(_icon);

            var body = new VisualElement();
            body.AddToClassList("loadout-slot__body");
            Add(body);

            _title = new Label();
            _title.AddToClassList("loadout-slot__title");
            _title.AddToClassList("od-body-md");
            body.Add(_title);

            _sub = new Label();
            _sub.AddToClassList("loadout-slot__sub");
            _sub.AddToClassList("od-caption");
            body.Add(_sub);

            _unequipBtn = new Button(() => UnequipRequested?.Invoke()) { text = "해제" };
            _unequipBtn.AddToClassList("loadout-slot__unequip");
            Add(_unequipBtn);

            _plusGlyph = new Label("+");
            _plusGlyph.AddToClassList("loadout-slot__plus");
            Add(_plusGlyph);

            RegisterCallback<ClickEvent>(OnRootClicked);

            ApplyEmpty();
        }

        public void SetSkill(SkillDescriptor descriptor)
        {
            _descriptor = descriptor;
            if (descriptor == null)
            {
                ApplyEmpty();
                return;
            }

            RemoveFromClassList("loadout-slot--empty");
            _numberBadge.RemoveFromClassList("loadout-slot__num--empty");

            _icon.style.display = DisplayStyle.Flex;
            _icon.style.backgroundColor = descriptor.Category.DisplayColor();
            _iconLetter.text = GetIconLetter(descriptor.DisplayName);

            _title.text = string.IsNullOrEmpty(descriptor.DisplayName) ? descriptor.Id : descriptor.DisplayName;
            _title.RemoveFromClassList("loadout-slot__placeholder-title");

            var category = descriptor.Category.DisplayName();
            var desc = descriptor.Description ?? string.Empty;
            _sub.text = string.IsNullOrEmpty(desc) ? category : $"{category} · {desc}";
            _sub.RemoveFromClassList("loadout-slot__placeholder-sub");

            _unequipBtn.style.display = DisplayStyle.Flex;
            _plusGlyph.style.display = DisplayStyle.None;
        }

        public void SetDropTarget(bool active)
        {
            EnableInClassList("loadout-slot--drop-target", active);
        }

        private void ApplyEmpty()
        {
            AddToClassList("loadout-slot--empty");
            _numberBadge.AddToClassList("loadout-slot__num--empty");

            _icon.style.display = DisplayStyle.None;

            _title.text = "비어있는 슬롯";
            _title.AddToClassList("loadout-slot__placeholder-title");

            _sub.text = "우측에서 선택해 장착해보세요";
            _sub.AddToClassList("loadout-slot__placeholder-sub");

            _unequipBtn.style.display = DisplayStyle.None;
            _plusGlyph.style.display = DisplayStyle.Flex;
        }

        private void OnRootClicked(ClickEvent evt)
        {
            // 해제 버튼은 자체 콜백을 갖고 별도 이벤트를 발행한다.
            if (evt.target == _unequipBtn) return;
            Clicked?.Invoke();
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
