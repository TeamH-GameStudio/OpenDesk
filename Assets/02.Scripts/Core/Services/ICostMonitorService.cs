using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 실시간 비용/리소스 모니터링
    /// UI에서 게임 스타일 게이지 바로 표시
    /// </summary>
    public interface ICostMonitorService
    {
        /// <summary>현재 세션 누적 API 비용 (USD)</summary>
        ReadOnlyReactiveProperty<decimal> CurrentSessionCost { get; }

        /// <summary>총 사용 토큰 수</summary>
        ReadOnlyReactiveProperty<long> TotalTokensUsed { get; }

        /// <summary>프롬프트 캐싱으로 절약된 토큰 수</summary>
        ReadOnlyReactiveProperty<long> TokensSavedByCache { get; }

        /// <summary>CPU 점유율 (0.0 ~ 1.0)</summary>
        ReadOnlyReactiveProperty<float> CpuUsage { get; }

        /// <summary>RAM 사용량 (MB)</summary>
        ReadOnlyReactiveProperty<float> RamUsageMb { get; }

        /// <summary>비용 임계값 초과 경고</summary>
        Observable<decimal> OnCostAlert { get; }

        /// <summary>비용 경고 임계값 설정 (USD)</summary>
        void SetCostAlertThreshold(decimal threshold);

        /// <summary>세션 비용 리셋</summary>
        void ResetSession();

        /// <summary>리소스 모니터링 시작</summary>
        UniTask StartMonitoringAsync(CancellationToken ct = default);
    }
}
