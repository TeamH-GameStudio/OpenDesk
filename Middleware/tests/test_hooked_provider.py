"""HookedProvider 스모크 테스트.

가짜 ProviderBase 가 scripted StreamEvent 시퀀스를 yield 할 때 HookedProvider 가
- 정확한 시점에 hook lifecycle 메서드를 호출하는지
- 이벤트 자체는 변경 없이 pass-through 하는지
- blocking hook 차단 시 error event 만 흘려보내는지
- inner 예외 시 hook 의 결정대로 처리하는지
검증한다.
"""

from __future__ import annotations

import time
from typing import AsyncIterator, Optional

import pytest

from hooks import BaseHook, ErrorAction, HookPipeline, RequestCtx, UsageSnapshot
from hooks.hooked_provider import HookedProvider
from providers.base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)


def _ctx() -> RequestCtx:
    return RequestCtx(
        request_id="req-1",
        agent_id="agent-1",
        session_id="sess-1",
        provider="fake",
        model="fake-model",
        started_at=time.monotonic(),
    )


class _ScriptedProvider(ProviderBase):
    """미리 정해놓은 StreamEvent 리스트를 그대로 yield 하는 fake provider."""

    name = "fake"

    def __init__(self, script: list[StreamEvent], raise_after: Optional[int] = None):
        self._script = script
        self._raise_after = raise_after

    async def check_available(self) -> tuple[bool, str]:
        return True, "fake"

    async def chat(self, **kw) -> None:
        # 사용 안 함 — run_stream 만 테스트.
        raise NotImplementedError

    async def run_stream(self, **kw) -> AsyncIterator[StreamEvent]:
        for idx, evt in enumerate(self._script):
            yield evt
            # raise_after=N → N 이벤트 yield 후 raise.
            if self._raise_after is not None and idx + 1 == self._raise_after:
                raise RuntimeError("scripted boom")


class _Recorder(BaseHook):
    """모든 lifecycle 호출을 events 에 기록 + 카운트."""

    def __init__(self, name: str = "recorder"):
        self.name = name
        self.events: list[str] = []
        self.first_token_ts: Optional[float] = None
        self.tool_rounds: list[int] = []
        self.final_text: Optional[str] = None
        self.errors: list[BaseException] = []

    async def on_pre_request(self, ctx):
        self.events.append("pre")
        return None

    async def on_first_token(self, ctx, ts):
        self.events.append("first_token")
        self.first_token_ts = ts

    async def on_tool_round_complete(self, ctx, round_idx, usage):
        self.events.append(f"round:{round_idx}")
        self.tool_rounds.append(round_idx)

    async def on_post_response(self, ctx, final_text, usage):
        self.events.append("post")
        self.final_text = final_text

    async def on_error(self, ctx, error):
        self.events.append("error")
        self.errors.append(error)
        return None


@pytest.mark.unit
@pytest.mark.asyncio
async def test_passes_through_all_stream_events():
    script: list[StreamEvent] = [
        TextDeltaEvent(text="Hello"),
        TextDeltaEvent(text=" world"),
        MessageStopEvent(reason="complete", accumulated_text="Hello world"),
    ]
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
    )

    collected: list[StreamEvent] = []
    async for evt in provider.run_stream(messages=[]):
        collected.append(evt)

    assert len(collected) == 3
    assert isinstance(collected[0], TextDeltaEvent) and collected[0].text == "Hello"
    assert isinstance(collected[1], TextDeltaEvent) and collected[1].text == " world"
    assert isinstance(collected[2], MessageStopEvent)
    assert collected[2].reason == "complete"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_calls_lifecycle_in_correct_order():
    script: list[StreamEvent] = [
        TextDeltaEvent(text="Hi"),
        ToolUseStartEvent(tool_use_id="t1", name="read_skill_body"),
        ToolUseResultEvent(tool_use_id="t1", name="read_skill_body", result="ok"),
        TextDeltaEvent(text=" more"),
        ToolUseStartEvent(tool_use_id="t2", name="read_skill_body"),
        ToolUseResultEvent(tool_use_id="t2", name="read_skill_body", result="ok2"),
        MessageStopEvent(reason="complete", accumulated_text="Hi more done"),
    ]
    recorder = _Recorder()
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=HookPipeline([recorder]),
        ctx_factory=_ctx,
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    # 순서: pre → 첫 텍스트일 때 first_token → 각 ToolUseResult 마다 round → 종료 시 post.
    assert recorder.events == [
        "pre",
        "first_token",
        "round:1",
        "round:2",
        "post",
    ]
    assert recorder.tool_rounds == [1, 2]
    assert recorder.final_text == "Hi more done"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_first_token_only_fires_once():
    """여러 TextDeltaEvent 가 있어도 첫 번째만 on_first_token 트리거."""
    script: list[StreamEvent] = [
        TextDeltaEvent(text="A"),
        TextDeltaEvent(text="B"),
        TextDeltaEvent(text="C"),
        MessageStopEvent(reason="complete", accumulated_text="ABC"),
    ]
    recorder = _Recorder()
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=HookPipeline([recorder]),
        ctx_factory=_ctx,
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    # first_token 은 정확히 1회.
    assert recorder.events.count("first_token") == 1


