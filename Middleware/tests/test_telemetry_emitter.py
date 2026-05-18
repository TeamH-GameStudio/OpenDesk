"""TelemetryEmitterHook 의 페이로드 스키마 / null 회피 / 격리 검증."""

from __future__ import annotations

import time
from typing import Any

import pytest

from hooks import HookPipeline, RequestCtx, UsageSnapshot
from hooks.builtin.cache_stats import CacheStatHook
from hooks.builtin.latency import LatencyHook
from hooks.builtin.telemetry_emitter import TelemetryEmitterHook


def _ctx() -> RequestCtx:
    return RequestCtx(
        request_id="req-1",
        agent_id="agent-x",
        session_id="sess-1",
        provider="anthropic_api",
        model="claude-sonnet-4-5",
        started_at=time.monotonic(),
    )


class _Collector:
    """send_fn 더미 — 호출된 payload 들을 수집."""
    def __init__(self):
        self.events: list[dict[str, Any]] = []

    async def __call__(self, payload: dict[str, Any]) -> None:
        self.events.append(payload)


_REQUIRED_FIELDS = {
    "type", "event", "request_id", "agent_id", "session_id",
    "provider", "model", "timestamp",
    "latency", "tokens", "cache", "reliability",
    "cost_estimate_usd", "telemetry_completeness",
    "has_error", "error",
}


@pytest.mark.unit
@pytest.mark.asyncio
async def test_emits_required_fields_on_post():
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector)

    ctx = _ctx()
    await hook.on_post_response(ctx, "ok", UsageSnapshot())

    assert len(collector.events) == 1
    payload = collector.events[0]
    missing = _REQUIRED_FIELDS - set(payload.keys())
    assert not missing, f"missing fields: {missing}"
    assert payload["type"] == "telemetry"
    assert payload["event"] == "request_complete"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_no_null_in_nested_objects():
    """JsonUtility 가 nested null 에 fragile — error 등은 빈 {} 로 보내야 한다."""
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector)

    await hook.on_post_response(_ctx(), "ok", UsageSnapshot())

    payload = collector.events[0]
    # error 는 빈 dict (null 아님).
    assert payload["error"] == {}
    assert isinstance(payload["latency"], dict)
    assert isinstance(payload["tokens"], dict)
    assert isinstance(payload["cache"], dict)
    assert isinstance(payload["reliability"], dict)


@pytest.mark.unit
@pytest.mark.asyncio
async def test_error_event_includes_error_object():
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector)

    class MyError(RuntimeError):
        pass

    await hook.on_error(_ctx(), MyError("boom"))

    assert len(collector.events) == 1
    payload = collector.events[0]
    assert payload["event"] == "error"
    assert payload["has_error"] is True
    assert payload["error"]["message"] == "boom"
    assert payload["error"]["code"] == "MyError"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_first_token_emits_partial_event():
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector)

    ctx = _ctx()
    # latency metadata 를 가상으로 채움 (LatencyHook 이 했다고 가정).
    ctx.metadata["latency"] = {"ttft_ms": 421}

    await hook.on_first_token(ctx, ts=time.monotonic())

    assert len(collector.events) == 1
    payload = collector.events[0]
    assert payload["event"] == "first_token"
    assert payload["latency"]["ttft_ms"] == 421


@pytest.mark.unit
@pytest.mark.asyncio
async def test_send_failure_isolated():
    """send_fn 이 예외 던져도 hook 자체는 raise 하지 않음 (telemetry 가 본 흐름 막지 않도록)."""

    async def boom(_payload):
        raise RuntimeError("ws closed")

    hook = TelemetryEmitterHook(send_fn=boom)
    # 예외 없이 통과해야 함.
    await hook.on_post_response(_ctx(), "ok", UsageSnapshot())


