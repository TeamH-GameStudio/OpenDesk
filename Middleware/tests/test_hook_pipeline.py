"""HookPipeline ordering / error isolation / error-action voting 회귀 테스트.

설계 의도를 결박하는 테스트들:
1. on_pre_request 순방향 / on_post_response 역방향
2. 한 hook 실패가 다른 hook 실행을 막지 않음 (격리)
3. on_error 투표: retry/escalate 우선 / suppress 만장일치 / 전부 None 시 escalate
4. blocking hook 의 None 만 차단으로 해석
"""

from __future__ import annotations

import time
from typing import Optional

import pytest

from hooks import BaseHook, ErrorAction, HookPipeline, RequestCtx, UsageSnapshot


def _make_ctx() -> RequestCtx:
    return RequestCtx(
        request_id="req-test",
        agent_id="agent-x",
        session_id="sess-1",
        provider="anthropic_api",
        model="claude-sonnet-4-5",
        started_at=time.monotonic(),
    )


class _RecordingHook(BaseHook):
    """모든 lifecycle 호출을 events 리스트에 기록하는 hook."""

    def __init__(self, name: str, events: list[str]):
        self.name = name
        self._events = events

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        self._events.append(f"pre:{self.name}")
        return None

    async def on_first_token(self, ctx: RequestCtx, ts: float) -> None:
        self._events.append(f"first:{self.name}")

    async def on_tool_round_complete(self, ctx, round_idx, usage) -> None:
        self._events.append(f"round:{self.name}:{round_idx}")

    async def on_post_response(self, ctx, final_text, usage) -> None:
        self._events.append(f"post:{self.name}")


@pytest.mark.unit
@pytest.mark.asyncio
async def test_run_pre_executes_in_registration_order():
    events: list[str] = []
    pipeline = HookPipeline([
        _RecordingHook("A", events),
        _RecordingHook("B", events),
        _RecordingHook("C", events),
    ])

    await pipeline.run_pre(_make_ctx())

    assert events == ["pre:A", "pre:B", "pre:C"]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_run_post_executes_in_registration_order():
    events: list[str] = []
    pipeline = HookPipeline([
        _RecordingHook("A", events),
        _RecordingHook("B", events),
        _RecordingHook("C", events),
    ])

    await pipeline.run_post(_make_ctx(), "final", UsageSnapshot())

    # post 도 등록 순방향 — TelemetryEmitter 가 마지막 등록되도록 호출자가 책임.
    assert events == ["post:A", "post:B", "post:C"]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_hook_failure_does_not_break_chain():
    """A 가 예외 던져도 B, C 가 계속 실행되어야 한다."""
    events: list[str] = []

    class _Boom(BaseHook):
        name = "boom"

        async def on_pre_request(self, ctx):
            events.append("pre:boom")
            raise RuntimeError("intentional")

    pipeline = HookPipeline([
        _Boom(),
        _RecordingHook("B", events),
        _RecordingHook("C", events),
    ])

    result = await pipeline.run_pre(_make_ctx())

    # 격리: boom 실패 후에도 B,C 실행됨. result 는 None 이 아님 (boom 은 blocking 아님).
    assert events == ["pre:boom", "pre:B", "pre:C"]
    assert result is not None


@pytest.mark.unit
@pytest.mark.asyncio
async def test_blocking_hook_none_aborts_request():
    """blocking_hook_names 에 등록된 hook 이 None 반환 시 ctx=None."""
    events: list[str] = []

    class _Block(BaseHook):
        name = "blocker"

        async def on_pre_request(self, ctx):
            events.append("pre:blocker")
            return None

    pipeline = HookPipeline(
        [_Block(), _RecordingHook("after", events)],
        blocking_hook_names={"blocker"},
    )

    result = await pipeline.run_pre(_make_ctx())

    # 차단됨 — 'after' hook 은 실행되지 않음.
    assert result is None
    assert events == ["pre:blocker"]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_non_blocking_hook_none_means_no_change():
    """blocking 등록 안 된 hook 의 None 은 단순히 '변경 없음'."""
    events: list[str] = []

    class _Passthrough(BaseHook):
        name = "passthrough"

        async def on_pre_request(self, ctx):
            events.append("pre:passthrough")
            return None

    pipeline = HookPipeline([_Passthrough(), _RecordingHook("after", events)])
    ctx_before = _make_ctx()

    ctx_after = await pipeline.run_pre(ctx_before)

    # 비차단 hook 의 None — 후속 hook 도 실행되고, ctx 는 원본 유지.
    assert events == ["pre:passthrough", "pre:after"]
    assert ctx_after is ctx_before


