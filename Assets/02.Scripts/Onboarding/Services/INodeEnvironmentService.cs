using System.Collections.Generic;
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

        /// <summary>
        /// 확인된 node 바이너리가 있는 디렉토리 반환 (예: ~/.nvm/.../bin)
        /// npm 등 같은 bin 디렉토리의 도구를 찾을 때 사용
        /// </summary>
        string GetNodeBinDirectory();

        /// <summary>설치된 Node.js 버전 반환 (예: "24.1.0"), 미설치 시 null</summary>
        UniTask<string> GetVersionAsync(CancellationToken ct = default);

        /// <summary>최소 버전 이상인지 확인</summary>
        UniTask<bool> MeetsMinVersionAsync(string minVersion = "22.16.0", CancellationToken ct = default);

        /// <summary>Node.js 자동 설치 (Windows: MSI 사일런트, macOS: pkg/brew)</summary>
        UniTask<bool> InstallAsync(CancellationToken ct = default);

        /// <summary>
        /// 컴퓨터에서 Node.js를 사용 중인 프로젝트 폴더 목록을 스캔합니다.
        /// (package.json이 있는 폴더를 찾음)
        /// </summary>
        UniTask<IReadOnlyList<string>> ScanExistingProjectsAsync(CancellationToken ct = default);

        /// <summary>
        /// nvm(Node Version Manager)을 사용해 기존 버전과 공존하도록 설치합니다.
        /// 기존 Node.js에 영향을 주지 않습니다.
        /// </summary>
        UniTask<bool> InstallViaNvmAsync(CancellationToken ct = default);
    }
}
