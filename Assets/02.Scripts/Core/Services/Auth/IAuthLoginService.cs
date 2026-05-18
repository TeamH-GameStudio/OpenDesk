using R3;

namespace OpenDesk.Core.Services.Auth
{
    /// <summary>
    /// Claude CLI 'claude login' 격리 OAuth 라이프사이클 래퍼.
    /// 미들웨어와 WebSocket 으로 통신하여 device URL/code 를 사용자에게 노출하고,
    /// 인증 결과(success/failed)를 알린다.
    /// </summary>
    public interface IAuthLoginService
    {
        Observable<AuthLoginState> OnState { get; }

        AuthLoginState CurrentState { get; }

        bool IsActive { get; }

        void Start();

        void Cancel();
    }

    public sealed class AuthLoginState
    {
        public AuthLoginPhase Phase { get; init; } = AuthLoginPhase.Idle;
        public string Url { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public enum AuthLoginPhase
    {
        Idle,
        Starting,
        AwaitingUser,   // URL/code 발행됨 — 사용자 브라우저 액션 대기
        Polling,        // 진행 중 (status 메시지)
        Success,
        Failed,
        Cancelled,
    }
}
