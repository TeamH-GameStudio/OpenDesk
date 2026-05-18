using OpenDesk.Claude.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 미들웨어 hook chain 의 telemetry 이벤트를 수신하여 UI 가 구독 가능한 reactive
    /// property 로 노출. ClaudeWebSocketClient.OnTelemetry 의 단일 진실 소스.
    ///
    /// 사용 예 — CostHudController:
    ///   _tel.LastTtftMs.Subscribe(ms => _ttftLabel.SetText($"{ms} ms"));
    ///   _tel.LastCacheHitRatio.Subscribe(r => _cacheBar.value = r);
    /// </summary>
    public interface IAgentTelemetryService
    {
        /// <summary>마지막 request 의 TTFT (ms). first_token 이벤트 도착 시 즉시 갱신.</summary>
        ReadOnlyReactiveProperty<int> LastTtftMs { get; }

        /// <summary>마지막 request 의 총 시간 (ms). request_complete 시 갱신.</summary>
        ReadOnlyReactiveProperty<int> LastTotalMs { get; }

        /// <summary>최근 cache hit ratio (0.0 ~ 1.0). 통계 불가 (partial) 일 때 0 + Available=false.</summary>
        ReadOnlyReactiveProperty<float> LastCacheHitRatio { get; }

        /// <summary>최근 retry 발생 수 (1 회 요청 당). 0 이 정상.</summary>
        ReadOnlyReactiveProperty<int> LastRetryCount { get; }

        /// <summary>마지막 request 가 429 를 맞은 횟수.</summary>
        ReadOnlyReactiveProperty<int> LastRateLimitHits { get; }

        /// <summary>true = full telemetry 가능 (anthropic_api). false = partial (CLI 등).</summary>
        ReadOnlyReactiveProperty<bool> TelemetryAvailable { get; }

        /// <summary>실시간 telemetry 이벤트 스트림 (raw). 상세 가공이 필요한 컴포넌트용.</summary>
        Observable<TelemetryEvent> Events { get; }

        /// <summary>ClaudeWebSocketClient.OnTelemetry 에서 호출. 외부 사용자는 보통 직접 호출하지 않음.</summary>
        void Ingest(TelemetryEvent evt);
    }
}
