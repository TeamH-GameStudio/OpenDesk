using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// Google Drive 비활성 시 사용되는 더미 구현
    /// GOOGLE_DRIVE_ENABLED 심볼 없을 때 VContainer 의존성 해소용
    /// </summary>
    public class NullGoogleDriveService : IGoogleDriveService
    {
        private readonly ReactiveProperty<bool> _authState = new(false);

        public bool IsAuthenticated => false;
        public ReadOnlyReactiveProperty<bool> AuthState => _authState;

        public UniTask<bool> AuthenticateAsync(CancellationToken ct = default)
            => UniTask.FromResult(false);

        public void RevokeAuth() { }

        public UniTask<IReadOnlyList<WorkspaceEntry>> ListFilesAsync(
            string folderId, CancellationToken ct = default)
            => UniTask.FromResult<IReadOnlyList<WorkspaceEntry>>(new List<WorkspaceEntry>());
    }
}
