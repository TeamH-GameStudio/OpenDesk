using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// OpenClaw 설치 여부 및 버전 감지
    /// 순수 탐색 전용 — 설치/실행 없음
    /// </summary>
    public interface IOpenClawDetector
    {
        // 설치 여부
        UniTask<bool> IsInstalledAsync(CancellationToken ct = default);

        // 설치 경로 반환 (없으면 null)
        UniTask<string> GetInstallPathAsync(CancellationToken ct = default);

        // 버전 문자열 반환 (없으면 null)
        UniTask<string> GetVersionAsync(CancellationToken ct = default);

        // Gateway가 현재 수신 대기 중인지 포트 체크
        UniTask<bool> IsGatewayListeningAsync(int port, CancellationToken ct = default);

        // Gateway 인증 토큰 읽기 (openclaw.json에서)
        UniTask<string> GetGatewayTokenAsync(CancellationToken ct = default);
    }
}
