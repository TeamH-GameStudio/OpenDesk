using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    public interface IGoogleDriveService
    {
        bool IsAuthenticated { get; }
        ReadOnlyReactiveProperty<bool> AuthState { get; }

        // OAuth 인증 (브라우저 열림)
        UniTask<bool> AuthenticateAsync(CancellationToken ct = default);
        void RevokeAuth();

        // 폴더 내 파일 목록 조회
        UniTask<IReadOnlyList<WorkspaceEntry>> ListFilesAsync(
            string folderId,
            CancellationToken ct = default);
    }
}
