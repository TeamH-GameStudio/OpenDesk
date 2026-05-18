"""Hook chain 통합 테스트 — HookedProvider + 모든 builtin hook + 가짜 provider.

scripted StreamEvent 시퀀스를 흘려보내고 최종 telemetry 페이로드에 latency / cache /
reliability 측정값이 모두 들어가는지 검증. Anthropic SDK 없이도 전체 파이프라인이
동작함을 보장.
"""

from __future__ import annotations

import time
from typing import Any, AsyncIterator

import pytest

from hooks import (
    BaseHook,
    ErrorAction,
    HookPipeline,
    RequestCtx,
    UsageSnapshot,
)
from hooks.builders import build_default_pipeline
from hooks.hooked_provider import HookedProvider
from providers.base import (
    MessageStopEvent,
    ProviderBase,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)


def _ctx_factory_for(provider_name: str, model: str):
    def _factory() -> RequestCtx:
        return RequestCtx(
            request_id="req-int-1",
            agent_id="agent-i",
            session_id="sess-i",
            provider=provider_name,
            model=model,
            started_at=time.monotonic(),
        )
    return _factory


class _ScriptedProvider(ProviderBase):
    name = "fake"

    def __init__(self, script: list[StreamEvent]):
        self._script = script

    async def check_available(self) -> tuple[bool, str]:
        return True, "fake"

    async def chat(self, **kw) -> None:
        raise NotImplementedError

    async def run_stream(self, **kw) -> AsyncIterator[StreamEvent]:
        for evt in self._script:
            yield evt


@pytest.mark.unit
@pytest.mark.asyncio
async def test_happy_path_telemetry_complete():
    """정상 시나리오 — latency / cache / reliability 모두 telemetry 페이로드에 합쳐진다."""
    sent_events: list[dict[str, Any]] = []

    async def collector(payload):
        sent_events.append(payload)

    pipeline = build_default_pipeline(
        config={
            "hooks": {
                "chain": ["retry", "rate_limit", "latency", "cache_stats", "telemetry_emitter"],
            },
        },
        send_fn=collector,
        telemetry_completeness="full",
    )

    final_usage = UsageSnapshot(
        input_tokens=100,
        output_tokens=40,
        cache_creation_input_tokens=600,
        cache_read_input_tokens=300,
    )
    script: list[StreamEvent] = [
        TextDeltaEvent(text="안녕"),
        TextDeltaEvent(text="하세요"),
        ToolUseStartEvent(tool_use_id="t1", name="read_skill_body"),
        ToolUseResultEvent(tool_use_id="t1", name="read_skill_body", result="ok"),
        TextDeltaEvent(text=" 추가"),
        MessageStopEvent(reason="complete", accumulated_text="안녕하세요 추가", usage=final_usage),
    ]
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=pipeline,
        ctx_factory=_ctx_factory_for("anthropic_api", "claude-sonnet-4-5"),
    )

    collected_stream: list[StreamEvent] = []
    async for evt in provider.run_stream(messages=[]):
        collected_stream.append(evt)

    # 1) stream pass-through — 모든 이벤트가 그대로 흘러나옴.
    assert len(collected_stream) == 6

    # 2) telemetry — first_token 1회 + request_complete 1회 = 최소 2회.
    assert len(sent_events) >= 2
    first_token = [e for e in sent_events if e["event"] == "first_token"]
    complete = [e for e in sent_events if e["event"] == "request_complete"]
    assert len(first_token) == 1
    assert len(complete) == 1

    final = complete[0]

    # 3) latency 필드 — TTFT, total_ms 가 양수.
    assert final["latency"]["ttft_ms"] >= 0
    assert final["latency"]["total_ms"] >= 0
    # tool_rounds_ms 는 ToolUseResult 1 회로 1개 entry.
    assert len(final["latency"]["tool_rounds_ms"]) == 1

    # 4) cache 필드 — hit_ratio 계산됨.
    cache = final["cache"]
    assert cache["available"] is True
    # 분모 = read + creation + non_cached_input = 300 + 600 + 100 = 1000.
    # read = 300 → ratio = 0.3.
    assert cache["hit_ratio"] == pytest.approx(0.3, abs=0.0001)
    assert cache["creation_tokens"] == 600
    assert cache["read_tokens"] == 300

    # 5) tokens 필드.
    assert final["tokens"]["input"] == 100
    assert final["tokens"]["output"] == 40
    assert final["tokens"]["cache_read_input"] == 300

    # 6) reliability — retry/rate_limit 0.
    rel = final["reliability"]
    assert rel["retry_count"] == 0
    assert rel["rate_limit_hits"] == 0
    assert rel["stop_reason"] == "complete"
    assert rel["tool_rounds_used"] == 1

    # 7) 에러 없음.
    assert final["has_error"] is False
    assert final["error"] == {}
    assert final["telemetry_completeness"] == "full"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_error_path_emits_error_telemetry():
    """inner provider 가 MessageStopEvent(reason='error') 를 yield 한 경우."""
    sent_events: list[dict[str, Any]] = []

    async def collector(payload):
        sent_events.append(payload)

    pipeline = build_default_pipeline(
        config={"hooks": {"chain": ["latency", "cache_stats", "telemetry_emitter"]}},
        send_fn=collector,
    )

    script: list[StreamEvent] = [
        TextDeltaEvent(text="partial"),
        MessageStopEvent(
            reason="error",
            accumulated_text="partial",
            error_message="boom",
            error_code="upstream_error",
        ),
    ]
    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=pipeline,
        ctx_factory=_ctx_factory_for("anthropic_api", "claude-sonnet-4-5"),
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    # request_complete 는 발행되지 않아야 함 (정상 종료 아님).
    complete = [e for e in sent_events if e["event"] == "request_complete"]
    assert complete == []
    # first_token 은 발행되어야 함 (텍스트가 한 번 흘렀음).
    first_token = [e for e in sent_events if e["event"] == "first_token"]
    assert len(first_token) == 1


