"""
BaseHook — 모든 lifecycle 메서드의 no-op 기본 구현.

새 hook 은 BaseHook 을 상속하고 필요한 메서드만 override 하면 된다.
이 패턴이 Protocol 만 두는 것보다 안전한 이유는:
- 새 lifecycle 메서드가 추가되어도 기존 hook 들이 깨지지 않음
- IDE 자동완성과 타입 검사가 일관됨
- 동기 return (await 없음) 으로 hot path 에서 hook overhead 최소화
"""

from __future__ import annotations

from typing import Optional

from .protocol import ErrorAction, RequestCtx, UsageSnapshot


class BaseHook:
    """No-op 기본 구현 mixin. 서브클래스에서 필요한 메서드만 override."""

    name: str = "base"

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        return None

    async def on_first_token(self, ctx: RequestCtx, ts: float) -> None:
        return None

    async def on_tool_round_complete(
        self,
        ctx: RequestCtx,
        round_idx: int,
        usage: UsageSnapshot,
    ) -> None:
        return None

    async def on_post_response(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        return None

    async def on_error(
        self,
        ctx: RequestCtx,
        error: BaseException,
    ) -> Optional[ErrorAction]:
        return None
