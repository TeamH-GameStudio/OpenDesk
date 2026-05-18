"""
OpenDeskRoutedProvider — Hybrid 라우팅 provider.

문서 원안의 클라우드 단일 서버 모델은 도구 실행과 사용자 자격증명까지 서버로
올려야 한다. OpenDesk 는 MCP/Skill/Plugin 도구가 로컬 자격증명에 의존하므로
이 가정은 깨진다. 따라서 **하이브리드** 로 분할한다:

    1) Pre-chat: 백엔드 서버에 라우팅 결정 + 크레딧 hold 요청
    2) Local: AnthropicApiProvider 의 run_stream() 으로 스트림 + MCP 도구 루프 수행
    3) Post-chat: 토큰 사용량을 서버에 settle (또는 예외 시 refund)

도구 실행과 자격증명은 로컬에 머무르고, 서버는 라우팅 + 크레딧 계산만 책임진다.

`OPENDESK_ROUTING_URL=embedded` (기본) 면 인메모리 mock 서버.
실 FastAPI 서버 구현 후에는 URL 만 바꾸면 그대로 운영 전환.
"""

from __future__ import annotations

import logging
from typing import Any, AsyncIterator, Awaitable, Callable, Optional

from hooks.protocol import UsageSnapshot
from mock_routing_server import RoutingDecision, RoutingDeniedError
from routing_client import RoutingClient, get_routing_client

from .anthropic_api import AnthropicApiProvider
from .base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)
from .registry import register_provider


logger = logging.getLogger("provider.opendesk_routed")


# ── 이벤트 송신 헬퍼 타입 ────────────────────────────────────
#
# 서버가 ChatSession 단위로 credit.* 메시지를 Unity 쪽에 푸시할 수 있도록
# provider 가 호출하는 콜백. ChatSession 이 set_credit_event_sink 로 주입한다.

CreditEventSink = Callable[[dict[str, Any]], Awaitable[None]]


