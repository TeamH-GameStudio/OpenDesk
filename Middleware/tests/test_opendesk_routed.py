"""OpenDeskRoutedProvider hybrid 동작 검증 — inner Anthropic SDK 를 Mock 해서 E2E flow 검증.

검증 시나리오:
    - 잔액 충분 → route → hold → stream → settle → balance 감소
    - 잔액 부족 → hold 실패 → on_error 호출, inner.chat 실행 안 됨
    - 스트림 중 예외 → refund → 잔액 원복
    - credit 이벤트 sink 가 모든 단계에 fire
"""

from __future__ import annotations

import asyncio
from typing import AsyncIterator, Optional
from unittest.mock import AsyncMock

import pytest

from hooks.protocol import UsageSnapshot
from mock_routing_server import reset_embedded_server
from providers.base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
)
from providers.opendesk_routed import OpenDeskRoutedProvider
from routing_client import RoutingClient, reset_routing_client


class _FakeInner(ProviderBase):
    """run_stream 만 사용하는 fake. text_delta 시퀀스 + final usage 를 미리 설정."""

    name = "fake_inner"

    def __init__(
        self,
        text_chunks: Optional[list[str]] = None,
        usage: Optional[UsageSnapshot] = None,
        raise_in_stream: bool = False,
        stop_reason: str = "complete",
    ) -> None:
        self._text_chunks = text_chunks or ["Hello", " world"]
        self._usage = usage or UsageSnapshot(input_tokens=500, output_tokens=200, available=True)
        self._raise = raise_in_stream
        self._stop_reason = stop_reason
        self.run_stream_called_with: dict = {}

    async def check_available(self) -> tuple[bool, str]:
        return True, "fake"

    async def chat(self, **kwargs) -> None:  # pragma: no cover — not used
        raise NotImplementedError

    async def run_stream(self, **kwargs) -> AsyncIterator[StreamEvent]:
        self.run_stream_called_with = kwargs
        if self._raise:
            raise RuntimeError("stream blew up")
        for chunk in self._text_chunks:
            yield TextDeltaEvent(text=chunk)
        yield MessageStopEvent(
            reason=self._stop_reason,  # type: ignore[arg-type]
            accumulated_text="".join(self._text_chunks),
            usage=self._usage,
            error_message="boom" if self._stop_reason == "error" else "",
            error_code="stream_error" if self._stop_reason == "error" else "",
        )


@pytest.fixture
def fresh_routing():
    """Fresh mock server + bound routing_client (TEST-1000 → user-test)."""
    reset_embedded_server()
    reset_routing_client()
    client = RoutingClient(url="embedded")
    yield client


@pytest.fixture
def captured_events():
    sink_events: list[dict] = []

    async def _sink(payload: dict) -> None:
        sink_events.append(payload)

    return sink_events, _sink


def _make_callbacks():
    on_delta = AsyncMock()
    on_final = AsyncMock()
    on_error = AsyncMock()
    on_status = AsyncMock()
    on_tool_round = AsyncMock()
    return ProviderCallbacks(
        on_delta=on_delta,
        on_final=on_final,
        on_error=on_error,
        on_status=on_status,
        on_tool_round=on_tool_round,
    )


@pytest.mark.unit
class TestHybridSuccessPath:
    @pytest.mark.asyncio
    async def test_route_hold_stream_settle_balance_decreases(self, fresh_routing, captured_events):
        client = fresh_routing
        # 라이선스 활성화 후 바인딩
        activation = await client.activate("TEST-1000", "fp-test", "Test Mac")
        client.bind_user(activation.jwt)

        events, sink = captured_events
        inner = _FakeInner(
            text_chunks=["hi"],
            usage=UsageSnapshot(input_tokens=50, output_tokens=30, available=True),
        )
        provider = OpenDeskRoutedProvider(
            routing_client=client,
            credit_event_sink=sink,
            inner=inner,
        )

        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "hi"}],
            callbacks=callbacks,
        )

        # final 호출됐고 error 없음
        callbacks.on_final.assert_awaited_once()
        callbacks.on_error.assert_not_awaited()
        callbacks.on_delta.assert_awaited_with("hi")

        # credit 이벤트 시퀀스
        event_types = [e["type"] for e in events]
        assert "credit.routing" in event_types
        assert "credit.balance" in event_types
        assert "credit.settled" in event_types

        # 마지막 balance 가 초기 1000 보다 작거나 같음 (실제 settle 후 거의 환불)
        final_balance_events = [e for e in events if e["type"] == "credit.balance" and e.get("held") == 0]
        assert final_balance_events  # 최종 balance push 발생
        assert final_balance_events[-1]["balance"] <= 1000


