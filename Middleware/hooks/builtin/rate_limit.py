"""
RateLimitHook — 429 에러를 retry-after 헤더 기반으로 처리.

- RateLimitError (Anthropic SDK) 또는 status_code=429 시 retry-after 파싱.
- 헤더 없으면 보수적 기본값 사용.
- 프로세스 전역 cooldown — 한 세션이 429 맞으면 모든 세션이 잠시 대기.
  asyncio.Semaphore(1) 글로벌 게이트로 동시 재시도 storm 방지.

다른 hook 들의 retry/escalate 결정보다 먼저 실행되어야 함 (pipeline 등록 시 순서 보장).
"""

from __future__ import annotations

import asyncio
import logging
import time
from typing import Optional

from ..base import BaseHook
from ..protocol import ErrorAction, RequestCtx

logger = logging.getLogger("hooks.rate_limit")


def _parse_retry_after(value: str | None) -> Optional[float]:
    """retry-after 헤더 파싱. 정수 초 또는 HTTP-date 둘 다 허용.

    HTTP-date 는 일단 미지원 — 정수 초만. 잘못된 값은 None.
    """
    if value is None:
        return None
    raw = str(value).strip()
    if not raw:
        return None
    try:
        return max(0.0, float(raw))
    except ValueError:
        return None


def _extract_retry_after(error: BaseException) -> Optional[float]:
    """SDK 예외 객체에서 retry-after 헤더 추출. SDK 마다 다른 구조 대응."""
    # 1) error.response.headers["retry-after"]
    response = getattr(error, "response", None)
    if response is not None:
        headers = getattr(response, "headers", None)
        if headers is not None:
            try:
                value = headers.get("retry-after")
            except AttributeError:
                value = None
            parsed = _parse_retry_after(value)
            if parsed is not None:
                return parsed

    # 2) error.headers["retry-after"]
    headers = getattr(error, "headers", None)
    if headers is not None:
        try:
            value = headers.get("retry-after")
        except AttributeError:
            value = None
        parsed = _parse_retry_after(value)
        if parsed is not None:
            return parsed

    return None


def _is_rate_limit(error: BaseException) -> bool:
    """RateLimitError 또는 429 상태 코드."""
    if type(error).__name__ == "RateLimitError":
        return True
    status_code = getattr(error, "status_code", None)
    if status_code is not None and int(status_code) == 429:
        return True
    # http 라이브러리 별 status 필드 (httpx 등).
    status = getattr(error, "status", None)
    if status is not None and int(status) == 429:
        return True
    return False


class RateLimitHook(BaseHook):
    name = "rate_limit"

    # 클래스 레벨 — 프로세스 전역 cooldown. 모든 hook 인스턴스가 공유.
    _global_gate: asyncio.Lock = asyncio.Lock()
    _global_cooldown_until: float = 0.0

    def __init__(
        self,
        default_backoff_seconds: float = 5.0,
        max_backoff_seconds: float = 60.0,
        max_retries: int = 3,
    ):
        self._default_backoff = default_backoff_seconds
        self._max_backoff = max_backoff_seconds
        self._max_retries = max_retries

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        ctx.metadata.setdefault(self.name, {"hits": 0, "last_retry_after": None})
        # 글로벌 cooldown 이 active 면 진입 대기 — HookedProvider 가 reqeust 자체를 지연시킴.
        # (현재 단계에서는 hook 이 sleep 호출하지 않고 metadata 만 기록. 실제 대기는
        #  RetryHook 의 backoff 와 마찬가지로 HookedProvider 에 의해 처리.)
        return None

    async def on_error(
        self,
        ctx: RequestCtx,
        error: BaseException,
    ) -> Optional[ErrorAction]:
        if not _is_rate_limit(error):
            return None
        if ctx.retry_count >= self._max_retries:
            return ErrorAction(
                kind="escalate",
                reason=f"rate limit retries exhausted ({self._max_retries})",
            )

        retry_after = _extract_retry_after(error)
        backoff = retry_after if retry_after is not None else self._default_backoff
        backoff = min(backoff, self._max_backoff)

        data = ctx.metadata.setdefault(self.name, {"hits": 0, "last_retry_after": None})
        data["hits"] = int(data.get("hits", 0)) + 1
        data["last_retry_after"] = retry_after
        # 글로벌 cooldown 도 함께 갱신 (다른 세션이 진입 시 동일 대기).
        type(self)._global_cooldown_until = max(
            type(self)._global_cooldown_until,
            time.monotonic() + backoff,
        )

        return ErrorAction(
            kind="retry",
            backoff_seconds=backoff,
            reason="rate limited",
        )

    @classmethod
    def global_cooldown_remaining(cls) -> float:
        """글로벌 cooldown 까지 남은 시간 (초). 0 이하면 대기 불필요."""
        remaining = cls._global_cooldown_until - time.monotonic()
        return max(0.0, remaining)

    @classmethod
    def reset_global_cooldown(cls) -> None:
        """테스트용 — cooldown 강제 리셋."""
        cls._global_cooldown_until = 0.0
