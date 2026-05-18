using System;
using System.ComponentModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine.UIElements;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// §3 OAuth 화면 컨트롤러. Idle/Pending/Error 패널 토글.
    /// 스피너는 App UI CircularProgress (UXML 상에서 indeterminate 자동 회전).
    /// </summary>
    public sealed class OnbAuthView : IDisposable
    {
        private readonly OnbAuthViewModel _vm;
        private readonly CancellationToken _ct;
        private readonly Button _googleBtn;
        private readonly Button _retryBtn;
        private readonly Button _prevBtn;
        private readonly VisualElement _idlePane;
        private readonly VisualElement _pendingPane;
        private readonly VisualElement _errorPane;
        private readonly Label _errorMessage;

        public OnbAuthView(VisualElement root, OnbAuthViewModel vm, CancellationToken ct)
        {
            _vm = vm;
            _ct = ct;

            _googleBtn = root.Q<Button>("onb-auth__google");
            _retryBtn = root.Q<Button>("onb-auth__retry");
            _prevBtn = root.Q<Button>("onb-auth__prev");
            _idlePane = root.Q<VisualElement>("onb-auth__idle");
            _pendingPane = root.Q<VisualElement>("onb-auth__pending");
            _errorPane = root.Q<VisualElement>("onb-auth__error");
            _errorMessage = root.Q<Label>("onb-auth__error-message");

            if (_googleBtn != null) _googleBtn.clicked += OnSignInClicked;
            if (_retryBtn != null) _retryBtn.clicked += OnSignInClicked;
            if (_prevBtn != null) _prevBtn.clicked += OnPrevClicked;

            _vm.PropertyChanged += OnVmPropertyChanged;
            RefreshState();
        }

        private void OnPrevClicked() => _vm.Back();

        private void OnSignInClicked() => _vm.SignInAsync(_ct).Forget();

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e) => RefreshState();

        private void RefreshState()
        {
            var state = _vm.State;
            SetPaneVisible(_idlePane, state == OnbAuthViewModel.AuthState.Idle);
            SetPaneVisible(_pendingPane, state == OnbAuthViewModel.AuthState.Pending);
            SetPaneVisible(_errorPane, state == OnbAuthViewModel.AuthState.Error);

            if (_googleBtn != null) _googleBtn.SetEnabled(_vm.CanSignIn);
            if (_retryBtn != null) _retryBtn.SetEnabled(_vm.CanSignIn);

            if (_errorMessage != null && !string.IsNullOrEmpty(_vm.ErrorMessage))
            {
                _errorMessage.text = _vm.ErrorMessage;
            }
        }

        private static void SetPaneVisible(VisualElement pane, bool visible)
        {
            if (pane == null) return;
            pane.EnableInClassList("onb-auth__pane--hidden", !visible);
        }

        public void Dispose()
        {
            if (_googleBtn != null) _googleBtn.clicked -= OnSignInClicked;
            if (_retryBtn != null) _retryBtn.clicked -= OnSignInClicked;
            if (_prevBtn != null) _prevBtn.clicked -= OnPrevClicked;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }
}