@pytest.mark.unit
class TestInsufficientCredits:
    @pytest.mark.asyncio
    async def test_low_balance_denies_before_stream(self, fresh_routing, captured_events):
        from mock_routing_server import get_embedded_server

        client = fresh_routing
        activation = await client.activate("TEST-1000", "fp-test", "Test")
        client.bind_user(activation.jwt)
        # 잔액 0 으로 비움
        await get_embedded_server().admin_set_balance("user-test", 0)

        events, sink = captured_events
        inner = _FakeInner()
        provider = OpenDeskRoutedProvider(routing_client=client, credit_event_sink=sink, inner=inner)

        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "hello"}],
            callbacks=callbacks,
        )

        callbacks.on_error.assert_awaited_once()
        callbacks.on_final.assert_not_awaited()
        # inner.run_stream 호출되지 않음
        assert inner.run_stream_called_with == {}

        types = [e["type"] for e in events]
        assert "credit.insufficient" in types


@pytest.mark.unit
class TestStreamErrorRefunds:
    @pytest.mark.asyncio
    async def test_stream_exception_triggers_refund(self, fresh_routing, captured_events):
        from mock_routing_server import get_embedded_server

        client = fresh_routing
        activation = await client.activate("TEST-1000", "fp-test", "Test")
        client.bind_user(activation.jwt)

        events, sink = captured_events
        inner = _FakeInner(raise_in_stream=True)
        provider = OpenDeskRoutedProvider(routing_client=client, credit_event_sink=sink, inner=inner)

        before = (await get_embedded_server().balance("user-test")).balance

        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "hi"}],
            callbacks=callbacks,
        )

        callbacks.on_error.assert_awaited_once()
        after = (await get_embedded_server().balance("user-test")).balance
        assert after == before, "refund 가 hold 전액을 원복해야 함"

    @pytest.mark.asyncio
    async def test_stream_error_event_triggers_refund(self, fresh_routing, captured_events):
        from mock_routing_server import get_embedded_server

        client = fresh_routing
        activation = await client.activate("TEST-1000", "fp-test", "Test")
        client.bind_user(activation.jwt)

        events, sink = captured_events
        inner = _FakeInner(stop_reason="error")
        provider = OpenDeskRoutedProvider(routing_client=client, credit_event_sink=sink, inner=inner)

        before = (await get_embedded_server().balance("user-test")).balance
        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "hi"}],
            callbacks=callbacks,
        )

        callbacks.on_error.assert_awaited_once()
        after = (await get_embedded_server().balance("user-test")).balance
        assert after == before


@pytest.mark.unit
class TestNotAuthenticated:
    @pytest.mark.asyncio
    async def test_chat_without_bind_errors(self, fresh_routing, captured_events):
        # bind_user 호출 안 함
        client = fresh_routing
        events, sink = captured_events
        inner = _FakeInner()
        provider = OpenDeskRoutedProvider(routing_client=client, credit_event_sink=sink, inner=inner)

        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "hi"}],
            callbacks=callbacks,
        )
        callbacks.on_error.assert_awaited_once()
        args, _ = callbacks.on_error.await_args
        assert args[1] == "license_not_activated"


@pytest.mark.unit
class TestExplicitModelHint:
    @pytest.mark.asyncio
    async def test_complexity_hint_forwards_to_router(self, fresh_routing, captured_events):
        client = fresh_routing
        activation = await client.activate("TEST-1000", "fp-test", "Test")
        client.bind_user(activation.jwt)
        events, sink = captured_events

        inner = _FakeInner(
            text_chunks=["ok"],
            usage=UsageSnapshot(input_tokens=10, output_tokens=10, available=True),
        )
        provider = OpenDeskRoutedProvider(
            routing_client=client,
            credit_event_sink=sink,
            inner=inner,
            user_complexity_hint="complex",
        )

        callbacks = _make_callbacks()
        await provider.chat(
            messages=[{"role": "user", "content": "tiny"}],
            callbacks=callbacks,
        )
        # complex hint → large tier 라우팅
        routing_events = [e for e in events if e["type"] == "credit.routing"]
        assert routing_events
        assert routing_events[0]["tier"] == "large"
