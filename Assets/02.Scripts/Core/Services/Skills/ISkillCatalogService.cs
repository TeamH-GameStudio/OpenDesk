using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using R3;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 카탈로그 in-memory 캐시 + 디스크 캐시 + 자동 새로고침 + 설치 상태 머지.
    /// UI/추천 서비스가 의존하는 단일 진입점.
    /// </summary>
    public interface ISkillCatalogService
    {
        Observable<Unit> OnCatalogChanged { get; }

        /// <summary>
        /// 디스크 캐시 → in-memory → (TTL 만료 시) 원격 fetch. 초기화/강제 새로고침 모두 호출.
        /// </summary>
        UniTask<bool> RefreshAsync(bool forceRefresh, CancellationToken ct);

        /// <summary>현재 카탈로그가 비어 있지 않은지</summary>
        bool IsLoaded { get; }

        /// <summary>모든 스킬 (IsInstalled 상태가 반영됨)</summary>
        IReadOnlyList<SkillDescriptor> GetAll();

        /// <summary>특정 카테고리의 스킬</summary>
        IReadOnlyList<SkillDescriptor> GetByCategory(SkillCategory category);

        /// <summary>역할 추천 카테고리 키 목록</summary>
        IReadOnlyList<SkillCategory> GetRecommendedCategoriesFor(AgentRole role);

        /// <summary>ID 단건 조회 (없으면 null)</summary>
        SkillDescriptor GetById(string skillId);

        /// <summary>설치 상태 변경 알림 (인스톨러가 호출)</summary>
        void NotifyInstallStateChanged(string skillId, bool isInstalled, string installPath);
    }
}
