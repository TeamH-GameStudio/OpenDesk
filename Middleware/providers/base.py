"""
ProviderBase — 모든 AI provider 가 구현하는 추상 인터페이스.

provider 는 사용자 prompt 와 (선택적) MCP config 를 받아 응답을 스트리밍한다.
두 가지 인터페이스를 제공한다:

1. `chat(...)` — 콜백 기반 (legacy). on_delta/on_final/on_error/on_status 콜백을 호출한다.
2. `run_stream(...)` — async iterator 기반 (신규). StreamEvent 객체를 yield 한다.
   캐릭터 입모양 동기화처럼 raw 이벤트 시퀀스가 필요한 소비자가 사용.

기본 `run_stream` 구현은 `chat()` 콜백을 큐로 어댑팅해서 호환성을 보장한다.
새로 구현하는 provider 는 `run_stream` 만 override 해도 되고, 양쪽 다 구현해도 된다.
"""

from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any, AsyncIterator, Awaitable, Callable, Literal, Optional

from hooks.protocol import UsageSnapshot

if TYPE_CHECKING:
    from tools.registry import ToolRegistry


# ── 콜백 타입 alias (legacy) ────────────────────────────────────

OnDelta = Callable[[str], Awaitable[None]]
OnFinal = Callable[[str, float], Awaitable[None]]
OnError = Callable[[str, str], Awaitable[None]]
OnStatus = Callable[[str], Awaitable[None]]
# tool_use 라운드 1회 분 기록. provider 가 도구 실행 직후 호출하면
# 서버가 ChatSession.tool_journal 로 라우팅한다.
OnToolRound = Callable[[str, Any, Any], Awaitable[None]]


@dataclass(frozen=True)
class ProviderCallbacks:
    """provider 에서 호출하는 스트리밍 콜백 묶음 (legacy chat 인터페이스용)."""

    on_delta: OnDelta
    on_final: OnFinal
    on_error: OnError
    on_status: Optional[OnStatus] = None
    on_tool_round: Optional[OnToolRound] = None


# ── Stream 이벤트 (run_stream 인터페이스용) ─────────────────────

# 디스크리미네이티드 union: type 필드로 구분.
StreamEventType = Literal[
    "text_delta",
    "tool_use_start",
    "tool_use_result",
    "message_stop",
]

# message_stop reason — talking_stop 의 reason 과 1:1 매핑.
StopReason = Literal["complete", "error", "interrupted"]


@dataclass(frozen=True)
class TextDeltaEvent:
    """텍스트 토큰 청크. Unity 의 text_delta WebSocket 메시지로 그대로 흐른다."""
    text: str
    type: StreamEventType = "text_delta"


@dataclass(frozen=True)
class ToolUseStartEvent:
    """도구 호출 시작. tool_use_id 로 결과와 짝짓는다."""
    tool_use_id: str
    name: str
    input: dict[str, Any] = field(default_factory=dict)
    type: StreamEventType = "tool_use_start"


@dataclass(frozen=True)
class ToolUseResultEvent:
    """도구 호출 결과. is_error=True 면 도구 실행 실패."""
    tool_use_id: str
    name: str
    result: str
    is_error: bool = False
    type: StreamEventType = "tool_use_result"


@dataclass(frozen=True)
class MessageStopEvent:
    """스트림 종료. accumulated_text 는 이번 턴의 누적 텍스트 (히스토리 저장용).

    usage 는 이번 요청의 누적 토큰/캐시 사용량 (provider 가 추출 가능한 경우).
    None 또는 available=False 면 hook 들이 통계 계산을 건너뛰어야 함.
    """
    reason: StopReason = "complete"
    accumulated_text: str = ""
    cost: float = 0.0
    error_message: str = ""
    error_code: str = ""
    usage: Optional[UsageSnapshot] = None
    type: StreamEventType = "message_stop"


StreamEvent = TextDeltaEvent | ToolUseStartEvent | ToolUseResultEvent | MessageStopEvent


# ── ProviderBase ────────────────────────────────────────────────

