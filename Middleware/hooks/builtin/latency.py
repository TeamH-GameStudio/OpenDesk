"""
LatencyHook — TTFT / tool round 별 지연 / 총 지연 측정.

ctx.metadata["latency"] 네임스페이스에 다음을 채운다:
    {
        "started_at_mono": float,       # on_pre_request 시각 (monotonic)
        "first_token_mono": float|None, # on_first_token 시각
        "ttft_ms": int|None,            # 첫 토큰까지 ms
        "round_starts_mono": [float],   # 각 round 시작 시각 (이전 round 종료 = 이번 round 시작)
        "tool_rounds_ms": [int],        # 각 round 소요 ms
        "total_ms": int|None,           # on_post_response 시점 총 ms
    }

TelemetryEmitterHook 이 이 네임스페이스를 읽어 telemetry 이벤트의 latency 필드를 채움.
"""

from __future__ import annotations

import time
from typing import Optional

from ..base import BaseHook
from ..protocol import RequestCtx, UsageSnapshot


class LatencyHook(BaseHook):
    name = "latency"

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        # ctx.started_at 은 ChatSession 에서 이미 설정. metadata 에는 동일 값 + round 추적 위함.
        ctx.metadata.setdefault(self.name, {
            "started_at_mono": ctx.started_at,
            "first_token_mono": None,
            "ttft_ms": None,
            # round_start_mono 는 다음 round 의 시작 기준점. 첫 round 의 시작은 첫 토큰 직후가 아닌
            # provider 가 모델 호출을 다시 시작한 시점이지만, 그 시점이 명확하지 않으므로
            # tool_use_result 완료 시각을 사용하여 round 종료 → 다음 round 시작으로 간주.
            "round_starts_mono": [ctx.started_at],
            "tool_rounds_ms": [],
            "total_ms": None,
        })
        return None

    async def on_first_token(self, ctx: RequestCtx, ts: float) -> None:
        data = ctx.metadata.setdefault(self.name, {})
        if data.get("first_token_mono") is None:
            data["first_token_mono"] = ts
            started = data.get("started_at_mono", ctx.started_at)
            data["ttft_ms"] = max(0, round((ts - started) * 1000))

    async def on_tool_round_complete(
        self,
        ctx: RequestCtx,
        round_idx: int,
        usage: UsageSnapshot,
    ) -> None:
        data = ctx.metadata.setdefault(self.name, {})
        now = time.monotonic()
        starts = data.setdefault("round_starts_mono", [ctx.started_at])
        rounds_ms = data.setdefault("tool_rounds_ms", [])
        # 이번 round 의 시작 = 마지막 기록된 start. 종료 = 지금.
        last_start = starts[-1] if starts else ctx.started_at
        rounds_ms.append(max(0, round((now - last_start) * 1000)))
        # 다음 round 의 시작점 등록.
        starts.append(now)

    async def on_post_response(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        data = ctx.metadata.setdefault(self.name, {})
        started = data.get("started_at_mono", ctx.started_at)
        data["total_ms"] = max(0, round((time.monotonic() - started) * 1000))
