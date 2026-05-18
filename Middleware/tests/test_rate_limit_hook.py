"""RateLimitHook 의 429 분류 / retry-after 파싱 / 글로벌 cooldown 회귀 테스트."""

from __future__ import annotations

import time

import pytest

from hooks import RequestCtx
from hooks.builtin.rate_limit import (
    RateLimitHook,
    _extract_retry_after,
    _is_rate_limit,
    _parse_retry_after,
)


def _ctx(retry_count: int = 0) -> RequestCtx:
    return RequestCtx(
        request_id="r",
        agent_id="a",
        session_id="s",
        provider="anthropic_api",
        model="m",
        started_at=time.monotonic(),
        retry_count=retry_count,
    )


@pytest.fixture(autouse=True)
def _reset_cooldown():
    """매 테스트 후 글로벌 cooldown 리셋 (테스트 간 leak 방지)."""
    yield
    RateLimitHook.reset_global_cooldown()


@pytest.mark.unit
def test_parse_retry_after_seconds():
    assert _parse_retry_after("5") == 5.0
    assert _parse_retry_after("0") == 0.0
    assert _parse_retry_after("12.5") == 12.5


@pytest.mark.unit
def test_parse_retry_after_invalid():
    assert _parse_retry_after(None) is None
    assert _parse_retry_after("") is None
    assert _parse_retry_after("  ") is None
    assert _parse_retry_after("not-a-number") is None


@pytest.mark.unit
def test_parse_retry_after_negative_clamped_to_zero():
    """음수 retry-after 는 비현실 — 0 으로 클램프."""
    assert _parse_retry_after("-5") == 0.0


@pytest.mark.unit
def test_is_rate_limit_by_name():
    class RateLimitError(Exception):
        pass

    assert _is_rate_limit(RateLimitError()) is True


@pytest.mark.unit
def test_is_rate_limit_by_status_code():
    class HttpError(Exception):
        def __init__(self):
            self.status_code = 429

    assert _is_rate_limit(HttpError()) is True


@pytest.mark.unit
def test_is_rate_limit_negative():
    """다른 status 는 False."""
    class HttpError(Exception):
        def __init__(self):
            self.status_code = 500

    assert _is_rate_limit(HttpError()) is False
    assert _is_rate_limit(ValueError("foo")) is False


@pytest.mark.unit
def test_extract_retry_after_from_response_headers():
    """SDK 의 일반적 구조 — error.response.headers."""

    class _Headers:
        def __init__(self, d):
            self._d = d

        def get(self, k, default=None):
            return self._d.get(k, default)

    class _Response:
        def __init__(self):
            self.headers = _Headers({"retry-after": "8"})

    class RateLimitError(Exception):
        def __init__(self):
            self.response = _Response()

    assert _extract_retry_after(RateLimitError()) == 8.0


@pytest.mark.unit
def test_extract_retry_after_from_direct_headers():
    """error.headers 패턴도 지원."""

    class _Headers(dict):
        pass

    class HttpError(Exception):
        def __init__(self):
            self.headers = _Headers({"retry-after": "3"})

    assert _extract_retry_after(HttpError()) == 3.0


@pytest.mark.unit
def test_extract_retry_after_missing_returns_none():
    class RateLimitError(Exception):
        pass

    assert _extract_retry_after(RateLimitError()) is None


@pytest.mark.unit
@pytest.mark.asyncio
async def test_retry_action_with_explicit_retry_after():
    hook = RateLimitHook(default_backoff_seconds=5.0)
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class _Headers(dict):
        pass

    class RateLimitError(Exception):
        def __init__(self):
            self.headers = _Headers({"retry-after": "12"})

    action = await hook.on_error(ctx, RateLimitError())

    assert action is not None
    assert action.kind == "retry"
    assert action.backoff_seconds == 12.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_retry_action_falls_back_to_default():
    hook = RateLimitHook(default_backoff_seconds=7.5)
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class RateLimitError(Exception):
        pass

    action = await hook.on_error(ctx, RateLimitError())
    assert action.backoff_seconds == 7.5


@pytest.mark.unit
@pytest.mark.asyncio
async def test_backoff_capped():
    hook = RateLimitHook(max_backoff_seconds=20.0)
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class _Headers(dict):
        pass

    class RateLimitError(Exception):
        def __init__(self):
            self.headers = _Headers({"retry-after": "999"})

    action = await hook.on_error(ctx, RateLimitError())
    assert action.backoff_seconds == 20.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_global_cooldown_updated():
    """retry 결정 시 클래스 레벨 cooldown 이 갱신된다."""
    hook = RateLimitHook(default_backoff_seconds=10.0)
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class RateLimitError(Exception):
        pass

    before = RateLimitHook.global_cooldown_remaining()
    assert before == 0.0

    await hook.on_error(ctx, RateLimitError())

    after = RateLimitHook.global_cooldown_remaining()
    # 약 10s 남음.
    assert 9.0 <= after <= 10.1


@pytest.mark.unit
@pytest.mark.asyncio
async def test_global_cooldown_keeps_max_value():
    """여러 세션이 동시에 429 → 가장 긴 cooldown 이 승리."""
    hook_a = RateLimitHook(default_backoff_seconds=3.0)
    hook_b = RateLimitHook(default_backoff_seconds=15.0)

    class RateLimitError(Exception):
        pass

    ctx_a = _ctx()
    ctx_b = _ctx()
    await hook_a.on_pre_request(ctx_a)
    await hook_b.on_pre_request(ctx_b)

    await hook_a.on_error(ctx_a, RateLimitError())  # 3s.
    await hook_b.on_error(ctx_b, RateLimitError())  # 15s — 더 큼.

    remaining = RateLimitHook.global_cooldown_remaining()
    assert 14.0 <= remaining <= 15.1


@pytest.mark.unit
@pytest.mark.asyncio
async def test_escalate_when_retries_exhausted():
    hook = RateLimitHook(max_retries=3)
    ctx = _ctx(retry_count=3)
    await hook.on_pre_request(ctx)

    class RateLimitError(Exception):
        pass

    action = await hook.on_error(ctx, RateLimitError())
    assert action.kind == "escalate"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_none_for_non_rate_limit():
    """다른 에러는 None — RetryHook 등에 위임."""
    hook = RateLimitHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class APIConnectionError(Exception):
        pass

    action = await hook.on_error(ctx, APIConnectionError())
    assert action is None


@pytest.mark.unit
@pytest.mark.asyncio
async def test_hits_counter_increments():
    hook = RateLimitHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class RateLimitError(Exception):
        pass

    await hook.on_error(ctx, RateLimitError())
    await hook.on_error(ctx, RateLimitError())  # retry_count 는 ctx 만 보지만 hits 는 누적.

    assert ctx.metadata["rate_limit"]["hits"] == 2
