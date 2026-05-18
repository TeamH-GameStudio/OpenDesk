"""
HookPipeline — 등록된 hook 들을 순서대로 실행하는 executor.

설계 결정:
- 실행 순서: 모든 lifecycle 메서드가 등록 순방향. TelemetryEmitter 처럼 다른 hook 의
  metadata 를 읽어야 하는 hook 은 **마지막에 등록**한다.
  (초기에 "post 만 reverse" 패턴을 시도했으나, first_token / tool_round 도 동일하게
   emitter 가 마지막에 실행돼야 정합성이 맞으므로 모든 메서드 forward 로 통일.)
- 에러 격리: 모든 hook 호출이 try/except 로 감싸짐. 한 hook 실패가 체인을 중단하지 않음.
- 에러 결정 투표 (on_error):
    * retry/escalate 는 첫 번째 반환 hook 이 승리 (early return).
    * suppress 는 만장일치일 때만 적용 (모든 hook 이 None 또는 suppress 반환).
    * 그 외엔 기본 escalate.
- 차단 의미론 (on_pre_request): blocking_hook_names 에 등록된 hook 이 None 반환 시 차단.
  나머지 hook 의 None 은 "변경 없음" 으로 해석.
"""

from __future__ import annotations

import logging
from typing import Iterable, Optional

from .protocol import ErrorAction, MessageHook, RequestCtx, UsageSnapshot

logger = logging.getLogger("hooks.pipeline")


class HookPipeline:
    """Hook 체인 실행기. ChatSession 이 1 회 생성하여 HookedProvider 에 주입."""

    def __init__(
        self,
        hooks: Iterable[MessageHook],
        blocking_hook_names: Optional[set[str]] = None,
    ):
        # 등록 순서를 보존 (dict/set 사용 금지). 동일 name 중복 등록은 허용하되 경고.
        self._hooks: list[MessageHook] = list(hooks)
        seen: set[str] = set()
        for h in self._hooks:
            if h.name in seen:
                logger.warning("duplicate hook name registered: %s", h.name)
            seen.add(h.name)
        self._blocking: set[str] = set(blocking_hook_names or ())

    @property
    def hooks(self) -> tuple[MessageHook, ...]:
        return tuple(self._hooks)

    async def run_pre(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        """on_pre_request 를 등록 순서로 실행. 차단 hook 이 None 반환하면 ctx=None."""
        current = ctx
        for h in self._hooks:
            try:
                result = await h.on_pre_request(current)
            except Exception:
                logger.exception("hook %s failed in on_pre_request (isolated)", h.name)
                continue
            if result is None:
                if h.name in self._blocking:
                    logger.info("blocking hook %s aborted request", h.name)
                    return None
                # 비차단 hook 의 None 은 "ctx 변경 없음".
                continue
            current = result
        return current

    async def run_first_token(self, ctx: RequestCtx, ts: float) -> None:
        for h in self._hooks:
            try:
                await h.on_first_token(ctx, ts)
            except Exception:
                logger.exception("hook %s failed in on_first_token", h.name)

    async def run_tool_round_complete(
        self,
        ctx: RequestCtx,
        round_idx: int,
        usage: UsageSnapshot,
    ) -> None:
        for h in self._hooks:
            try:
                await h.on_tool_round_complete(ctx, round_idx, usage)
            except Exception:
                logger.exception("hook %s failed in on_tool_round_complete", h.name)

    async def run_post(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        # 등록 순방향. emitter 가 마지막에 등록되어야 다른 hook 이 채운 metadata 를 본다.
        for h in self._hooks:
            try:
                await h.on_post_response(ctx, final_text, usage)
            except Exception:
                logger.exception("hook %s failed in on_post_response", h.name)

    async def run_error(
        self,
        ctx: RequestCtx,
        error: BaseException,
    ) -> ErrorAction:
        """에러 결정 투표.

        retry / escalate 가 나오면 즉시 반환 (early return).
        모든 hook 이 None 또는 suppress 만 반환하면 suppress 로 결정.
        그 외 (전부 None) 면 escalate 가 기본.
        """
        suppress_seen = False
        all_none = True
        for h in self._hooks:
            try:
                action = await h.on_error(ctx, error)
            except Exception:
                logger.exception("hook %s failed in on_error", h.name)
                continue
            if action is None:
                continue
            all_none = False
            if action.kind == "retry" or action.kind == "escalate":
                return action
            if action.kind == "suppress":
                suppress_seen = True
        if all_none:
            return ErrorAction(kind="escalate", reason="no hook handled error")
        # 만장일치 suppress (retry/escalate 가 한 번도 안 나왔고 suppress 가 있음)
        if suppress_seen:
            return ErrorAction(kind="suppress", reason="unanimous suppress")
        return ErrorAction(kind="escalate", reason="fallthrough")
