"""RetryHook 의 분류 / backoff 곡선 / 예산 회귀 테스트."""

from __future__ import annotations

import time

import pytest

from hooks import RequestCtx
from hooks.builtin.retry import RetryHook, _is_retriable


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


@pytest.mark.unit
def test_retriable_classification_by_name():
    class APIConnectionError(Exception):
        pass

    class APITimeoutError(Exception):
        pass

    class InternalServerError(Exception):
        pass

    assert _is_retriable(APIConnectionError()) is True
    assert _is_retriable(APITimeoutError()) is True
    assert _is_retriable(InternalServerError()) is True


@pytest.mark.unit
def test_non_retriable_4xx_errors():
    class BadRequestError(Exception):
        pass

    class AuthenticationError(Exception):
        pass

    assert _is_retriable(BadRequestError()) is False
    assert _is_retriable(AuthenticationError()) is False


@pytest.mark.unit
def test_rate_limit_error_not_retriable_here():
    """RateLimitError 는 RateLimitHook 의 영역 — RetryHook 은 패스."""

    class RateLimitError(Exception):
        pass

    assert _is_retriable(RateLimitError()) is False


@pytest.mark.unit
def test_status_code_5xx_retriable():
    class APIStatusError(Exception):
        def __init__(self):
            self.status_code = 503

    assert _is_retriable(APIStatusError()) is True


@pytest.mark.unit
def test_status_code_4xx_not_retriable():
    class APIStatusError(Exception):
        def __init__(self):
            self.status_code = 400

    assert _is_retriable(APIStatusError()) is False


@pytest.mark.unit
def test_connection_error_retriable():
    assert _is_retriable(ConnectionError("network")) is True
    assert _is_retriable(OSError("oops")) is True


@pytest.mark.unit
@pytest.mark.asyncio
async def test_retry_action_emitted_for_retriable_error():
    hook = RetryHook(max_retries=3, base_seconds=1.0)
    ctx = _ctx(retry_count=0)
    await hook.on_pre_request(ctx)

    class APIConnectionError(Exception):
        pass

    action = await hook.on_error(ctx, APIConnectionError("net"))

    assert action is not None
    assert action.kind == "retry"
    # base*2^0 = 1.0 ~ 1.5 (jitter).
    assert 1.0 <= action.backoff_seconds <= 1.5


@pytest.mark.unit
@pytest.mark.asyncio
async def test_backoff_grows_exponentially():
    hook = RetryHook(max_retries=5, base_seconds=1.0, jitter_max=0.0)

    class APIConnectionError(Exception):
        pass

    # retry_count 별 backoff 확인 (jitter=0 으로 결정적).
    for i, expected in enumerate([1.0, 2.0, 4.0, 8.0, 16.0]):
        ctx = _ctx(retry_count=i)
        await hook.on_pre_request(ctx)
        action = await hook.on_error(ctx, APIConnectionError())
        assert action is not None
        assert action.backoff_seconds == expected, f"i={i}"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_backoff_capped_at_max():
    hook = RetryHook(
        max_retries=12, base_seconds=1.0,
        max_backoff_seconds=5.0, jitter_max=0.0,
    )

    class APIConnectionError(Exception):
        pass

    # retry_count=9 (max=12 보다 작음) → 2^9=512s 가 max=5s 로 클램프.
    ctx = _ctx(retry_count=9)
    await hook.on_pre_request(ctx)
    action = await hook.on_error(ctx, APIConnectionError())
    assert action.kind == "retry"
    assert action.backoff_seconds == 5.0


@pytest.mark.unit
@pytest.mark.asyncio
async def test_escalate_when_retries_exhausted():
    hook = RetryHook(max_retries=3)
    ctx = _ctx(retry_count=3)  # 이미 3회 소모.
    await hook.on_pre_request(ctx)

    class APIConnectionError(Exception):
        pass

    action = await hook.on_error(ctx, APIConnectionError())

    assert action is not None
    assert action.kind == "escalate"
    assert "exhausted" in action.reason


@pytest.mark.unit
@pytest.mark.asyncio
async def test_none_returned_for_non_retriable():
    """RetryHook 는 비대상 에러에 None 을 돌려 다른 hook 에게 양보."""
    hook = RetryHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class BadRequestError(Exception):
        pass

    action = await hook.on_error(ctx, BadRequestError())
    assert action is None


@pytest.mark.unit
@pytest.mark.asyncio
async def test_attempt_recorded_in_metadata():
    hook = RetryHook()
    ctx = _ctx()
    await hook.on_pre_request(ctx)

    class APIConnectionError(Exception):
        pass

    await hook.on_error(ctx, APIConnectionError("net"))

    attempts = ctx.metadata["retry"]["attempts"]
    assert len(attempts) == 1
    assert attempts[0]["error_type"] == "APIConnectionError"
    assert attempts[0]["retry_count"] == 0
    assert "backoff_seconds" in attempts[0]
