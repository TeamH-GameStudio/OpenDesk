"""
build_default_pipeline — config.json 의 hook 설정으로부터 HookPipeline 생성.

config 의 `hooks.chain` 리스트가 이 빌더의 입력이다. 알 수 없는 이름은 경고 + skip.
TelemetryEmitter 는 항상 체인의 마지막에 위치 (다른 hook 의 metadata 를 읽기 위해).
"""

from __future__ import annotations

import logging
from typing import Any, Awaitable, Callable, Optional

from .builtin import (
    CacheStatHook,
    LatencyHook,
    RateLimitHook,
    RetryHook,
    TelemetryEmitterHook,
)
from .pipeline import HookPipeline
from .protocol import MessageHook, UsageSnapshot

logger = logging.getLogger("hooks.builders")


SendFn = Callable[[dict[str, Any]], Awaitable[None]]
CostEstimator = Callable[[str, UsageSnapshot], float]


_BUILDERS: dict[str, Callable[..., MessageHook]] = {
    "retry": lambda cfg, **_: RetryHook(
        max_retries=int(cfg.get("max_retries", 3)),
        base_seconds=float(cfg.get("base_seconds", 1.0)),
        max_backoff_seconds=float(cfg.get("max_backoff_seconds", 30.0)),
        jitter_max=float(cfg.get("jitter_max", 0.5)),
    ),
    "rate_limit": lambda cfg, **_: RateLimitHook(
        default_backoff_seconds=float(cfg.get("default_backoff_seconds", 5.0)),
        max_backoff_seconds=float(cfg.get("max_backoff_seconds", 60.0)),
        max_retries=int(cfg.get("max_retries", 3)),
    ),
    "latency": lambda cfg, **_: LatencyHook(),
    "cache_stats": lambda cfg, **_: CacheStatHook(),
}


def build_default_pipeline(
    config: dict[str, Any],
    send_fn: Optional[SendFn] = None,
    cost_estimator: Optional[CostEstimator] = None,
    telemetry_completeness: str = "full",
) -> HookPipeline:
    """config 에서 hooks 블록을 읽고 pipeline 을 구성.

    예시 config:
        {
          "hooks": {
            "enabled": true,
            "chain": ["retry", "rate_limit", "latency", "cache_stats", "telemetry_emitter"],
            "retry":   {"max_retries": 3, "base_seconds": 1.0},
            "rate_limit": {"default_backoff_seconds": 5.0},
          }
        }

    send_fn 이 None 이면 TelemetryEmitter 는 등록되지 않는다 (테스트 환경 등).
    """
    hooks_cfg = config.get("hooks", {})
    chain = hooks_cfg.get("chain", ["retry", "rate_limit", "latency", "cache_stats", "telemetry_emitter"])

    built: list[MessageHook] = []
    for name in chain:
        if name == "telemetry_emitter":
            continue  # 마지막에 별도로 추가.
        builder = _BUILDERS.get(name)
        if builder is None:
            logger.warning("unknown hook name in chain: %s (skipping)", name)
            continue
        sub_cfg = hooks_cfg.get(name, {})
        try:
            built.append(builder(sub_cfg))
        except Exception:
            logger.exception("failed to build hook %s — skipping", name)

    if send_fn is not None:
        built.append(TelemetryEmitterHook(
            send_fn=send_fn,
            cost_estimator=cost_estimator,
            telemetry_completeness=telemetry_completeness,
        ))

    return HookPipeline(built)