@pytest.mark.unit
@pytest.mark.asyncio
async def test_on_error_retry_wins_over_later_suppress():
    """retry 가 첫 번째로 나오면 early return — 뒤 suppress 평가 안 함."""

    class _Retry(BaseHook):
        name = "r"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="retry", backoff_seconds=1.5, reason="r")

    class _Suppress(BaseHook):
        name = "s"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="suppress")

    pipeline = HookPipeline([_Retry(), _Suppress()])

    action = await pipeline.run_error(_make_ctx(), RuntimeError("boom"))

    assert action.kind == "retry"
    assert action.backoff_seconds == 1.5


@pytest.mark.unit
@pytest.mark.asyncio
async def test_on_error_escalate_wins_immediately():
    """escalate 도 retry 와 동일하게 즉시 승리."""

    class _Escalate(BaseHook):
        name = "e"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="escalate", reason="critical")

    class _Retry(BaseHook):
        name = "r"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="retry")

    pipeline = HookPipeline([_Escalate(), _Retry()])

    action = await pipeline.run_error(_make_ctx(), RuntimeError("boom"))

    assert action.kind == "escalate"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_on_error_unanimous_suppress():
    """retry/escalate 가 한 번도 안 나오고 suppress 가 있으면 suppress."""

    class _Suppress(BaseHook):
        name: str

        def __init__(self, name: str):
            self.name = name

        async def on_error(self, ctx, error):
            return ErrorAction(kind="suppress")

    pipeline = HookPipeline([_Suppress("a"), _Suppress("b")])

    action = await pipeline.run_error(_make_ctx(), RuntimeError("boom"))

    assert action.kind == "suppress"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_on_error_all_none_defaults_to_escalate():
    """아무 hook 도 action 반환 안 하면 기본 escalate."""

    class _Silent(BaseHook):
        name: str

        def __init__(self, name: str):
            self.name = name

        async def on_error(self, ctx, error):
            return None

    pipeline = HookPipeline([_Silent("a"), _Silent("b")])

    action = await pipeline.run_error(_make_ctx(), RuntimeError("boom"))

    assert action.kind == "escalate"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_on_error_hook_failure_isolated():
    """한 hook 이 on_error 에서 예외 던져도 다른 hook 의 결정은 살아남는다."""

    class _Boom(BaseHook):
        name = "boom"

        async def on_error(self, ctx, error):
            raise RuntimeError("hook itself failed")

    class _Retry(BaseHook):
        name = "r"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="retry", backoff_seconds=0.1)

    pipeline = HookPipeline([_Boom(), _Retry()])

    action = await pipeline.run_error(_make_ctx(), RuntimeError("boom"))

    assert action.kind == "retry"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_pre_hook_can_rewrite_ctx():
    """on_pre_request 가 반환한 RequestCtx 가 후속 hook 에 전달된다."""

    class _Rewriter(BaseHook):
        name = "rewriter"

        async def on_pre_request(self, ctx):
            # 새 ctx 객체로 model 변경
            return RequestCtx(
                request_id=ctx.request_id,
                agent_id=ctx.agent_id,
                session_id=ctx.session_id,
                provider=ctx.provider,
                model="claude-haiku-4-5",  # 라우팅된 모델
                started_at=ctx.started_at,
            )

    captured: dict = {}

    class _Capture(BaseHook):
        name = "capture"

        async def on_pre_request(self, ctx):
            captured["model"] = ctx.model
            return None

    pipeline = HookPipeline([_Rewriter(), _Capture()])

    result = await pipeline.run_pre(_make_ctx())

    assert result is not None
    assert result.model == "claude-haiku-4-5"
    assert captured["model"] == "claude-haiku-4-5"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_run_tool_round_complete_executes_in_order():
    events: list[str] = []
    pipeline = HookPipeline([
        _RecordingHook("A", events),
        _RecordingHook("B", events),
    ])

    await pipeline.run_tool_round_complete(_make_ctx(), 3, UsageSnapshot())

    assert events == ["round:A:3", "round:B:3"]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_run_first_token_isolates_failures():
    events: list[str] = []

    class _BoomFirstToken(BaseHook):
        name = "boom"

        async def on_first_token(self, ctx, ts):
            events.append("first:boom")
            raise RuntimeError("intentional")

    pipeline = HookPipeline([
        _BoomFirstToken(),
        _RecordingHook("good", events),
    ])

    await pipeline.run_first_token(_make_ctx(), time.monotonic())

    # boom 실패 후에도 good 의 on_first_token 실행됨.
    assert events == ["first:boom", "first:good"]


@pytest.mark.unit
def test_duplicate_hook_name_does_not_raise():
    """이름 중복은 경고만 (로그). 가벼운 위반."""

    class _H(BaseHook):
        name = "dup"

    # 예외 없이 생성되어야 함.
    pipeline = HookPipeline([_H(), _H()])
    assert len(pipeline.hooks) == 2
