using System;
using System.ComponentModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.ViewModels;
using Unity.AppUI.UI;
using UnityEngine.UIElements;
// UXML 이 만드는 Button/TextField 는 UI Toolkit 것이므로 별칭으로 고정해 모호성 제거.
using Button = UnityEngine.UIElements.Button;
using TextField = UnityEngine.UIElements.TextField;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §4 유저 정보 입력 화면 컨트롤러.
    /// App UI RadioGroup 으로 성별/나이 선택 — 수동 라디오 빌드/Dictionary 추적 불필요.
    /// </summary>
    public sealed class OnbUserView : IDisposable
    {
        private readonly OnbUserViewModel _vm;
        private readonly CancellationToken _ct;
        private readonly TextField _nameInput;
        private readonly Label _placeholder;
        private readonly Button _prevBtn;
        private readonly Button _nextBtn;
        private readonly RadioGroup _genderGroup;
        private readonly RadioGroup _ageGroup;

        private static readonly (Gender Tier, string Label)[] GenderOptions =
        {
            (Gender.Female, "여성"),
            (Gender.Male, "남성"),
            (Gender.Undisclosed, "비공개"),
        };

        private static readonly (AgeBucket Tier, string Label)[] AgeOptions =
        {
            (AgeBucket.Teens, "10대"),
            (AgeBucket.Twenties, "20대"),
            (AgeBucket.Thirties, "30대"),
            (AgeBucket.Forties, "40대"),
            (AgeBucket.FiftiesPlus, "50대+"),
            (AgeBucket.Undisclosed, "비공개"),
        };

        public OnbUserView(VisualElement root, OnbUserViewModel vm, CancellationToken ct)
        {
            _vm = vm;
            _ct = ct;

            _nameInput = root.Q<TextField>("onb-user__name");
            _placeholder = root.Q<Label>("onb-user__name-placeholder");
            _prevBtn = root.Q<Button>("onb-user__prev");
            _nextBtn = root.Q<Button>("onb-user__next");

            _genderGroup = BuildGenderGroup(root.Q<VisualElement>("onb-user__gender"));
            if (_genderGroup != null)
                _genderGroup.RegisterValueChangedCallback(OnGenderChanged);

            _ageGroup = BuildAgeGroup(root.Q<VisualElement>("onb-user__age"));
            if (_ageGroup != null)
                _ageGroup.RegisterValueChangedCallback(OnAgeChanged);

            if (_nameInput != null)
            {
                _nameInput.value = _vm.Name ?? string.Empty;
                _nameInput.RegisterValueChangedCallback(OnNameChanged);
            }

            if (_prevBtn != null) _prevBtn.clicked += OnPrevClicked;
            if (_nextBtn != null) _nextBtn.clicked += OnNextClicked;

            _vm.PropertyChanged += OnVmPropertyChanged;
            RefreshState();
        }

        private static RadioGroup BuildGenderGroup(VisualElement container)
        {
            if (container == null) return null;
            container.Clear();
            var group = new RadioGroup();
            group.style.flexDirection = FlexDirection.Row;
            foreach (var (tier, label) in GenderOptions)
            {
                var radio = new Radio { key = tier.ToString(), label = label };
                radio.style.marginRight = 16;
                group.Add(radio);
            }
            container.Add(group);
            return group;
        }

        private static RadioGroup BuildAgeGroup(VisualElement container)
        {
            if (container == null) return null;
            container.Clear();
            var group = new RadioGroup();
            group.style.flexDirection = FlexDirection.Row;
            group.style.flexWrap = Wrap.Wrap;
            foreach (var (tier, label) in AgeOptions)
            {
                var radio = new Radio { key = tier.ToString(), label = label };
                radio.style.width = new StyleLength(new Length(33, LengthUnit.Percent));
                radio.style.marginBottom = 8;
                group.Add(radio);
            }
            container.Add(group);
            return group;
        }

        private void OnGenderChanged(ChangeEvent<string> evt)
        {
            if (Enum.TryParse<Gender>(evt.newValue, out var gender))
                _vm.SelectGender(gender);
        }

        private void OnAgeChanged(ChangeEvent<string> evt)
        {
            if (Enum.TryParse<AgeBucket>(evt.newValue, out var age))
                _vm.SelectAge(age);
        }

        private void OnNameChanged(ChangeEvent<string> evt)
        {
            _vm.Name = evt.newValue;
            UpdatePlaceholder();
        }

        private void UpdatePlaceholder()
        {
            if (_placeholder == null) return;
            var hasText = !string.IsNullOrEmpty(_nameInput?.value);
            _placeholder.EnableInClassList("onb-input-placeholder--hidden", hasText);
        }

        private void OnPrevClicked() => _vm.Back();

        private void OnNextClicked() => _vm.AdvanceAsync(_ct).Forget();

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) => RefreshState();

        private void RefreshState()
        {
            if (_genderGroup != null)
                _genderGroup.SetValueWithoutNotify(_vm.SelectedGender.ToString());

            if (_ageGroup != null)
                _ageGroup.SetValueWithoutNotify(_vm.SelectedAge.ToString());

            if (_nextBtn != null) _nextBtn.SetEnabled(_vm.CanAdvance);
            UpdatePlaceholder();
        }

        public void Dispose()
        {
            if (_nameInput != null) _nameInput.UnregisterValueChangedCallback(OnNameChanged);
            if (_prevBtn != null) _prevBtn.clicked -= OnPrevClicked;
            if (_nextBtn != null) _nextBtn.clicked -= OnNextClicked;
            if (_genderGroup != null) _genderGroup.UnregisterValueChangedCallback(OnGenderChanged);
            if (_ageGroup != null) _ageGroup.UnregisterValueChangedCallback(OnAgeChanged);
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }
}