class OpenDeskRoutedProvider(ProviderBase):
    name = "opendesk_routed"

    def __init__(
        self,
        agent_role: str = "default",
        user_complexity_hint: str = "auto",
        routing_client: Optional[RoutingClient] = None,
        credit_event_sink: Optional[CreditEventSink] = None,
        inner: Optional[AnthropicApiProvider] = None,
    ) -> None:
        self._agent_role = agent_role
        self._hint = user_complexity_hint
        self._routing = routing_client or get_routing_client()
        self._credit_sink = credit_event_sink
        self._inner = inner or AnthropicApiProvider()

    # ── 컨텍스트 주입 setter ────────────────────────────────

    def set_agent_role(self, role: str) -> None:
        self._agent_role = role or "default"

    def set_complexity_hint(self, hint: str) -> None:
        self._hint = hint or "auto"

    def set_credit_event_sink(self, sink: Optional[CreditEventSink]) -> None:
        self._credit_sink = sink

    # ── ProviderBase 인터페이스 ─────────────────────────────

    async def check_available(self) -> tuple[bool, str]:
        if not self._routing.is_authenticated:
            return False, "license_not_activated"
        return await self._inner.check_available()

    async def chat(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        callbacks: ProviderCallbacks,
        in_process_tools: Optional[Any] = None,
    ) -> None:
        """Pre-route → 내부 inner.chat 위임 → settle.

        usage 가 필요해 run_stream 을 사용하지 않고, inner.chat 의 callbacks 를
        가로채는 방식으로 구현. inner.chat 은 cost 만 넘기고 토큰을 직접 노출하지
        않으므로, 별도로 inner 의 run_stream 을 한 번 사용해 final_message 의 usage
        를 가져오는 어댑터를 거친다.
        """
        if not messages:
            await callbacks.on_error("메시지가 비어있습니다", "empty_message")
            return

        if not self._routing.is_authenticated:
            await callbacks.on_error(
                "라이선스 인증이 필요합니다. /auth/activate 후 set_auth 로 JWT 를 전달하세요.",
                "license_not_activated",
            )
            return

        last_user = next((m for m in reversed(messages) if m.get("role") == "user"), None)
        user_task = _stringify(last_user.get("content")) if last_user else ""
        available_tool_names = _list_available_tool_names(in_process_tools)

        try:
            decision = await self._routing.route(
                user_task=user_task,
                agent_role=self._agent_role,
                available_tools=available_tool_names,
                user_complexity_hint=self._hint,
                requested_model=model,
            )
        except RoutingDeniedError as e:
            await self._emit_credit({"type": "credit.insufficient",
                                     "code": e.code,
                                     "required": e.required,
                                     "balance": e.balance})
            await callbacks.on_error(str(e), e.code)
            return
        except Exception as e:  # noqa: BLE001
            logger.exception("라우팅 결정 실패")
            await callbacks.on_error(f"routing failed: {e}", "routing_failed")
            return

        await self._emit_credit({
            "type": "credit.routing",
            "taskId": decision.task_id,
            "model": decision.model,
            "tier": decision.tier_name,
            "estimatedCredits": decision.estimated_credits,
            "reasoning": decision.reasoning,
            "escalationAllowed": decision.escalation_allowed,
            "expectedToolCalls": decision.expected_tool_calls,
        })

        try:
            hold = await self._routing.hold(decision.estimated_credits, decision.task_id)
        except RoutingDeniedError as e:
            await self._emit_credit({"type": "credit.insufficient",
                                     "code": e.code,
                                     "required": e.required,
                                     "balance": e.balance})
            await callbacks.on_error(str(e), e.code)
            return

        await self._emit_credit({
            "type": "credit.balance",
            "balance": hold.balance_after,
            "held": decision.estimated_credits,
        })

        # 내부 run_stream 으로 위임 → final usage 까지 받아 settle.
        usage = UsageSnapshot()
        accumulated_text = ""
        stop_reason = "complete"
        error_payload: tuple[str, str] | None = None

        try:
            async for event in self._inner.run_stream(
                messages=messages,
                system_prompt=system_prompt,
                mcp_config=mcp_config,
                model=decision.model,
                in_process_tools=in_process_tools,
            ):
                if isinstance(event, TextDeltaEvent):
                    if event.text:
                        await callbacks.on_delta(event.text)
                elif isinstance(event, ToolUseStartEvent):
                    if callbacks.on_status is not None:
                        await callbacks.on_status(f"tool_use:{event.name}:start")
                elif isinstance(event, ToolUseResultEvent):
                    if callbacks.on_tool_round is not None:
                        await callbacks.on_tool_round(event.name, None, event.result)
                elif isinstance(event, MessageStopEvent):
                    accumulated_text = event.accumulated_text
                    stop_reason = event.reason
                    if event.usage is not None and event.usage.available:
                        usage = event.usage
                    if event.reason == "error":
                        error_payload = (
                            event.error_message or "stream error",
                            event.error_code or "stream_error",
                        )
                    break
        except Exception as e:  # noqa: BLE001
            logger.exception("inner stream 실패")
            error_payload = (str(e), "inner_stream_error")

        if error_payload is not None or stop_reason == "error":
            try:
                refund = await self._routing.refund(hold.hold_id, reason=str(error_payload))
                await self._emit_credit({
                    "type": "credit.balance",
                    "balance": refund.balance,
                    "held": refund.held,
                })
            except Exception:  # noqa: BLE001
                logger.exception("refund 실패")
            if error_payload is not None:
                await callbacks.on_error(error_payload[0], error_payload[1])
            return

        # Settle.
        try:
            settle = await self._routing.settle(
                hold_id=hold.hold_id,
                model_id=decision.model,
                input_tokens=usage.input_tokens,
                output_tokens=usage.output_tokens,
            )
            await self._emit_credit({
                "type": "credit.settled",
                "taskId": decision.task_id,
                "model": decision.model,
                "actualCredits": settle.actual_credits,
                "inputTokens": usage.input_tokens,
                "outputTokens": usage.output_tokens,
                "balance": settle.balance_after,
            })
            await self._emit_credit({
                "type": "credit.balance",
                "balance": settle.balance_after,
                "held": 0,
            })
        except Exception:  # noqa: BLE001
            logger.exception("settle 실패 — 잔액이 불일치할 수 있음")

        await callbacks.on_final(accumulated_text, 0.0)

    async def run_stream(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        in_process_tools: Optional[Any] = None,
    ) -> AsyncIterator[StreamEvent]:
        """기본 ProviderBase.run_stream 구현은 chat 콜백 어댑팅을 사용한다.
        opendesk_routed 는 라우팅 사이클이 묶여 있어 inner.run_stream 을 그대로
        forward 하지 않는다 — 대신 ProviderBase 기본 어댑터에 위임한다.
        """
        async for event in super().run_stream(
            messages=messages,
            system_prompt=system_prompt,
            mcp_config=mcp_config,
            model=model,
            in_process_tools=in_process_tools,
        ):
            yield event

    def kill_active(self) -> None:
        self._inner.kill_active()

    # ── 내부 ────────────────────────────────────────────────

    async def _emit_credit(self, payload: dict[str, Any]) -> None:
        if self._credit_sink is None:
            return
        try:
            await self._credit_sink(payload)
        except Exception:  # noqa: BLE001
            logger.exception("credit event sink 실패 (계속 진행)")


# ── 헬퍼 ─────────────────────────────────────────────────────

def _stringify(content: Any) -> str:
    """messages[i].content 가 str 또는 block list 일 수 있음. text 만 추출."""
    if content is None:
        return ""
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        parts: list[str] = []
        for block in content:
            if isinstance(block, dict):
                if block.get("type") == "text" and isinstance(block.get("text"), str):
                    parts.append(block["text"])
                elif "text" in block and isinstance(block["text"], str):
                    parts.append(block["text"])
            elif isinstance(block, str):
                parts.append(block)
        return "\n".join(parts)
    return str(content)


def _list_available_tool_names(in_process_tools: Optional[Any]) -> list[str]:
    if in_process_tools is None:
        return []
    try:
        schemas = in_process_tools.to_anthropic_schemas()
        return [s.get("name", "") for s in schemas if s.get("name")]
    except Exception:  # noqa: BLE001
        return []


# ── 등록 ─────────────────────────────────────────────────────

def _make_opendesk_routed() -> OpenDeskRoutedProvider:
    return OpenDeskRoutedProvider()


register_provider("opendesk_routed", _make_opendesk_routed)
