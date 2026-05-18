using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Common;
using OpenDesk.Onboarding.Services;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §3 OAuth 화면 ViewModel — Idle / Pending / Error 상태 머신.
    /// 백엔드는 IGoogleAuthService 추상으로 보고, 현재 구현체는 UI 스텁(FakeGoogleAuthService).
    /// </summary>
    public sealed class OnbAuthViewModel : ObservableObject
    {
        public enum AuthState
        {
            Idle = 0,
            Pending = 1,
            Error = 2,
        }

        private readonly IGoogleAuthService _authService;

        private AuthState _state = AuthState.Idle;
        public AuthState State
        {
            get => _state;
            private set
            {
                if (SetField(ref _state, value))
                {
                    Raise(nameof(CanSignIn));
                }
            }
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetField(ref _errorMessage, value);
        }

        public bool CanSignIn => _state != AuthState.Pending;

        public event Action<string> AuthSucceeded;
        public event Action BackRequested;

        public OnbAuthViewModel(IGoogleAuthService authService)
        {
            _authService = authService;
        }

        public void Back() => BackRequested?.Invoke();

        public async UniTask SignInAsync(CancellationToken ct = default)
        {
            if (_authService == null || State == AuthState.Pending) return;

            ErrorMessage = null;
            State = AuthState.Pending;

            var result = await _authService.SignInAsync(ct);
            if (result == null)
            {
                State = AuthState.Error;
                ErrorMessage = "알 수 없는 오류가 발생했어요";
                return;
            }

            if (result.Success)
            {
                State = AuthState.Idle;
                AuthSucceeded?.Invoke(result.Email);
                return;
            }

            State = AuthState.Error;
            ErrorMessage = "인증을 완료하지 못했어요";
        }
    }
}
