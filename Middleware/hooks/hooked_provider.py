"""
HookedProvider — ProviderBase 를 감싸 lifecycle 시점에 HookPipeline 을 실행하는 데코레이터.

설계 원칙:
- 안쪽 provider 는 hook 무지(無知). 데코레이터가 스트림 이벤트 시퀀스 위에서 hook 호출.
- 어떤 provider 든 동일하게 작동 (anthropic_api / anthropic_cli / 미래 OpenAI 등).
- on_pre_request 가 blocking hook 에 의해 차단되면 즉시 error MessageStop 만 yield.
- 첫 TextDeltaEvent 도착 시 on_first_token 1회 (TTFT 기록 지점).
- ToolUseResultEvent 도착 시마다 tool_round 증가 + on_tool_round_complete.
- MessageStopEvent(reason='complete') 시 on_post_response.
- 예외 발생 시 on_error 투표 → escalate/suppress 처리. retry 는 Day 5 에서 wiring.

ctx_factory 는 매 run_stream 호출마다 새 RequestCtx 를 만드는 callable.
ChatSession 이 request_id / agent_id / session_id 를 매핑해서 주입한다.
"""

from __future__ import annotations

import logging
import time
from typing import AsyncIterator, Callable, Optional

from providers.base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
)

from .pipeline import HookPipeline
from .protocol import RequestCtx, UsageSnapshot

logger = logging.getLogger("hooks.hooked_provider")


CtxFactory = Callable[[], RequestCtx]


