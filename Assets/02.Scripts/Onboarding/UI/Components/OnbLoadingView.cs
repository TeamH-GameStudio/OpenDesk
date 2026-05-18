using System;
using System.ComponentModel;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine.UIElements;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §6 로딩 화면 컨트롤러. 메시지 바인딩만 담당. 스피너는 App UI CircularProgress
    /// (UXML 상에서 indeterminate 으로 자동 회전). 시퀀스 자체는 ViewModel.RunAsync.
    /// </summary>
    public sealed class OnbLoadingView : IDisposable
    {
        private readonly OnbLoadingViewModel _vm;
        private readonly Label _messageLabel;

        public OnbLoadingView(VisualElement root, OnbLoadingViewModel vm)
        {
            _vm = vm;
            _messageLabel = root.Q<Label>("onb-loading__message");

            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyMessage();
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) => ApplyMessage();

        private void ApplyMessage()
        {
            if (_messageLabel == null) return;
            _messageLabel.text = _vm.CurrentMessage ?? string.Empty;
        }

        public void Dispose()
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }
}
