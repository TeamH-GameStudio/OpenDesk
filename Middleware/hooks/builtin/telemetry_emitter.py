"""
TelemetryEmitterHook — 다른 hook 들의 metadata 를 모아 WebSocket 'telemetry' 이벤트로 emit.

이 hook 은 pipeline 의 마지막에 등록되어야 한다 (post 는 역순이므로 가장 먼저 실행되고
다른 hook 의 metadata 를 읽음 — 단, 본 구현은 emit 만 하므로 등록 순서는 결과에
큰 영향을 주지 않는다. 단지 다른 hook 이 metadata 를 먼저 채워줘야 한다는 점만 보장).

전송 페이로드는 PROTOCOL.md 의 `telemetry` 이벤트 스키마와 일치한다. JsonUtility
(Unity 클라이언트) 가 fragile 한 nested null 을 우회하기 위해 누락된 필드는 빈 {} 또는
0 으로 발행한다.
"""

from __future__ import annotations

import logging
import time
from typing import Any, Awaitable, Callable, Optional

from ..base import BaseHook
from ..protocol import RequestCtx, UsageSnapshot

logger = logging.getLogger("hooks.telemetry_emitter")


# Unity → middleware → Unity 방향 (server→client) 의 WS 전송 함수 시그니처.
SendFn = Callable[[dict[str, Any]], Awaitable[None]]


class TelemetryEmitterHook(BaseHook):
    name = "telemetry_emitter"

    def __init__(
        self,
        send_fn: SendFn,
        cost_estimator: Optional[Callable[[str, UsageSnapshot], float]] = None,
        telemetry_completeness: str = "full",  # "full" | "partial"
    ):
        self._send = send_fn
        self._cost = cost_estimator or (lambda model, u: 0.0)
        self._completeness = telemetry_completeness

    async def on_first_token(self, ctx: RequestCtx, ts: float) -> None:
        # 가벼운 partial 이벤트 — TTFT 만 빠르게 UI 에 노출.
        payload = self._base_payload(ctx, event="first_token")
        payload["latency"] = {
            "ttft_ms": int(ctx.metadata.get("latency", {}).get("ttft_ms") or 0),
            "total_ms": 0,
            "tool_rounds_ms": [],
        }
        await self._safe_send(payload)

    async def on_post_response(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        payload = self._build_complete_payload(ctx, usage, has_error=False)
        await self._safe_send(payload)

    async def on_error(self, ctx: RequestCtx, error: BaseException):
        usage = ctx.cumulative_usage or UsageSnapshot(available=False)
        payload = self._build_complete_payload(
            ctx, usage, has_error=True, error=error,
        )
        payload["event"] = "error"
        await self._safe_send(payload)
        return None  # 결정은 다른 hook 에 위임.

    # ── internal ────────────────────────────────────────────────

    def _base_payload(self, ctx: RequestCtx, *, event: str) -> dict[str, Any]:
        return {
            "type": "telemetry",
            "event": event,
            "request_id": ctx.request_id,
            "agent_id": ctx.agent_id or "",
            "session_id": ctx.session_id or "",
            "provider": ctx.provider,
            "model": ctx.model,
            "timestamp": time.time(),
            "telemetry_completeness": self._completeness,
            "has_error": False,
            "error": {},
            "latency": {"ttft_ms": 0, "total_ms": 0, "tool_rounds_ms": []},
            "tokens": {
                "input": 0, "output": 0,
                "cache_creation_input": 0, "cache_read_input": 0,
            },
            "cache": {
                "available": True, "hit_ratio": 0.0,
                "creation_tokens": 0, "read_tokens": 0,
            },
            "reliability": {
                "retry_count": 0, "rate_limit_hits": 0,
                "max_tool_rounds": 0, "tool_rounds_used": 0,
                "stop_reason": "complete",
            },
            "cost_estimate_usd": 0.0,
        }

    def _build_complete_payload(
        self,
        ctx: RequestCtx,
        usage: UsageSnapshot,
        *,
        has_error: bool,
        error: Optional[BaseException] = None,
    ) -> dict[str, Any]:
        payload = self._base_payload(ctx, event="request_complete")

        # latency 채우기
        lat = ctx.metadata.get("latency", {})
        payload["latency"] = {
            "ttft_ms": int(lat.get("ttft_ms") or 0),
            "total_ms": int(lat.get("total_ms") or 0),
            "tool_rounds_ms": list(lat.get("tool_rounds_ms") or []),
        }

        # tokens
        payload["tokens"] = {
            "input": int(usage.input_tokens or 0),
            "output": int(usage.output_tokens or 0),
            "cache_creation_input": int(usage.cache_creation_input_tokens or 0),
            "cache_read_input": int(usage.cache_read_input_tokens or 0),
        }

        # cache
        cache = ctx.metadata.get("cache", {})
        payload["cache"] = {
            "available": bool(cache.get("available", usage.available)),
            "hit_ratio": float(cache.get("hit_ratio") or 0.0),
            "creation_tokens": int(cache.get("creation_tokens") or 0),
            "read_tokens": int(cache.get("read_tokens") or 0),
        }

        # reliability
        retry = ctx.metadata.get("retry", {})
        rate = ctx.metadata.get("rate_limit", {})
        attempts = retry.get("attempts", []) if isinstance(retry, dict) else []
        payload["reliability"] = {
            "retry_count": int(ctx.retry_count),
            "rate_limit_hits": int(rate.get("hits", 0)) if isinstance(rate, dict) else 0,
            "max_tool_rounds": int(ctx.metadata.get("max_tool_rounds", 0) or 0),
            "tool_rounds_used": int(ctx.tool_round),
            "stop_reason": "error" if has_error else "complete",
        }

        # cost estimate (best effort)
        try:
            payload["cost_estimate_usd"] = float(self._cost(ctx.model, usage))
        except Exception:
            payload["cost_estimate_usd"] = 0.0

        if has_error:
            payload["has_error"] = True
            payload["error"] = {
                "message": str(error) if error else "unknown",
                "code": type(error).__name__ if error else "unknown",
                "recoverable": False,
            }
        else:
            payload["has_error"] = False
            payload["error"] = {}

        return payload

    async def _safe_send(self, payload: dict[str, Any]) -> None:
        try:
            await self._send(payload)
        except Exception:
            logger.exception("telemetry send failed (isolated)")
