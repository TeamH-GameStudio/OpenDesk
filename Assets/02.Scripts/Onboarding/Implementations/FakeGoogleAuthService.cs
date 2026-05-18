using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// UI 스텁 구현체. 1.5초 지연 후 성공 반환. 실제 OAuth는 후속 PR에서 본 인터페이스를 구현하는 별도 서비스로 교체.
    /// </summary>
    public sealed class FakeGoogleAuthService : IGoogleAuthService
    {
        private const int DelayMilliseconds = 1500;
        private const string FakeEmail = "user@example.com";

        public async UniTask<AuthResult> SignInAsync(CancellationToken ct = default)
        {
            try
            {
                await UniTask.Delay(DelayMilliseconds, cancellationToken: ct);
                return AuthResult.Ok(FakeEmail);
            }
            catch (OperationCanceledException)
            {
                return AuthResult.Fail("cancelled");
            }
        }
    }
}