@pytest.mark.unit
@pytest.mark.asyncio
async def test_aggregates_other_hooks_metadata(monkeypatch):
    """LatencyHook + CacheStatHook 와 함께 작동 시 둘의 metadata 가 payload 에 반영됨."""
    collector = _Collector()
    latency = LatencyHook()
    cache = CacheStatHook()
    emitter = TelemetryEmitterHook(send_fn=collector)
    pipeline = HookPipeline([latency, cache, emitter])

    ctx = _ctx()

    await pipeline.run_pre(ctx)
    await pipeline.run_first_token(ctx, ts=ctx.started_at + 0.123)  # 123ms TTFT.

    current = {"t": ctx.started_at + 1.0}
    monkeypatch.setattr(
        "hooks.builtin.latency.time.monotonic",
        lambda: current["t"],
    )

    usage = UsageSnapshot(
        input_tokens=50,
        output_tokens=20,
        cache_creation_input_tokens=200,
        cache_read_input_tokens=300,
    )
    await pipeline.run_post(ctx, "all good", usage)

    # post 후 emitter 가 1회 호출 (+ first_token 시 1회 = 총 2).
    assert len(collector.events) == 2
    complete = collector.events[-1]  # request_complete.
    assert complete["event"] == "request_complete"
    assert complete["latency"]["ttft_ms"] == 123
    assert complete["latency"]["total_ms"] == 1000
    assert complete["tokens"]["input"] == 50
    assert complete["tokens"]["output"] == 20
    assert complete["tokens"]["cache_creation_input"] == 200
    assert complete["tokens"]["cache_read_input"] == 300
    assert complete["cache"]["read_tokens"] == 300
    assert complete["cache"]["creation_tokens"] == 200
    # hit_ratio = 300 / (50 + 200 + 300) = 0.5454...
    assert complete["cache"]["hit_ratio"] == pytest.approx(300 / 550, abs=0.0001)


@pytest.mark.unit
@pytest.mark.asyncio
async def test_telemetry_completeness_flag_passes_through():
    """provider 가 'partial' 로 마킹하면 payload 에도 그대로 반영."""
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector, telemetry_completeness="partial")

    await hook.on_post_response(_ctx(), "ok", UsageSnapshot(available=False))

    payload = collector.events[0]
    assert payload["telemetry_completeness"] == "partial"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_cost_estimator_invoked():
    collector = _Collector()

    def estimator(model: str, u: UsageSnapshot) -> float:
        return 0.0123 if model == "claude-sonnet-4-5" else 0.0

    hook = TelemetryEmitterHook(send_fn=collector, cost_estimator=estimator)

    await hook.on_post_response(_ctx(), "ok", UsageSnapshot(input_tokens=1000))

    payload = collector.events[0]
    assert payload["cost_estimate_usd"] == pytest.approx(0.0123)


@pytest.mark.unit
@pytest.mark.asyncio
async def test_cost_estimator_failure_safe():
    """cost_estimator 가 예외 던져도 payload 는 0 으로 안전 출고."""
    collector = _Collector()

    def boom(_model: str, _u: UsageSnapshot) -> float:
        raise ValueError("bad pricing data")

    hook = TelemetryEmitterHook(send_fn=collector, cost_estimator=boom)

    await hook.on_post_response(_ctx(), "ok", UsageSnapshot())

    assert collector.events[0]["cost_estimate_usd"] == 0.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_tool_rounds_ms_list_preserved():
    """latency.tool_rounds_ms 가 그대로 list 로 emit."""
    collector = _Collector()
    hook = TelemetryEmitterHook(send_fn=collector)
    ctx = _ctx()
    ctx.metadata["latency"] = {
        "ttft_ms": 100,
        "total_ms": 5000,
        "tool_rounds_ms": [400, 2200, 2400],
    }

    await hook.on_post_response(ctx, "ok", UsageSnapshot())

    payload = collector.events[0]
    assert payload["latency"]["tool_rounds_ms"] == [400, 2200, 2400]