class HookedProvider(ProviderBase):
    """ProviderBase 를 감싸 hook lifecycle 을 실행하는 데코레이터.

    inner 에서 비롯되는 모든 StreamEvent 는 변경 없이 pass-through 된다.
    hook 호출은 yield 사이에 await 로 끼어들 뿐, 이벤트 순서/내용은 보존.

    Note: `chat()` legacy 인터페이스는 hook 처리 없이 inner 로 passthrough. hook 체인은
    `run_stream()` 경로에서만 동작. 모든 새 호출자는 run_stream 을 사용해야 한다.

    Dynamic max_tool_rounds: cache hit ratio 가 임계치(기본 0.6) 를 넘으면 bonus 라운드를
    추가로 허용한다. cache 가 잘 작동 중이면 추가 호출의 토큰 비용이 낮으므로 더 깊은
    추론을 허용해도 안전하다는 휴리스틱.
    """

    def __init__(
        self,
        inner: ProviderBase,
        pipeline: HookPipeline,
        ctx_factory: CtxFactory,
        *,
        base_max_tool_rounds: int = 8,
        cache_bonus_rounds: int = 4,
        cache_hit_bonus_threshold: float = 0.6,
    ):
        self._inner = inner
        self._pipeline = pipeline
        self._ctx_factory = ctx_factory
        self.name = inner.name
        self._base_rounds = max(1, int(base_max_tool_rounds))
        self._cache_bonus = max(0, int(cache_bonus_rounds))
        self._cache_threshold = float(cache_hit_bonus_threshold)

    def _resolve_max_rounds(self, ctx: RequestCtx) -> int:
        """ctx.metadata['cache']['hit_ratio'] 를 보고 bonus 결정."""
        if self._cache_bonus <= 0:
            return self._base_rounds
        cache = ctx.metadata.get("cache", {})
        if not isinstance(cache, dict):
            return self._base_rounds
        if not cache.get("available", False):
            return self._base_rounds
        ratio = float(cache.get("hit_ratio") or 0.0)
        if ratio >= self._cache_threshold:
            return self._base_rounds + self._cache_bonus
        return self._base_rounds

    async def check_available(self) -> tuple[bool, str]:
        return await self._inner.check_available()

    async def chat(
        self,
        *,
        messages,
        system_prompt: str = "",
        mcp_config=None,
        model: str = "",
        callbacks: ProviderCallbacks,
        in_process_tools=None,
    ) -> None:
        # legacy 경로 — hook 미적용. 향후 run_stream 으로 통일 후 제거 예정.
        return await self._inner.chat(
            messages=messages,
            system_prompt=system_prompt,
            mcp_config=mcp_config,
            model=model,
            callbacks=callbacks,
            in_process_tools=in_process_tools,
        )

    def kill_active(self) -> None:
        self._inner.kill_active()

    async def run_stream(
        self,
        *,
        messages,
        system_prompt: str = "",
        mcp_config=None,
        model: str = "",
        in_process_tools=None,
    ) -> AsyncIterator[StreamEvent]:
        ctx = self._ctx_factory()
        pre_result = await self._pipeline.run_pre(ctx)
        if pre_result is None:
            yield MessageStopEvent(
                reason="error",
                accumulated_text="",
                error_message="request blocked by hook",
                error_code="hook_blocked",
            )
            return
        ctx = pre_result

        first_token_seen = False
        cumulative_usage: Optional[UsageSnapshot] = None
        final_text_parts: list[str] = []
        # 시작 시점 cache 통계 기반 (보통 0). 첫 round 후 ratio 가 갱신되면 inner 에
        # 별도 신호를 전달하기 어렵지만, 적어도 max_tool_rounds 메타데이터는 기록.
        max_rounds = self._resolve_max_rounds(ctx)
        ctx.metadata["max_tool_rounds"] = max_rounds

        try:
            inner_kwargs = {
                "messages": messages,
                "system_prompt": system_prompt,
                "mcp_config": mcp_config,
                "model": model,
                "in_process_tools": in_process_tools,
            }
            # provider 가 max_tool_rounds kwarg 를 받으면 주입 (anthropic_api).
            # 받지 않으면 무시 — TypeError 회피 위해 try.
            try:
                async_iter = self._inner.run_stream(**inner_kwargs, max_tool_rounds=max_rounds)
            except TypeError:
                async_iter = self._inner.run_stream(**inner_kwargs)

            async for evt in async_iter:
                if isinstance(evt, TextDeltaEvent):
                    if not first_token_seen:
                        first_token_seen = True
                        await self._pipeline.run_first_token(ctx, time.monotonic())
                    final_text_parts.append(evt.text)

                if isinstance(evt, ToolUseResultEvent):
                    ctx.tool_round += 1
                    # provider 가 round 단위 usage 를 제공하면 여기에서 누적해야 하지만
                    # 현재 ToolUseResultEvent 는 usage 를 운반하지 않는다. 빈 (모든 0) snapshot
                    # 을 available=True 로 두면 CacheStatHook 등이 누적 산수에 영향 없이
                    # round 카운트만 가져가게 된다 (분모/분자 모두 0 → 변화 없음).
                    await self._pipeline.run_tool_round_complete(
                        ctx, ctx.tool_round, UsageSnapshot()
                    )

                yield evt

                if isinstance(evt, MessageStopEvent):
                    final_usage = evt.usage or UsageSnapshot(available=False)
                    ctx.cumulative_usage = final_usage
                    cumulative_usage = final_usage
                    if evt.reason == "complete":
                        await self._pipeline.run_post(
                            ctx,
                            evt.accumulated_text or "".join(final_text_parts),
                            final_usage,
                        )
                    return
        except Exception as exc:
            action = await self._pipeline.run_error(ctx, exc)
            if action.kind == "suppress":
                # 조용히 종료 — error event 는 발생시키지만 raise 안 함.
                yield MessageStopEvent(
                    reason="error",
                    accumulated_text="".join(final_text_parts),
                    error_message=action.reason or "suppressed",
                    error_code="hook_suppressed",
                    usage=cumulative_usage,
                )
                return
            # retry kind 는 현재 Day 2 단계에서 별도 처리 안 함 — Day 5/9 에서 wiring.
            # 향후 retry 의 경우 inner.run_stream 을 새로 시작해서 동일 스트림 재발행.
            # 지금은 escalate 동일하게 처리.
            logger.warning(
                "hook decided %s for error %s; raising (retry not yet wired)",
                action.kind, type(exc).__name__,
            )
            raise
