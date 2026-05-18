using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Services.Licensing;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations.Licensing
{
    /// <summary>
    /// Phase 1 구현 — 미들웨어 WebSocket 으로 라이선스 활성화/JWT 바인딩을 프록시.
    ///
    /// 흐름:
    ///   1) ActivateAsync(key, deviceName)
    ///   2) IDeviceFingerprintService.GetFingerprintAsync()
    ///   3) ClaudeWebSocketClient.SendLicenseActivate(...)
    ///   4) OnLicenseActivated 수신 → JWT 저장 → SendAuthToken(jwt) → OnAuthStatus
    ///   5) 실패 시 OnLicenseError → Phase = DeviceLimitReached / Invalid
    ///
    /// PlayerPrefs 키:
    ///   OpenDesk_License_Key          (사용자 입력 보존, 재활성화 편의)
    ///   OpenDesk_License_Jwt          (활성 JWT)
    ///   OpenDesk_License_RefreshToken (만료 시 갱신 — Phase 2 에서 사용)
    ///   OpenDesk_License_UserId / OpenDesk_License_PlanTier  (UI 캐시)
    /// </summary>
    public sealed class LicenseService : ILicenseService, IDisposable
    {
        public const string PrefsKeyLicenseKey = "OpenDesk_License_Key";
        public const string PrefsKeyJwt = "OpenDesk_License_Jwt";
        public const string PrefsKeyRefresh = "OpenDesk_License_RefreshToken";
        public const string PrefsKeyUserId = "OpenDesk_License_UserId";
        public const string PrefsKeyPlanTier = "OpenDesk_License_PlanTier";

        private readonly ClaudeWebSocketClient _ws;
        private readonly IDeviceFingerprintService _fingerprint;
        private readonly Subject<LicenseState> _state = new();
        private LicenseState _current = new();
        private UniTaskCompletionSource<LicenseActivationOutcome> _pendingActivation;
        private readonly object _activationGate = new();

        public Observable<LicenseState> OnStateChanged => _state;
        public LicenseState CurrentState => _current;

        [Inject]
        public LicenseService(IDeviceFingerprintService fingerprint, ClaudeWebSocketClient ws = null)
        {
            _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            _ws = ws; // OnboardingScene 에서는 WS 가 아직 없을 수 있다 — 활성화는 skip 으로 우회.

            if (_ws != null)
            {
                _ws.OnLicenseActivated += HandleActivated;
                _ws.OnLicenseError += HandleLicenseError;
                _ws.OnAuthStatus += HandleAuthStatus;
                _ws.OnConnectionChanged += HandleConnectionChanged;
            }

            // 캐시된 JWT 자동 로드 — Active 상태면 다음 OnConnectionChanged 에서 자동 rebind.
            LoadCachedCredentials();
        }

        public void LoadCachedCredentials()
        {
            var jwt = PlayerPrefs.GetString(PrefsKeyJwt, string.Empty);
            if (string.IsNullOrEmpty(jwt))
            {
                Transition(new LicenseState { Phase = LicensePhase.Inactive });
                return;
            }

            Transition(new LicenseState
            {
                Phase = LicensePhase.Active,
                UserId = PlayerPrefs.GetString(PrefsKeyUserId, string.Empty),
                PlanTier = PlayerPrefs.GetString(PrefsKeyPlanTier, string.Empty),
            });
            RebindIfActive();
        }

        public async UniTask<LicenseActivationOutcome> ActivateAsync(
            string licenseKey, string deviceName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                return new LicenseActivationOutcome(false, "invalid_input", "라이선스 키가 필요합니다");
            }

            if (_ws == null || !_ws.IsConnected)
            {
                return new LicenseActivationOutcome(false, "ws_not_connected", "미들웨어에 연결되지 않았습니다");
            }

            var fingerprint = await _fingerprint.GetFingerprintAsync(ct);
            var name = string.IsNullOrWhiteSpace(deviceName)
                ? _fingerprint.GetSuggestedDeviceName()
                : deviceName.Trim();

            UniTaskCompletionSource<LicenseActivationOutcome> tcs;
            lock (_activationGate)
            {
                if (_pendingActivation != null && !_pendingActivation.Task.Status.IsCompleted())
                {
                    return new LicenseActivationOutcome(false, "already_activating", "이미 진행 중인 활성화 요청이 있습니다");
                }
                tcs = new UniTaskCompletionSource<LicenseActivationOutcome>();
                _pendingActivation = tcs;
            }

            Transition(new LicenseState { Phase = LicensePhase.Activating });
            _ws.SendLicenseActivate(licenseKey.Trim(), fingerprint, name);

            // 사용자 입력 키 보존 — 재활성화 편의용 (덮어쓰기 OK)
            PlayerPrefs.SetString(PrefsKeyLicenseKey, licenseKey.Trim());
            PlayerPrefs.Save();

            using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                lock (_activationGate)
                {
                    _pendingActivation = null;
                }
                return new LicenseActivationOutcome(false, "cancelled", "취소됨");
            }
        }

        public void RebindIfActive()
        {
            var jwt = PlayerPrefs.GetString(PrefsKeyJwt, string.Empty);
            if (string.IsNullOrEmpty(jwt)) return;
            if (_ws == null || !_ws.IsConnected) return;
            _ws.SendAuthToken(jwt);
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(PrefsKeyJwt);
            PlayerPrefs.DeleteKey(PrefsKeyRefresh);
            PlayerPrefs.DeleteKey(PrefsKeyUserId);
            PlayerPrefs.DeleteKey(PrefsKeyPlanTier);
            PlayerPrefs.Save();
            if (_ws != null && _ws.IsConnected)
            {
                _ws.SendAuthToken(string.Empty);
            }
            Transition(new LicenseState { Phase = LicensePhase.Inactive });
        }

        // ── WebSocket 이벤트 핸들러 ─────────────────────────

        private void HandleActivated(LicenseActivatedMessage msg)
        {
            if (msg == null) return;
            PlayerPrefs.SetString(PrefsKeyJwt, msg.jwt ?? string.Empty);
            PlayerPrefs.SetString(PrefsKeyRefresh, msg.refreshToken ?? string.Empty);
            PlayerPrefs.SetString(PrefsKeyUserId, msg.userId ?? string.Empty);
            PlayerPrefs.SetString(PrefsKeyPlanTier, msg.planTier ?? string.Empty);
            PlayerPrefs.Save();

            Transition(new LicenseState
            {
                Phase = LicensePhase.Active,
                UserId = msg.userId ?? string.Empty,
                PlanTier = msg.planTier ?? string.Empty,
                Balance = msg.balance,
            });

            // 활성화 직후 set_auth 전송 — 이후 chat 에서 opendesk_routed 가 라우팅 가능.
            if (!string.IsNullOrEmpty(msg.jwt) && _ws != null && _ws.IsConnected)
            {
                _ws.SendAuthToken(msg.jwt);
            }

            CompletePendingActivation(new LicenseActivationOutcome(true));
        }

        private void HandleLicenseError(LicenseErrorMessage err)
        {
            if (err == null) return;
            var phase = err.code switch
            {
                "device_limit_reached" => LicensePhase.DeviceLimitReached,
                "invalid_license" => LicensePhase.Invalid,
                "expired" => LicensePhase.Expired,
                _ => LicensePhase.Invalid,
            };
            Transition(new LicenseState
            {
                Phase = phase,
                ErrorCode = err.code ?? string.Empty,
                ErrorMessage = err.message ?? string.Empty,
            });
            CompletePendingActivation(new LicenseActivationOutcome(false, err.code ?? string.Empty, err.message ?? string.Empty));
        }

        private void HandleAuthStatus(AuthStatusMessage status)
        {
            if (status == null) return;
            if (!status.authenticated && _current.Phase == LicensePhase.Active)
            {
                Transition(new LicenseState { Phase = LicensePhase.Expired });
            }
        }

        private void HandleConnectionChanged(bool connected, string _)
        {
            // 재연결 시 JWT 자동 rebind
            if (connected)
            {
                RebindIfActive();
            }
        }

        // ── 내부 ─────────────────────────────────────────────

        private void Transition(LicenseState next)
        {
            _current = next;
            _state.OnNext(next);
        }

        private void CompletePendingActivation(LicenseActivationOutcome outcome)
        {
            UniTaskCompletionSource<LicenseActivationOutcome> tcs;
            lock (_activationGate)
            {
                tcs = _pendingActivation;
                _pendingActivation = null;
            }
            tcs?.TrySetResult(outcome);
        }

        public void Dispose()
        {
            if (_ws != null)
            {
                _ws.OnLicenseActivated -= HandleActivated;
                _ws.OnLicenseError -= HandleLicenseError;
                _ws.OnAuthStatus -= HandleAuthStatus;
                _ws.OnConnectionChanged -= HandleConnectionChanged;
            }
            _state.Dispose();
        }
    }
}
