using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// Google OAuth (UI 스텁 단계). 현재 구현체는 1.5초 지연 후 성공 반환.
    /// 실제 OAuth 도입 시 본 인터페이스를 그대로 두고 구현체만 교체한다.
    /// </summary>
    public interface IGoogleAuthService
    {
        UniTask<AuthResult> SignInAsync(CancellationToken ct = default);
    }
}
