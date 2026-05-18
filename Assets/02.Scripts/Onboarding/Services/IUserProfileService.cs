using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 사용자 기본 프로필(이름/성별/나이) 조회 + 영속화.
    /// 내부적으로 IGameDataService를 통해 UserProfileData를 캐싱/저장한다.
    /// </summary>
    public interface IUserProfileService
    {
        /// <summary>
        /// 캐시에 있는 현재 프로필 (영속 미설정이면 null).
        /// </summary>
        UserProfile Current { get; }

        /// <summary>
        /// 캐시에서 프로필을 다시 읽어온다 (앱 시작 직후 InitializeAllData 이후에 호출 권장).
        /// </summary>
        UniTask<UserProfile> LoadAsync(CancellationToken ct = default);

        /// <summary>
        /// 프로필을 캐시에 적용 후 저장한다.
        /// </summary>
        UniTask<bool> SaveAsync(UserProfile profile, CancellationToken ct = default);
    }
}
