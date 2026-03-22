using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// Node.js 환경 감지 및 자동 설치
    /// OpenClaw 구동을 위한 전제 조건 (최소 22.16, 권장 24+)
    /// </summary>
    public interface INodeEnvironmentService
    {
        ReadOnlyReactiveProperty<float>  Progress   { get; }
        ReadOnlyReactiveProperty<string> StatusText { get; }

        /// <summary>Node.js가 PATH에 존재하는지 확인</summary>
        UniTask<bool> IsInstalledAsync(CancellationToken ct = default);

        /// <summary>설치된 Node.js 버전 반환 (예: "24.1.0"), 미설치 시 null</summary>
        UniTask<string> GetVersionAsync(CancellationToken ct = default);

        /// <summary>최소 버전 이상인지 확인</summary>
        UniTask<bool> MeetsMinVersionAsync(string minVersion = "22.16.0", CancellationToken ct = default);

        /// <summary>Node.js 자동 설치 (Windows: MSI 사일런트, macOS: pkg/brew)</summary>
        UniTask<bool> InstallAsync(CancellationToken ct = default);
    }
}
