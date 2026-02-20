using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    public interface IWorkspaceService
    {
        string LocalPath      { get; }
        bool   IsInitialized  { get; }

        // 로컬 폴더 경로 지정 (설정 저장 포함)
        void SetLocalPath(string path);

        // 파일 목록 — 로컬 + Drive 통합
        UniTask<IReadOnlyList<WorkspaceEntry>> GetEntriesAsync(CancellationToken ct = default);

        // 파일 변경 알림 스트림
        Observable<WorkspaceEntry> OnEntryChanged { get; }

        // OS 기본 앱으로 파일 열기
        void OpenEntry(WorkspaceEntry entry);
    }
}
