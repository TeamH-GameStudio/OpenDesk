using System;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// IAgentTelemetryService 의 R3 기반 구현. Ingest 호출 시 nested telemetry 필드를
    /// 별도 ReactiveProperty 로 풀어 UI 가 부분만 구독할 수 있게 한다.
    ///
    /// 동시에 ICostMonitorService 가 등록되어 있으면 tokens / cache 통계를 forward 하여
    /// 기존 비용 추적과 single source of truth 를 유지.
    /// </summary>
    public class AgentTelemetryService : IAgentTelemetryService, IDisposable
    {
        private readonly ReactiveProperty<int>   _ttftMs           = new(0);
        private readonly ReactiveProperty<int>   _totalMs          = new(0);
        private readonly ReactiveProperty<float> _cacheHitRatio    = new(0f);
        private readonly ReactiveProperty<int>   _retryCount       = new(0);
        private readonly ReactiveProperty<int>   _rateLimitHits    = new(0);
        private readonly ReactiveProperty<bool>  _telemetryAvail   = new(true);
        private readonly Subject<TelemetryEvent> _events           = new();

        private readonly ICostMonitorService _costMonitor;

        public AgentTelemetryService(ICostMonitorService costMonitor = null)
        {
            _costMonitor = costMonitor;
        }

        public ReadOnlyReactiveProperty<int>   LastTtftMs         => _ttftMs;
        public ReadOnlyReactiveProperty<int>   LastTotalMs        => _totalMs;
        public ReadOnlyReactiveProperty<float> LastCacheHitRatio  => _cacheHitRatio;
        public ReadOnlyReactiveProperty<int>   LastRetryCount     => _retryCount;
        public ReadOnlyReactiveProperty<int>   LastRateLimitHits  => _rateLimitHits;
        public ReadOnlyReactiveProperty<bool>  TelemetryAvailable => _telemetryAvail;
        public Observable<TelemetryEvent>      Events             => _events;

        public void Ingest(TelemetryEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            try
            {
                _events.OnNext(evt);

                if (evt.latency != null)
                {
                    // first_token 이벤트는 ttft 만 의미 — total_ms 가 0 인 경우 갱신 안 함.
                    if (evt.latency.ttft_ms > 0)
                    {
                        _ttftMs.Value = evt.latency.ttft_ms;
                    }
                    if (evt.latency.total_ms > 0)
                    {
                        _totalMs.Value = evt.latency.total_ms;
                    }
                }

                if (evt.cache != null)
                {
                    _telemetryAvail.Value = evt.cache.available;
                    if (evt.cache.available)
                    {
                        _cacheHitRatio.Value = evt.cache.hit_ratio;
                    }
                }

                if (evt.reliability != null)
                {
                    _retryCount.Value    = evt.reliability.retry_count;
                    _rateLimitHits.Value = evt.reliability.rate_limit_hits;
                }

                // CostMonitor 와 single source of truth 유지 — tokens 와 cost 를 forward.
                if (_costMonitor is CostMonitorService impl && evt.tokens != null
                    && evt.@event == "request_complete")
                {
                    impl.ReportTokenUsage(
                        inputTokens:  evt.tokens.input + evt.tokens.cache_creation_input,
                        outputTokens: evt.tokens.output,
                        cost:         (decimal)evt.cost_estimate_usd,
                        cachedTokens: evt.tokens.cache_read_input
                    );
                }
            }
            catch (Exception ex)
            {
                // telemetry ingest 실패가 본 흐름 막지 않도록 격리.
                Debug.LogWarning($"[AgentTelemetry] ingest failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _ttftMs.Dispose();
            _totalMs.Dispose();
            _cacheHitRatio.Dispose();
            _retryCount.Dispose();
            _rateLimitHits.Dispose();
            _telemetryAvail.Dispose();
            _events.Dispose();
        }
    }
}