@pytest.mark.unit
@pytest.mark.asyncio
async def test_blocking_hook_yields_error_event_only():
    """blocking hook 이 None 반환 시 inner 호출 없이 error MessageStop 1개만 yield."""

    class _Blocker(BaseHook):
        name = "blocker"

        async def on_pre_request(self, ctx):
            return None

    inner_called = {"called": False}

    class _SpyProvider(_ScriptedProvider):
        async def run_stream(self, **kw):
            inner_called["called"] = True
            for evt in self._script:
                yield evt

    provider = HookedProvider(
        inner=_SpyProvider([TextDeltaEvent(text="should not appear")]),
        pipeline=HookPipeline([_Blocker()], blocking_hook_names={"blocker"}),
        ctx_factory=_ctx,
    )

    collected = []
    async for evt in provider.run_stream(messages=[]):
        collected.append(evt)

    assert len(collected) == 1
    assert isinstance(collected[0], MessageStopEvent)
    assert collected[0].reason == "error"
    assert collected[0].error_code == "hook_blocked"
    assert inner_called["called"] is False


@pytest.mark.unit
@pytest.mark.asyncio
async def test_error_kind_suppress_does_not_raise():
    """on_error 만장일치 suppress 시 예외를 raise 하지 않고 error event yield."""

    class _Suppressor(BaseHook):
        name = "s"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="suppress", reason="testing")

    script: list[StreamEvent] = [
        TextDeltaEvent(text="partial"),
    ]
    provider = HookedProvider(
        inner=_ScriptedProvider(script, raise_after=1),
        pipeline=HookPipeline([_Suppressor()]),
        ctx_factory=_ctx,
    )

    collected = []
    async for evt in provider.run_stream(messages=[]):
        collected.append(evt)

    # 첫 텍스트 + suppress 후 error MessageStop = 2개.
    assert len(collected) == 2
    assert isinstance(collected[0], TextDeltaEvent)
    assert isinstance(collected[1], MessageStopEvent)
    assert collected[1].reason == "error"
    assert collected[1].error_code == "hook_suppressed"
    # partial text 가 보존됨.
    assert collected[1].accumulated_text == "partial"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_error_kind_escalate_raises():
    """on_error 가 escalate 면 예외가 그대로 raise 된다."""

    class _Escalator(BaseHook):
        name = "e"

        async def on_error(self, ctx, error):
            return ErrorAction(kind="escalate")

    provider = HookedProvider(
        inner=_ScriptedProvider([TextDeltaEvent(text="x")], raise_after=1),
        pipeline=HookPipeline([_Escalator()]),
        ctx_factory=_ctx,
    )

    with pytest.raises(RuntimeError, match="scripted boom"):
        async for _ in provider.run_stream(messages=[]):
            pass


@pytest.mark.unit
@pytest.mark.asyncio
async def test_no_post_call_on_error_stop():
    """MessageStopEvent.reason='error' 인 경우 on_post_response 호출 안 함."""
    script: list[StreamEvent] = [
        MessageStopEvent(reason="error", accumulated_text="", error_message="boom"),
    ]
    recorder = _Recorder()
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=HookPipeline([recorder]),
        ctx_factory=_ctx,
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    # pre 는 호출되지만 post 는 호출되지 않아야 한다.
    assert "pre" in recorder.events
    assert "post" not in recorder.events


