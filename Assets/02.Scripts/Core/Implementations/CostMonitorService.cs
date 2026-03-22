using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 실시간 비용/리소스 모니터링
    /// - API 비용: Gateway 이벤트의 usage 필드에서 추출
    /// - CPU/RAM: System.Diagnostics.Process에서 주기적 샘플링
    /// - 임계값 초과 시 경고 이벤트 발행
    /// </summary>
    public class CostMonitorService : ICostMonitorService, IDisposable
    {
        private readonly ReactiveProperty<decimal> _sessionCost     = new(0m);
        private readonly ReactiveProperty<long>    _totalTokens     = new(0L);
        private readonly ReactiveProperty<long>    _savedTokens     = new(0L);
        private readonly ReactiveProperty<float>   _cpuUsage        = new(0f);
        private readonly ReactiveProperty<float>   _ramUsageMb      = new(0f);
        private readonly Subject<decimal>          _costAlert       = new();

        private decimal _alertThreshold = 10m; // 기본 $10

        public ReadOnlyReactiveProperty<decimal> CurrentSessionCost => _sessionCost;
        public ReadOnlyReactiveProperty<long>    TotalTokensUsed    => _totalTokens;
        public ReadOnlyReactiveProperty<long>    TokensSavedByCache => _savedTokens;
        public ReadOnlyReactiveProperty<float>   CpuUsage           => _cpuUsage;
        public ReadOnlyReactiveProperty<float>   RamUsageMb         => _ramUsageMb;
        public Observable<decimal>               OnCostAlert        => _costAlert;

        public void SetCostAlertThreshold(decimal threshold)
        {
            _alertThreshold = threshold;
            Debug.Log($"[CostMonitor] 비용 경고 임계값: ${threshold}");
        }

        public void ResetSession()
        {
            _sessionCost.Value = 0m;
            _totalTokens.Value = 0L;
            _savedTokens.Value = 0L;
        }

        /// <summary>외부에서 토큰 사용량 보고 (Bridge 이벤트에서 호출)</summary>
        public void ReportTokenUsage(long inputTokens, long outputTokens, decimal cost, long cachedTokens = 0)
        {
            _totalTokens.Value += inputTokens + outputTokens;
            _savedTokens.Value += cachedTokens;
            _sessionCost.Value += cost;

            // 임계값 초과 경고
            if (_sessionCost.Value >= _alertThreshold)
            {
                _costAlert.OnNext(_sessionCost.Value);
            }
        }

        public async UniTask StartMonitoringAsync(CancellationToken ct = default)
        {
            Debug.Log("[CostMonitor] 리소스 모니터링 시작");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SampleResources();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CostMonitor] 샘플링 오류: {ex.Message}");
                }

                await UniTask.Delay(2000, cancellationToken: ct); // 2초 간격
            }
        }

        private void SampleResources()
        {
            try
            {
                var process = Process.GetCurrentProcess();

                // RAM (Working Set → MB)
                _ramUsageMb.Value = process.WorkingSet64 / (1024f * 1024f);

                // CPU (총 프로세서 시간 기반 추정)
                var cpuTime = process.TotalProcessorTime.TotalMilliseconds;
                var upTime  = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
                var cpuCount = Environment.ProcessorCount;
                _cpuUsage.Value = (float)Math.Min(cpuTime / (upTime * cpuCount), 1.0);
            }
            catch
            {
                // 일부 플랫폼에서 Process 접근 실패 가능
            }
        }

        public void Dispose()
        {
            _sessionCost.Dispose();
            _totalTokens.Dispose();
            _savedTokens.Dispose();
            _cpuUsage.Dispose();
            _ramUsageMb.Dispose();
            _costAlert.Dispose();
        }
    }
}
