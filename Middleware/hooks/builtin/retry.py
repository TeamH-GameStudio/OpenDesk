"""
RetryHook — transient 에러에 jittered exponential backoff 로 재시도 결정.

대상 에러:
- APIConnectionError / APITimeoutError (Anthropic SDK)
- HTTP 5xx (InternalServerError)
- 일반 OSError / ConnectionError

대상이 아닌 에러:
- 429 (RateLimitHook 가 처리)
- 4xx (입력 문제 — escalate)
- KeyboardInterrupt / SystemExit

backoff: min(2^retry_count + uniform(0, 0.5), max_backoff_seconds)
max_retries 초과 시 escalate.

retry_count 는 RequestCtx 의 필드를 사용 — HookedProvider 가 재시도 시 증가시켜야 한다.
현재는 hook 이 결정만 내림. 실제 재시도 wiring 은 Day 7/9 에서 HookedProvider 측에 추가.
"""

from __future__ import annotations

import logging
import random
from typing import Optional, Type

from ..base import BaseHook
from ..protocol import ErrorAction, RequestCtx

logger = logging.getLogger("hooks.retry")


# 분류 헬퍼 — anthropic SDK 가 설치 안 된 환경에서도 import 가능하도록 lazy lookup.

_RETRIABLE_NAMES = {
    "APIConnectionError",
    "APITimeoutError",
    "InternalServerError",
    "ConnectionError",
    "TimeoutError",
}

_NON_RETRIABLE_NAMES = {
    "RateLimitError",   # → RateLimitHook
    "BadRequestError",
    "AuthenticationError",
    "PermissionDeniedError",
    "NotFoundError",
    "UnprocessableEntityError",
}


def _is_retriable(error: BaseException) -> bool:
    """타입 이름으로 retriable 판정. 5xx 만 retry 대상.

    우선순위:
      1. 명시적 non-retriable 이름 → False (4xx 류 명확 차단)
      2. status_code 있는 경우 5xx 만 retry
      3. 명시적 retriable 이름 → True
      4. ConnectionError / OSError → True
      5. 그 외 → False (보수적)
    """
    cls_name = type(error).__name__
    if cls_name in _NON_RETRIABLE_NAMES:
        return False

    # status_code 가 있으면 5xx 만 retry — 4xx 는 즉시 False.
    # (cls_name 이 _RETRIABLE_NAMES 에 있어도 status_code 가 우선)
    status_code = getattr(error, "status_code", None)
    if status_code is not None:
        return 500 <= int(status_code) < 600

    if cls_name in _RETRIABLE_NAMES:
        return True

    if isinstance(error, (ConnectionError, OSError)):
        return True

    return False


class RetryHook(BaseHook):
    name = "retry"

    def __init__(
        self,
        max_retries: int = 3,
        base_seconds: float = 1.0,
        max_backoff_seconds: float = 30.0,
        jitter_max: float = 0.5,
    ):
        self._max_retries = max_retries
        self._base = base_seconds
        self._max_backoff = max_backoff_seconds
        self._jitter_max = jitter_max

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        ctx.metadata.setdefault(self.name, {"attempts": []})
        return None

    async def on_error(
        self,
        ctx: RequestCtx,
        error: BaseException,
    ) -> Optional[ErrorAction]:
        if not _is_retriable(error):
            return None
        if ctx.retry_count >= self._max_retries:
            return ErrorAction(
                kind="escalate",
                reason=f"retry budget exhausted ({self._max_retries})",
            )

        backoff = self._compute_backoff(ctx.retry_count)
        ctx.metadata.setdefault(self.name, {"attempts": []})["attempts"].append({
            "retry_count": ctx.retry_count,
            "error_type": type(error).__name__,
            "backoff_seconds": backoff,
        })
        return ErrorAction(
            kind="retry",
            backoff_seconds=backoff,
            reason=f"retriable {type(error).__name__}",
        )

    def _compute_backoff(self, retry_count: int) -> float:
        # exp = 2^retry_count * base. 첫 retry: 1s, 2nd: 2s, 3rd: 4s.
        exp = self._base * (2 ** retry_count)
        jitter = random.uniform(0.0, self._jitter_max)
        return min(exp + jitter, self._max_backoff)
