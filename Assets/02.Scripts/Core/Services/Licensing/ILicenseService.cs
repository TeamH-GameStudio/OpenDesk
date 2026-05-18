using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Core.Services.Licensing
{
    /// <summary>
    /// 라이선스 활성화 / JWT 보관 / 만료 감시.
    ///
    /// Phase 1: ClaudeWebSocketClient 를 통해 미들웨어가 mock_routing_server 로 프록시.
    /// 실 클라우드 서버 도입 시 본 인터페이스 동일, 구현체만 UnityWebRequest 로 교체.
    /// </summary>
    public interface ILicenseService
    {
        Observable<LicenseState> OnStateChanged { get; }

        LicenseState CurrentState { get; }

        /// <summary>PlayerPrefs 의 캐시된 JWT 를 로드. 없으면 Inactive.</summary>
        void LoadCachedCredentials();

        /// <summary>라이선스 키 + 디바이스 지문으로 활성화. 성공 시 JWT 저장 + 미들웨어 set_auth.</summary>
        UniTask<LicenseActivationOutcome> ActivateAsync(string licenseKey, string deviceName, CancellationToken ct = default);

        /// <summary>저장된 JWT 를 미들웨어에 재 바인딩 (재연결시 사용).</summary>
        void RebindIfActive();

        /// <summary>로그아웃. PlayerPrefs 비우고 미들웨어 unbind.</summary>
        void Clear();
    }

    public sealed record LicenseState
    {
        public LicensePhase Phase { get; init; } = LicensePhase.Inactive;
        public string UserId { get; init; } = string.Empty;
        public string PlanTier { get; init; } = string.Empty;
        public long Balance { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;
    }

    public enum LicensePhase
    {
        Inactive,
        Activating,
        Active,
        Expired,
        DeviceLimitReached,
        Invalid,
    }

    public sealed record LicenseActivationOutcome(
        bool Success,
        string ErrorCode = "",
        string ErrorMessage = "");
}
