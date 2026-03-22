using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 관리자 권한 획득/강등/체크
    /// 보안 핵심: 설치 완료 즉시 일반 사용자 모드로 강등
    /// </summary>
    public interface IAdminPrivilegeService
    {
        /// <summary>현재 프로세스가 관리자 권한인지 확인</summary>
        UniTask<bool> IsElevatedAsync(CancellationToken ct = default);

        /// <summary>
        /// 관리자 권한으로 외부 프로세스 실행
        /// Windows: runas verb, macOS/Linux: sudo
        /// </summary>
        UniTask<ProcessOutput> RunElevatedAsync(
            string command,
            string arguments,
            CancellationToken ct = default);

        /// <summary>
        /// 관리자 권한 강등 — 일반 사용자 모드로 프로그램 재시작
        /// 설치 완료 후 즉시 호출
        /// </summary>
        UniTask DropPrivilegesAndRestartAsync(CancellationToken ct = default);
    }

    /// <summary>외부 프로세스 실행 결과</summary>
    public class ProcessOutput
    {
        public int    ExitCode { get; set; }
        public string StdOut   { get; set; } = "";
        public string StdErr   { get; set; } = "";
        public bool   Success  => ExitCode == 0;
    }
}
