"""CacheStatHook 의 누적 / hit_ratio 계산 / available 처리 회귀 테스트."""

from __future__ import annotations

import time

import pytest

from hooks import HookPipeline, RequestCtx, UsageSnapshot
from hooks.builtin.cache_stats import CacheStatHook


def _ctx() -> RequestCtx:
    return RequestCtx(
        request_id="r",
        agent_id="a",
        session_id="s",
        provider="anthropic_api",
        model="claude-sonnet-4-5",
        started_at=time.monotonic(),
    )


@pytest.mark.unit
@pytest.mark.asyncio
async def test_hit_ratio_from_single_round():
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    usage = UsageSnapshot(
        input_tokens=200,
        output_tokens=50,
        cache_creation_input_tokens=800,
        cache_read_input_tokens=0,
    )
    await hook.on_post_response(ctx, "ok", usage)

    data = ctx.metadata["cache"]
    # 첫 호출은 모두 cache creation. read=0 이므로 hit_ratio=0.
    assert data["creation_tokens"] == 800
    assert data["read_tokens"] == 0
    assert data["hit_ratio"] == 0.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_hit_ratio_on_cache_read():
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    usage = UsageSnapshot(
        input_tokens=100,
        output_tokens=50,
        cache_creation_input_tokens=0,
        cache_read_input_tokens=900,  # 대부분 캐시 hit.
    )
    await hook.on_post_response(ctx, "ok", usage)

    data = ctx.metadata["cache"]
    # 분모 = 900 + 0 + 100 = 1000, read=900, ratio=0.9.
    assert data["read_tokens"] == 900
    assert data["hit_ratio"] == 0.9


@pytest.mark.unit
@pytest.mark.asyncio
async def test_zero_division_safe():
    """모든 input 카운트가 0 이면 ratio=0 (NaN 금지)."""
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    usage = UsageSnapshot()  # 전부 0, available=True.
    await hook.on_post_response(ctx, "ok", usage)

    assert ctx.metadata["cache"]["hit_ratio"] == 0.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_available_false_when_provider_unsupported():
    """usage.available=False 면 cache.available 도 False 로 고정."""
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    usage = UsageSnapshot(available=False)  # CLI provider 등.
    await hook.on_post_response(ctx, "ok", usage)

    assert ctx.metadata["cache"]["available"] is False


@pytest.mark.unit
@pytest.mark.asyncio
async def test_accumulates_across_rounds():
    """tool round 가 여러 번이면 누적되어야 한다."""
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    # round 1: cache write.
    await hook.on_tool_round_complete(ctx, 1, UsageSnapshot(
        input_tokens=100,
        cache_creation_input_tokens=500,
        cache_read_input_tokens=0,
    ))
    # round 2: cache hit.
    await hook.on_tool_round_complete(ctx, 2, UsageSnapshot(
        input_tokens=50,
        cache_creation_input_tokens=0,
        cache_read_input_tokens=500,
    ))
    # 최종 post.
    await hook.on_post_response(ctx, "ok", UsageSnapshot(
        input_tokens=30,
        cache_creation_input_tokens=0,
        cache_read_input_tokens=500,
    ))

    data = ctx.metadata["cache"]
    assert data["creation_tokens"] == 500
    assert data["read_tokens"] == 1000  # 500 + 500.
    assert data["non_cached_input_tokens"] == 180  # 100 + 50 + 30.
    # 분모 = 500 + 1000 + 180 = 1680, read=1000, ratio ≈ 0.5952.
    assert data["hit_ratio"] == pytest.approx(0.5952, abs=0.0001)


@pytest.mark.unit
@pytest.mark.asyncio
async def test_partial_availability_keeps_false():
    """첫 라운드 available=False 면 이후 라운드가 available=True 여도 False 유지.

    한 라운드라도 통계 누락이면 ratio 자체가 불완전. 보수적으로 false 잠금.
    """
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    await hook.on_tool_round_complete(ctx, 1, UsageSnapshot(available=False))
    await hook.on_tool_round_complete(ctx, 2, UsageSnapshot(
        available=True,
        cache_read_input_tokens=100,
    ))

    assert ctx.metadata["cache"]["available"] is False


@pytest.mark.unit
@pytest.mark.asyncio
async def test_full_lifecycle_via_pipeline():
    """HookPipeline 으로 호출해도 정상 동작."""
    hook = CacheStatHook()
    pipeline = HookPipeline([hook])
    ctx = _ctx()

    await pipeline.run_pre(ctx)
    await pipeline.run_tool_round_complete(ctx, 1, UsageSnapshot(
        input_tokens=100,
        cache_creation_input_tokens=400,
    ))
    await pipeline.run_post(ctx, "ok", UsageSnapshot(
        input_tokens=10,
        cache_read_input_tokens=400,
    ))

    data = ctx.metadata["cache"]
    # 분모 = 400 + 400 + 110, read=400.
    assert data["read_tokens"] == 400
    assert data["creation_tokens"] == 400
    assert data["hit_ratio"] == pytest.approx(400 / 910, abs=0.0001)


@pytest.mark.unit
@pytest.mark.asyncio
async def test_input_tokens_field_consistency():
    """data['input_tokens'] = non_cached + creation + read (모든 input 합)."""
    hook = CacheStatHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    await hook.on_post_response(ctx, "ok", UsageSnapshot(
        input_tokens=200,
        cache_creation_input_tokens=300,
        cache_read_input_tokens=500,
    ))

    data = ctx.metadata["cache"]
    assert data["input_tokens"] == 1000  # 200 + 300 + 500.
