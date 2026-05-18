# Middleware Hook Chain

OpenDesk 미들웨어의 lifecycle hook 시스템. 신뢰성 + 관측가능성을 위해 hook chain 도입.
PROTOCOL.md 의 telemetry v1 스키마와 함께 사용.

## 구성요소

```
ChatSession ──► HookPipeline ──► [RetryHook, RateLimitHook, LatencyHook,
                                  CacheStatHook, TelemetryEmitterHook]
                       │
                       ▼
              HookedProvider (decorator)
                       │
                ┌──────┴──────┐
                ▼             ▼
         AnthropicApi     AnthropicCli
         Provider         Provider
```

| 파일 | 역할 |
|------|------|
| `protocol.py` | `MessageHook` Protocol + `RequestCtx` / `UsageSnapshot` / `ErrorAction` dataclasses |
| `base.py` | `BaseHook` no-op mixin (새 hook 의 부모) |
| `pipeline.py` | `HookPipeline` — lifecycle 실행기 (forward 순서, 에러 격리) |
| `hooked_provider.py` | `HookedProvider` — ProviderBase 데코레이터 + dynamic max_tool_rounds |
| `builders.py` | `build_default_pipeline(config, send_fn)` |
| `builtin/latency.py` | TTFT / 라운드별 / 총 시간 측정 |
| `builtin/cache_stats.py` | Anthropic cache hit ratio 계산 |
| `builtin/retry.py` | 5xx / connection error 재시도 (jittered exp backoff) |
| `builtin/rate_limit.py` | 429 처리 + 글로벌 cooldown |
| `builtin/telemetry_emitter.py` | WS 'telemetry' 이벤트 발행 |

## 새 hook 추가

```python
from hooks import BaseHook, RequestCtx, UsageSnapshot

class MyHook(BaseHook):
    name = "my_hook"

    async def on_pre_request(self, ctx: RequestCtx):
        ctx.metadata.setdefault(self.name, {})
        return None  # ctx 변경 없음.

    async def on_post_response(self, ctx, final_text, usage):
        # ... metric 계산
        pass
```

`build_default_pipeline` 에 등록하려면 `builders._BUILDERS` 에 추가 + `config.json` 의
`hooks.chain` 에 이름 추가.

## Feature Flag (롤백)

`config.json`:
```json
{
  "hooks": {
    "enabled": false  // ← false 시 hook chain 우회, raw provider.chat() 폴백
  }
}
```

미들웨어 재시작 → hook 무관 raw 경로로 즉시 복귀. C# 측 클라이언트는 telemetry
이벤트가 안 와도 unknown type 으로 무시하므로 안전.

## 디자인 결정 (회귀 가드)

- **모든 lifecycle 메서드 forward 순서** — TelemetryEmitter 가 마지막에 등록되어
  다른 hook 의 metadata 를 읽고 emit. ("post 만 reverse" 시도했으나 first_token /
  tool_round_complete 도 동일하게 emitter 가 마지막에 실행돼야 정합성 — 통일.)
- **state 는 `ctx.metadata[hook.name]` 네임스페이스** — 글로벌 mutable state 금지.
- **에러 격리** — 한 hook 실패가 체인 멈추지 않음. 모든 호출이 try/except 격리.
- **에러 결정 투표** — retry/escalate 는 첫 번째 반환이 승리. suppress 는 만장일치.
- **JsonUtility nested null fragility** — telemetry 페이로드는 항상 빈 `{}` /0 으로
  채워 emit. Unity 의 `JsonUtility.FromJson` 이 null nested object 에 약함.
- **CLI provider 의 telemetry_completeness=partial** — cache 통계가 SDK 만큼 신뢰
  가능하지 않을 수 있어 UI 가 grey-out 표시 가능.

## 측정 메트릭

- TTFT (Time To First Token) — `latency.ttft_ms`
- 총 응답 시간 — `latency.total_ms`
- Round 별 시간 — `latency.tool_rounds_ms`
- Cache hit ratio — `cache.hit_ratio`
- Retry 횟수 — `reliability.retry_count`
- Rate limit hits — `reliability.rate_limit_hits`

상세 schema 는 `Middleware/PROTOCOL.md` 참조.
