using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;
using R3;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 스킬 zip 다운로드 → 검증 → 설치(압축 해제) → 매니페스트 파싱.
    /// 또한 ~/.opendesk/skills 디렉토리 스캔으로 설치 상태 동기화.
    /// </summary>
    public interface ISkillInstallerService
    {
        Observable<SkillInstallEvent> OnInstallChanged { get; }

        UniTask<bool> InstallAsync(
            string skillId,
            IProgress<float> progress,
            CancellationToken ct);

        UniTask<bool> UninstallAsync(string skillId, CancellationToken ct);

        /// <summary>~/.opendesk/skills 디렉토리 스캔. 설치된 스킬의 SkillDescriptor 목록 반환.</summary>
        UniTask<IReadOnlyList<SkillDescriptor>> ScanInstalledAsync(CancellationToken ct);
    }

    public readonly struct SkillInstallEvent
    {
        public readonly string SkillId;
        public readonly bool IsInstalled;
        public readonly string InstallPath;

        public SkillInstallEvent(string skillId, bool isInstalled, string installPath)
        {
            SkillId = skillId;
            IsInstalled = isInstalled;
            InstallPath = installPath;
        }
    }
}
