using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services.Licensing;
using OpenDesk.Onboarding.Common;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §3.5 라이선스 활성화 화면 ViewModel — Idle / Activating / Activated / Error.
    /// 백엔드는 ILicenseService 추상으로 보고, Phase 1 에서는 미들웨어 mock 으로 프록시.
    /// </summary>
    public sealed class OnbLicenseViewModel : ObservableObject
    {
        public enum ViewState
        {
            Idle,
            Activating,
            Activated,
            Error,
        }

        private readonly ILicenseService _licenseService;
        private readonly IDeviceFingerprintService _fingerprint;

        private ViewState _state = ViewState.Idle;
        public ViewState State
        {
            get => _state;
            private set
            {
                if (SetField(ref _state, value))
                {
                    Raise(nameof(CanActivate));
                }
            }
        }

        private string _licenseKey = string.Empty;
        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                if (SetField(ref _licenseKey, value ?? string.Empty))
                {
                    Raise(nameof(CanActivate));
                }
            }
        }

        private string _deviceName = string.Empty;
        public string DeviceName
        {
            get => _deviceName;
            set => SetField(ref _deviceName, value ?? string.Empty);
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            private set => SetField(ref _message, value);
        }

        public bool CanActivate
            => _state != ViewState.Activating && !string.IsNullOrWhiteSpace(_licenseKey);

        public event Action ActivationSucceeded;
        public event Action SkipRequested;
        public event Action BackRequested;

        public OnbLicenseViewModel(ILicenseService licenseService, IDeviceFingerprintService fingerprint)
        {
            _licenseService = licenseService;
            _fingerprint = fingerprint;

            if (_fingerprint != null)
            {
                _deviceName = _fingerprint.GetSuggestedDeviceName();
            }
        }

        public void Back() => BackRequested?.Invoke();
        public void Skip() => SkipRequested?.Invoke();

        public async UniTask ActivateAsync(CancellationToken ct = default)
        {
            if (_licenseService == null || !CanActivate) return;

            State = ViewState.Activating;
            Message = "활성화 중...";

            var outcome = await _licenseService.ActivateAsync(_licenseKey.Trim(), _deviceName, ct);
            if (outcome.Success)
            {
                State = ViewState.Activated;
                Message = "활성화 완료";
                ActivationSucceeded?.Invoke();
                return;
            }

            State = ViewState.Error;
            Message = outcome.ErrorCode switch
            {
                "device_limit_reached" => "이 라이선스는 이미 최대 디바이스 수에 도달했어요",
                "invalid_license" => "라이선스 키가 올바르지 않아요",
                "ws_not_connected" => "미들웨어 연결 대기 중... 잠시 후 다시 시도해주세요",
                _ => string.IsNullOrEmpty(outcome.ErrorMessage)
                    ? "활성화에 실패했어요"
                    : outcome.ErrorMessage,
            };
        }
    }
}
