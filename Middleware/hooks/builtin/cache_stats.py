"""
CacheStatHook — Anthropic 의 prompt caching 효과를 추적.

usage block 의 cache_creation_input_tokens / cache_read_input_tokens 를 누적하여
hit_ratio = read / (read + creation + non_cached_input) 형태로 계산.

ctx.metadata["cache"] 에 다음 채움:
    {
        "available": bool,                   # provider 가 캐시 통계 제공 가능
        "creation_tokens": int,
        "read_tokens": int,
        "non_cached_input_tokens": int,
        "hit_ratio": float,                   # 0.0 ~ 1.0
        "input_tokens": int,                  # 모든 input 합 (참고용)
    }

available=False 면 hit_ratio 는 0.0 으로 두되, 대시보드에서 grey-out 표시되도록
TelemetryEmitterHook 가 available 플래그를 함께 emit 해야 함.

분모 0 이면 hit_ratio=0.0 (NaN 회피).
"""

from __future__ import annotations

from typing import Optional

from ..base import BaseHook
from ..protocol import RequestCtx, UsageSnapshot


class CacheStatHook(BaseHook):
    name = "cache"

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        ctx.metadata.setdefault(self.name, {
            "available": True,  # 일단 True 로 시작, 실제 usage 가 None/disabled 면 False.
            "creation_tokens": 0,
            "read_tokens": 0,
            "non_cached_input_tokens": 0,
            "input_tokens": 0,
            "hit_ratio": 0.0,
        })
        return None

    async def on_tool_round_complete(
        self,
        ctx: RequestCtx,
        round_idx: int,
        usage: UsageSnapshot,
    ) -> None:
        self._accumulate(ctx, usage)

    async def on_post_response(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        self._accumulate(ctx, usage)
        self._recompute_ratio(ctx)

    def _accumulate(self, ctx: RequestCtx, usage: UsageSnapshot) -> None:
        data = ctx.metadata.setdefault(self.name, {
            "available": True,
            "creation_tokens": 0,
            "read_tokens": 0,
            "non_cached_input_tokens": 0,
            "input_tokens": 0,
            "hit_ratio": 0.0,
        })
        if not usage.available:
            # provider 가 캐시 정보를 제공하지 못함 — available=False 로 고정.
            data["available"] = False
            return

        # input_tokens 는 Anthropic SDK 가 "non-cached input" 으로 보고하는 값.
        # cache_creation 과 cache_read 는 별도 카운트 (input_tokens 와 합산 X).
        data["creation_tokens"] += int(usage.cache_creation_input_tokens or 0)
        data["read_tokens"] += int(usage.cache_read_input_tokens or 0)
        data["non_cached_input_tokens"] += int(usage.input_tokens or 0)
        data["input_tokens"] = (
            data["non_cached_input_tokens"]
            + data["creation_tokens"]
            + data["read_tokens"]
        )
        self._recompute_ratio(ctx)

    def _recompute_ratio(self, ctx: RequestCtx) -> None:
        data = ctx.metadata[self.name]
        denom = (
            data["read_tokens"]
            + data["creation_tokens"]
            + data["non_cached_input_tokens"]
        )
        if denom <= 0:
            data["hit_ratio"] = 0.0
            return
        data["hit_ratio"] = round(data["read_tokens"] / denom, 4)
