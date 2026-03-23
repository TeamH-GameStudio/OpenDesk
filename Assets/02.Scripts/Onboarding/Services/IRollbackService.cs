using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// OpenDesk가 설치한 항목을 추적하고 롤백(제거/복원)하는 서비스
    /// </summary>
    public interface IRollbackService
    {
        /// <summary>설치 항목 기록 추가</summary>
        void RecordInstall(InstalledItem item);

        /// <summary>현재까지 설치된 항목 목록</summary>
        IReadOnlyList<InstalledItem> GetInstalledItems();

        /// <summary>특정 항목 롤백 (제거/복원)</summary>
        UniTask<bool> RollbackItemAsync(string itemId, CancellationToken ct = default);

        /// <summary>전체 롤백 — OpenDesk가 설치한 모든 것을 제거</summary>
        UniTask<bool> RollbackAllAsync(CancellationToken ct = default);

        /// <summary>설치 기록을 디스크에서 로드</summary>
        void LoadRecord();

        /// <summary>설치 기록을 디스크에 저장</summary>
        void SaveRecord();
    }
}
