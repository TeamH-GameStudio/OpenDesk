using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// Google Drive 미사용 시 등록되는 Null Object
    /// GOOGLE_DRIVE_ENABLED 심볼이 없을 때 CoreInstaller가 이 구현을 주입
    /// </summary>
    public class NullGoogleDriveService : IGoogleDriveService
    {
        public bool IsAuthenticated => false;
        public ReadOnlyReactiveProperty<bool> AuthState { get; } =
            new ReactiveProperty<bool>(false).ToReadOnlyReactiveProperty();

        public UniTask<bool> AuthenticateAsync(CancellationToken ct = default) =>
            UniTask.FromResult(false);

        public void RevokeAuth() { }

        public UniTask<IReadOnlyList<WorkspaceEntry>> ListFilesAsync(
            string folderId, CancellationToken ct = default) =>
            UniTask.FromResult<IReadOnlyList<WorkspaceEntry>>(System.Array.Empty<WorkspaceEntry>());
    }
}
