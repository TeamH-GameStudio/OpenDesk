using System;
using System.ComponentModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using TextField = UnityEngine.UIElements.TextField;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §3.5 라이선스 활성화 화면 컨트롤러. 라이선스 키 + 디바이스 이름 입력 → ActivateAsync.
    /// </summary>
    public sealed class OnbLicenseView : IDisposable
    {
        private readonly OnbLicenseViewModel _vm;
        private readonly CancellationToken _ct;

        private readonly TextField _keyInput;
        private readonly TextField _deviceInput;
        private readonly Label _keyPlaceholder;
        private readonly Label _devicePlaceholder;
        private readonly Label _message;
        private readonly Button _prevBtn;
        private readonly Button _skipBtn;
        private readonly Button _activateBtn;

        public OnbLicenseView(VisualElement root, OnbLicenseViewModel vm, CancellationToken ct)
        {
            _vm = vm;
            _ct = ct;

            _keyInput = root.Q<TextField>("onb-license__key");
            _deviceInput = root.Q<TextField>("onb-license__device");
            _keyPlaceholder = root.Q<Label>("onb-license__key-placeholder");
            _devicePlaceholder = root.Q<Label>("onb-license__device-placeholder");
            _message = root.Q<Label>("onb-license__message");
            _prevBtn = root.Q<Button>("onb-license__prev");
            _skipBtn = root.Q<Button>("onb-license__skip");
            _activateBtn = root.Q<Button>("onb-license__activate");

            if (_keyInput != null)
            {
                _keyInput.value = _vm.LicenseKey ?? string.Empty;
                _keyInput.RegisterValueChangedCallback(OnKeyChanged);
            }
            if (_deviceInput != null)
            {
                _deviceInput.value = _vm.DeviceName ?? string.Empty;
                _deviceInput.RegisterValueChangedCallback(OnDeviceChanged);
            }
            if (_prevBtn != null) _prevBtn.clicked += OnPrev;
            if (_skipBtn != null) _skipBtn.clicked += OnSkip;
            if (_activateBtn != null) _activateBtn.clicked += OnActivate;

            _vm.PropertyChanged += OnVmChanged;
            RefreshState();
        }

        private void OnKeyChanged(ChangeEvent<string> evt) => _vm.LicenseKey = evt.newValue ?? string.Empty;
        private void OnDeviceChanged(ChangeEvent<string> evt) => _vm.DeviceName = evt.newValue ?? string.Empty;
        private void OnPrev() => _vm.Back();
        private void OnSkip() => _vm.Skip();
        private void OnActivate() => _vm.ActivateAsync(_ct).Forget();

        private void OnVmChanged(object sender, PropertyChangedEventArgs e) => RefreshState();

        private void RefreshState()
        {
            if (_activateBtn != null) _activateBtn.SetEnabled(_vm.CanActivate);
            if (_message != null) _message.text = _vm.Message ?? string.Empty;

            if (_keyPlaceholder != null)
            {
                _keyPlaceholder.style.display = string.IsNullOrEmpty(_vm.LicenseKey)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            if (_devicePlaceholder != null)
            {
                _devicePlaceholder.style.display = string.IsNullOrEmpty(_vm.DeviceName)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        public void Dispose()
        {
            if (_keyInput != null) _keyInput.UnregisterValueChangedCallback(OnKeyChanged);
            if (_deviceInput != null) _deviceInput.UnregisterValueChangedCallback(OnDeviceChanged);
            if (_prevBtn != null) _prevBtn.clicked -= OnPrev;
            if (_skipBtn != null) _skipBtn.clicked -= OnSkip;
            if (_activateBtn != null) _activateBtn.clicked -= OnActivate;
            _vm.PropertyChanged -= OnVmChanged;
        }
    }
}