@pytest.mark.unit
@pytest.mark.asyncio
async def test_usage_propagates_to_ctx():
    """MessageStopEvent.usage 가 있으면 ctx.cumulative_usage 에 반영되고 post hook 이 받는다."""
    snap = UsageSnapshot(
        input_tokens=100,
        output_tokens=50,
        cache_creation_input_tokens=80,
        cache_read_input_tokens=20,
    )
    script: list[StreamEvent] = [
        TextDeltaEvent(text="hi"),
        MessageStopEvent(reason="complete", accumulated_text="hi", usage=snap),
    ]

    received_usage = {}

    class _UsageCapture(BaseHook):
        name = "uc"

        async def on_post_response(self, ctx, final_text, usage):
            received_usage["snap"] = usage
            received_usage["ctx_usage"] = ctx.cumulative_usage

    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=HookPipeline([_UsageCapture()]),
        ctx_factory=_ctx,
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    assert received_usage["snap"] == snap
    assert received_usage["ctx_usage"] == snap


@pytest.mark.unit
@pytest.mark.asyncio
async def test_inner_check_available_passthrough():
    inner = _ScriptedProvider([])
    provider = HookedProvider(
        inner=inner,
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
    )
    ok, msg = await provider.check_available()
    assert ok is True
    assert msg == "fake"


@pytest.mark.unit
def test_name_inherited_from_inner():
    inner = _ScriptedProvider([])
    provider = HookedProvider(
        inner=inner,
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
    )
    assert provider.name == "fake"


@pytest.mark.unit
def test_resolve_max_rounds_no_cache_data():
    """ctx.metadata['cache'] 비어있으면 base rounds 그대로."""
    provider = HookedProvider(
        inner=_ScriptedProvider([]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=8,
        cache_bonus_rounds=4,
        cache_hit_bonus_threshold=0.6,
    )
    ctx = _ctx()
    assert provider._resolve_max_rounds(ctx) == 8


@pytest.mark.unit
def test_resolve_max_rounds_low_hit_ratio_no_bonus():
    provider = HookedProvider(
        inner=_ScriptedProvider([]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=8,
        cache_bonus_rounds=4,
        cache_hit_bonus_threshold=0.6,
    )
    ctx = _ctx()
    ctx.metadata["cache"] = {"available": True, "hit_ratio": 0.3}
    assert provider._resolve_max_rounds(ctx) == 8


@pytest.mark.unit
def test_resolve_max_rounds_high_hit_ratio_grants_bonus():
    provider = HookedProvider(
        inner=_ScriptedProvider([]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=8,
        cache_bonus_rounds=4,
        cache_hit_bonus_threshold=0.6,
    )
    ctx = _ctx()
    ctx.metadata["cache"] = {"available": True, "hit_ratio": 0.85}
    assert provider._resolve_max_rounds(ctx) == 12


@pytest.mark.unit
def test_resolve_max_rounds_unavailable_cache_no_bonus():
    """available=False (CLI 등) 면 bonus 안 줌 — 통계가 신뢰 못함."""
    provider = HookedProvider(
        inner=_ScriptedProvider([]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=8,
        cache_bonus_rounds=4,
    )
    ctx = _ctx()
    ctx.metadata["cache"] = {"available": False, "hit_ratio": 0.9}
    assert provider._resolve_max_rounds(ctx) == 8


@pytest.mark.unit
@pytest.mark.asyncio
async def test_max_tool_rounds_passed_to_inner_when_supported():
    """inner.run_stream 이 max_tool_rounds kwarg 를 받으면 주입된다."""
    captured = {}

    class _KwargCapture(_ScriptedProvider):
        async def run_stream(self, **kw):
            captured.update(kw)
            for evt in self._script:
                yield evt

    provider = HookedProvider(
        inner=_KwargCapture([MessageStopEvent(reason="complete")]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=5,
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    assert captured.get("max_tool_rounds") == 5


@pytest.mark.unit
@pytest.mark.asyncio
async def test_inner_without_max_tool_rounds_still_works():
    """inner 가 max_tool_rounds 를 모르면 TypeError 회피 후 정상 동작."""

    class _LegacyProvider(_ScriptedProvider):
        async def run_stream(self, *, messages, system_prompt="", mcp_config=None,
                             model="", in_process_tools=None):
            # max_tool_rounds 안 받음 (구버전 provider).
            for evt in self._script:
                yield evt

    provider = HookedProvider(
        inner=_LegacyProvider([MessageStopEvent(reason="complete")]),
        pipeline=HookPipeline([]),
        ctx_factory=_ctx,
        base_max_tool_rounds=5,
    )

    collected = []
    async for evt in provider.run_stream(messages=[]):
        collected.append(evt)
    assert len(collected) == 1