class ProviderBase(ABC):
    """모든 AI provider 의 공통 인터페이스."""

    # provider 식별자 ("anthropic_cli", "anthropic_api", "openai", "gemini" …)
    name: str = ""

    @abstractmethod
    async def check_available(self) -> tuple[bool, str]:
        """provider 가 사용 가능한지 확인. (ok, version_or_error) 반환."""

    @abstractmethod
    async def chat(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        callbacks: ProviderCallbacks,
        in_process_tools: Optional["ToolRegistry"] = None,
    ) -> None:
        """채팅 요청을 처리하고 콜백으로 응답을 스트리밍한다.

        messages: 대화 히스토리. [{"role": "user"|"assistant", "content": "..."}]
            마지막 항목은 반드시 role="user".
            provider 가 자기 형식(CLI 통합 prompt vs API messages)으로 변환.
        system_prompt: 시스템 프롬프트 (별도 채널). CLI 는 prompt 앞에 합성.
        mcp_config: McpConfigPayload JSON dict.
            { "agentId": str, "servers": [ {name, transport, command, args, env} ] }
            None / 빈 servers 면 MCP 비활성.
        in_process_tools: 미들웨어 in-process 도구 레지스트리 (BaseTool 기반).
            API provider 는 이 도구들을 MCP 도구와 함께 모델에 노출하고,
            tool_use 응답 시 in-process 우선 룩업 → MCP hub 폴백 순서로 실행.
            CLI provider 는 미지원 (Claude CLI subprocess 가 호스트 메모리에 접근 불가).
        """

    async def run_stream(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        in_process_tools: Optional["ToolRegistry"] = None,
    ) -> AsyncIterator[StreamEvent]:
        """async iterator 형태로 StreamEvent 를 yield.

        기본 구현은 `chat()` 콜백을 asyncio.Queue 로 어댑팅한다. provider 가
        native 스트리밍을 가지고 있으면 override 해서 더 정확한 이벤트 시퀀스를
        제공할 수 있다.

        반드시 마지막에 MessageStopEvent 를 1회 yield 한다 (정상/에러 무관).
        """
        queue: asyncio.Queue[StreamEvent] = asyncio.Queue()
        accumulated: list[str] = []

        async def _on_delta(text: str) -> None:
            accumulated.append(text)
            await queue.put(TextDeltaEvent(text=text))

        async def _on_final(full_text: str, cost: float) -> None:
            await queue.put(MessageStopEvent(
                reason="complete",
                accumulated_text=full_text or "".join(accumulated),
                cost=cost,
            ))

        async def _on_error(message: str, code: str) -> None:
            await queue.put(MessageStopEvent(
                reason="error",
                accumulated_text="".join(accumulated),
                error_message=message,
                error_code=code,
            ))

        callbacks = ProviderCallbacks(
            on_delta=_on_delta,
            on_final=_on_final,
            on_error=_on_error,
            on_status=None,
        )

        chat_task = asyncio.create_task(self.chat(
            messages=messages,
            system_prompt=system_prompt,
            mcp_config=mcp_config,
            model=model,
            callbacks=callbacks,
            in_process_tools=in_process_tools,
        ))

        try:
            while True:
                # chat_task 완료 + 큐 비었으면 누락된 message_stop 보강 후 종료
                if chat_task.done() and queue.empty():
                    yield MessageStopEvent(
                        reason="complete",
                        accumulated_text="".join(accumulated),
                    )
                    return

                getter = asyncio.create_task(queue.get())
                done, _ = await asyncio.wait(
                    {getter, chat_task},
                    return_when=asyncio.FIRST_COMPLETED,
                )
                if getter in done:
                    event = getter.result()
                    yield event
                    if isinstance(event, MessageStopEvent):
                        return
                else:
                    getter.cancel()
        finally:
            if not chat_task.done():
                chat_task.cancel()

    def kill_active(self) -> None:
        """진행 중인 호출 강제 종료. 기본 구현은 no-op (provider 가 필요 시 override)."""
        return