@pytest.mark.unit
@pytest.mark.asyncio
async def test_partial_completeness_for_cli_provider():
    """provider=anthropic_cli 라면 emitter 가 telemetry_completeness=partial 로 emit."""
    sent_events: list[dict[str, Any]] = []

    async def collector(payload):
        sent_events.append(payload)

    pipeline = build_default_pipeline(
        config={},
        send_fn=collector,
        telemetry_completeness="partial",  # CLI 경로 설정.
    )

    provider = HookedProvider(
        inner=_ScriptedProvider([
            MessageStopEvent(reason="complete", accumulated_text="", usage=UsageSnapshot(available=False)),
        ]),
        pipeline=pipeline,
        ctx_factory=_ctx_factory_for("anthropic_cli", "claude-sonnet-4-5"),
    )

    async for _ in provider.run_stream(messages=[]):
        pass

    complete = [e for e in sent_events if e["event"] == "request_complete"]
    assert len(complete) == 1
    assert complete[0]["telemetry_completeness"] == "partial"
    assert complete[0]["cache"]["available"] is False


@pytest.mark.unit
@pytest.mark.asyncio
async def test_pipeline_disabled_when_no_send_fn():
    """send_fn 이 없으면 emitter 가 등록 안 되어 telemetry 발행 안 함 — 다른 hook 은 정상."""
    pipeline = build_default_pipeline({}, send_fn=None)
    # emitter 미등록 검증.
    assert "telemetry_emitter" not in [h.name for h in pipeline.hooks]

    provider = HookedProvider(
        inner=_ScriptedProvider([MessageStopEvent(reason="complete")]),
        pipeline=pipeline,
        ctx_factory=_ctx_factory_for("anthropic_api", "claude-sonnet-4-5"),
    )

    # 예외 없이 통과해야 함.
    async for _ in provider.run_stream(messages=[]):
        pass


@pytest.mark.unit
@pytest.mark.asyncio
async def test_hook_chain_overhead_bounded():
    """hook chain overhead 가 너무 크지 않은지 — sanity check (5ms × 5 hooks × 8 rounds = 200ms).

    벤치 한 번 — overhead 가 1초를 넘으면 무언가 잘못된 것.
    """
    sent_events: list[dict[str, Any]] = []

    async def collector(payload):
        sent_events.append(payload)

    pipeline = build_default_pipeline({}, send_fn=collector)

    script: list[StreamEvent] = [
        TextDeltaEvent(text=f"chunk{i}") for i in range(20)
    ] + [MessageStopEvent(reason="complete", accumulated_text="x" * 100)]

    provider = HookedProvider(
        inner=_ScriptedProvider(script),
        pipeline=pipeline,
        ctx_factory=_ctx_factory_for("anthropic_api", "claude-sonnet-4-5"),
    )

    started = time.monotonic()
    async for _ in provider.run_stream(messages=[]):
        pass
    elapsed = time.monotonic() - started

    # 가벼운 sanity check — 5ms × 20 deltas + post = 매우 빨라야 함.
    assert elapsed < 1.0, f"Hook overhead suspiciously high: {elapsed:.3f}s"
