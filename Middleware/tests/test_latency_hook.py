"""LatencyHook 의 TTFT / round / total 측정 정확도 회귀 테스트.

monotonic 의 흐름을 패치해 결정적 결과를 얻는다.
"""

from __future__ import annotations

import time
from typing import Optional

import pytest

from hooks import HookPipeline, RequestCtx, UsageSnapshot
from hooks.builtin.latency import LatencyHook


def _ctx(started_at: float) -> RequestCtx:
    return RequestCtx(
        request_id="r",
        agent_id="a",
        session_id="s",
        provider="p",
        model="m",
        started_at=started_at,
    )


@pytest.mark.unit
@pytest.mark.asyncio
async def test_ttft_recorded_on_first_token():
    hook = LatencyHook()
    ctx = _ctx(started_at=1000.0)
    await hook.on_pre_request(ctx)

    await hook.on_first_token(ctx, ts=1000.412)

    data = ctx.metadata["latency"]
    assert data["ttft_ms"] == 412
    assert data["first_token_mono"] == 1000.412


@pytest.mark.unit
@pytest.mark.asyncio
async def test_ttft_only_first_call_counts():
    """on_first_token 이 잘못 여러 번 호출돼도 첫 번째 값만 유지."""
    hook = LatencyHook()
    ctx = _ctx(started_at=1000.0)
    await hook.on_pre_request(ctx)

    await hook.on_first_token(ctx, ts=1000.412)
    await hook.on_first_token(ctx, ts=1005.0)  # 두 번째는 무시되어야 함.

    assert ctx.metadata["latency"]["ttft_ms"] == 412


@pytest.mark.unit
@pytest.mark.asyncio
async def test_tool_round_ms_uses_monotonic(monkeypatch):
    """tool_use_result 도착 시각을 monotonic 으로 측정하여 round 별 ms 산출."""
    hook = LatencyHook()
    ctx = _ctx(started_at=1000.0)
    await hook.on_pre_request(ctx)

    # 시퀀스 끝나면 마지막 값을 계속 반환 (pytest 내부 호출에도 안전).
    times = [1001.0, 1003.5, 1004.2]
    state = {"i": 0}

    def fake_mono():
        i = state["i"]
        state["i"] = min(i + 1, len(times) - 1)
        return times[i]

    monkeypatch.setattr("hooks.builtin.latency.time.monotonic", fake_mono)

    # round 1 종료 → t=1001.0 → started_at 부터 1000ms.
    await hook.on_tool_round_complete(ctx, 1, UsageSnapshot(available=False))
    # round 2 종료 → t=1003.5 → 이전 종료(1001.0)부터 2500ms.
    await hook.on_tool_round_complete(ctx, 2, UsageSnapshot(available=False))
    # round 3 종료 → t=1004.2 → 이전 종료(1003.5)부터 700ms.
    await hook.on_tool_round_complete(ctx, 3, UsageSnapshot(available=False))

    rounds = ctx.metadata["latency"]["tool_rounds_ms"]
    assert rounds == [1000, 2500, 700]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_total_ms_recorded_on_post(monkeypatch):
    hook = LatencyHook()
    ctx = _ctx(started_at=2000.0)
    await hook.on_pre_request(ctx)

    monkeypatch.setattr(
        "hooks.builtin.latency.time.monotonic",
        lambda: 2009.18,  # 9180ms 후 post.
    )
    await hook.on_post_response(ctx, "done", UsageSnapshot(available=False))

    assert ctx.metadata["latency"]["total_ms"] == 9180


@pytest.mark.unit
@pytest.mark.asyncio
async def test_ttft_clamped_to_zero_for_clock_skew():
    """first_token_ts 가 started_at 보다 빠른 비현실적 케이스 — 음수 대신 0."""
    hook = LatencyHook()
    ctx = _ctx(started_at=1000.5)
    await hook.on_pre_request(ctx)

    await hook.on_first_token(ctx, ts=1000.0)  # 더 빠른 시각.

    assert ctx.metadata["latency"]["ttft_ms"] == 0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_full_lifecycle_through_pipeline(monkeypatch):
    """HookPipeline 으로 호출했을 때 전체 lifecycle 이 정합적으로 기록되는지."""
    hook = LatencyHook()
    pipeline = HookPipeline([hook])

    ctx = _ctx(started_at=500.0)

    # pipeline 의 모든 lifecycle 통과.
    await pipeline.run_pre(ctx)
    await pipeline.run_first_token(ctx, ts=500.05)  # 50ms TTFT.

    # 단계별로 다른 시각을 반환하도록 (지정 가능) 패치.
    current = {"t": 501.5}
    monkeypatch.setattr(
        "hooks.builtin.latency.time.monotonic",
        lambda: current["t"],
    )
    await pipeline.run_tool_round_complete(ctx, 1, UsageSnapshot(available=False))

    current["t"] = 503.0
    await pipeline.run_post(ctx, "ok", UsageSnapshot(available=False))

    data = ctx.metadata["latency"]
    assert data["ttft_ms"] == 50
    assert data["tool_rounds_ms"] == [1500]
    # total = post 시각(503.0) - started_at(500.0) = 3000ms.
    assert data["total_ms"] == 3000


@pytest.mark.unit
@pytest.mark.asyncio
async def test_metadata_namespace_isolated():
    """다른 hook 이 metadata 의 다른 키를 사용해도 latency 가 덮어쓰지 않음."""
    hook = LatencyHook()
    ctx = _ctx(started_at=0.0)
    ctx.metadata["cache"] = {"hit_ratio": 0.5}  # 다른 hook 의 미리 채운 값.

    await hook.on_pre_request(ctx)

    assert "cache" in ctx.metadata
    assert ctx.metadata["cache"] == {"hit_ratio": 0.5}
    assert "latency" in ctx.metadata
