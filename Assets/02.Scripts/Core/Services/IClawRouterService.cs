using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// ClawRouter 스마트 라우터 관리
    /// - Gateway ↔ AI API 사이에서 요청 난이도 분석 후 최적 모델 자동 분배
    /// - 비용 최대 92% 절감
    /// - Free 모드: Ollama 로컬 모델 전용 (API 키 불필요)
    /// </summary>
    public interface IClawRouterService
    {
        ReadOnlyReactiveProperty<float>  Progress   { get; }
        ReadOnlyReactiveProperty<string> StatusText { get; }

        /// <summary>ClawRouter 설치 여부</summary>
        UniTask<bool> IsInstalledAsync(CancellationToken ct = default);

        /// <summary>ClawRouter 설치</summary>
        UniTask<bool> InstallAsync(CancellationToken ct = default);

        /// <summary>현재 라우팅 설정 조회</summary>
        UniTask<RoutingConfig> GetCurrentConfigAsync(CancellationToken ct = default);

        /// <summary>
        /// 라우팅 모드 설정
        /// Free: Ollama 로컬 전용 (무료)
        /// Eco/Auto/Premium: 클라우드 API 키 필요
        /// </summary>
        UniTask<bool> SetRoutingModeAsync(RoutingMode mode, CancellationToken ct = default);

        /// <summary>모드별 예상 월 비용 조회</summary>
        UniTask<decimal> GetEstimatedCostAsync(RoutingMode mode, CancellationToken ct = default);

        /// <summary>설정 변경 이벤트</summary>
        Observable<RoutingConfig> OnConfigChanged { get; }
    }
}
