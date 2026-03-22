using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// ClawHub 스킬 마켓플레이스
    /// - 스킬 검색/설치/삭제
    /// - 샌드박스 모드 토글
    /// - ~/.openclaw/skills/ 디렉토리 관리
    /// </summary>
    public interface ISkillMarketService
    {
        /// <summary>스킬 검색</summary>
        UniTask<IReadOnlyList<SkillEntry>> SearchSkillsAsync(string query = "", CancellationToken ct = default);

        /// <summary>추천 스킬 목록</summary>
        UniTask<IReadOnlyList<SkillEntry>> GetFeaturedSkillsAsync(CancellationToken ct = default);

        /// <summary>설치된 스킬 목록</summary>
        UniTask<IReadOnlyList<SkillEntry>> GetInstalledSkillsAsync(CancellationToken ct = default);

        /// <summary>스킬 설치 (SKILL.md 자동 배치)</summary>
        UniTask<bool> InstallSkillAsync(string skillId, CancellationToken ct = default);

        /// <summary>스킬 삭제</summary>
        UniTask<bool> UninstallSkillAsync(string skillId, CancellationToken ct = default);

        /// <summary>샌드박스 모드 토글 (agents.defaults.sandbox 설정)</summary>
        UniTask<bool> SetSandboxModeAsync(string skillId, bool enabled, CancellationToken ct = default);

        /// <summary>스킬 설치/삭제 이벤트</summary>
        Observable<SkillEntry> OnSkillChanged { get; }
    }
}
