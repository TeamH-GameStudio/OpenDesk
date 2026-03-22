using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// WSL2 (Windows Subsystem for Linux) 감지 및 활성화
    /// Windows 전용 — OpenClaw 안정성을 위해 필수
    /// </summary>
    public interface IWsl2Service
    {
        ReadOnlyReactiveProperty<float>  Progress   { get; }
        ReadOnlyReactiveProperty<string> StatusText { get; }

        /// <summary>WSL2가 활성화되어 있는지 확인</summary>
        UniTask<bool> IsEnabledAsync(CancellationToken ct = default);

        /// <summary>설치된 WSL 배포판 목록 반환</summary>
        UniTask<IReadOnlyList<string>> GetDistributionsAsync(CancellationToken ct = default);

        /// <summary>
        /// WSL2 활성화 (관리자 권한 필요)
        /// 재부팅이 필요할 수 있음 — needsReboot 반환
        /// </summary>
        UniTask<Wsl2InstallResult> EnableAsync(CancellationToken ct = default);
    }

    public class Wsl2InstallResult
    {
        public bool Success      { get; set; }
        public bool NeedsReboot  { get; set; }
        public string Message    { get; set; } = "";
    }
}
