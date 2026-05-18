using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Persistence;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// IGameDataService.UserProfileData 위에 얹은 얇은 래퍼.<br/>
    /// 외부에서는 immutable <see cref="UserProfile"/>로 주고받는다.
    /// </summary>
    public sealed class UserProfileService : IUserProfileService
    {
        private readonly IGameDataService _gameDataService;

        public UserProfileService(IGameDataService gameDataService)
        {
            _gameDataService = gameDataService;
        }

        public UserProfile Current
        {
            get
            {
                var data = _gameDataService.GetData<UserProfileData>();
                return data?.Snapshot();
            }
        }

        public async UniTask<UserProfile> LoadAsync(CancellationToken ct = default)
        {
            var data = _gameDataService.GetData<UserProfileData>();
            if (data == null)
            {
                Debug.LogWarning("[UserProfileService] UserProfileData가 캐시에 없습니다 — InitializeAllData가 선행되어야 합니다.");
                return null;
            }

            await _gameDataService.FetchData<UserProfileData>().AttachExternalCancellation(ct);
            return data.Snapshot();
        }

        public async UniTask<bool> SaveAsync(UserProfile profile, CancellationToken ct = default)
        {
            if (profile == null) return false;

            var data = _gameDataService.GetData<UserProfileData>() ?? new UserProfileData();
            data.Apply(profile);

            return await _gameDataService.SaveData(data).AttachExternalCancellation(ct);
        }
    }
}
