"""
Hook Protocol — middleware 의 모든 lifecycle 후킹 지점 정의.

MessageHook 은 5개의 async 메서드로 구성된 Protocol:
- on_pre_request:  요청 진입. ctx 재작성/차단 가능.
- on_first_token:  첫 텍스트 토큰 수신. TTFT 측정 지점.
- on_tool_round_complete: tool_use 1라운드 종료. 누적 usage 기록.
- on_post_response: 정상 종료. 최종 텍스트 + usage.
- on_error:        예외. ErrorAction 으로 retry/escalate/suppress 결정.

각 메서드는 BaseHook 의 no-op 기본 구현을 상속받아 필요한 것만 override.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal, Optional, Protocol, runtime_checkable

# ── 데이터 객체 ────────────────────────────────────────────────


@dataclass
class UsageSnapshot:
    """1회 요청(또는 1 round) 분량의 토큰/캐시 사용량.

    cache_* 필드가 0 이고 available=False 면 provider 가 캐시 통계를 보고하지 않음
    (CLI provider 등). 0 과 "보고 안함" 을 구분해야 ratio 계산에서 0/0 misleading 회피.
    """

    input_tokens: int = 0
    output_tokens: int = 0
    cache_creation_input_tokens: int = 0
    cache_read_input_tokens: int = 0
    available: bool = True


@dataclass
class RequestCtx:
    """1회 chat 요청의 lifecycle 동안 모든 hook 이 공유하는 상태.

    metadata 는 hook 별 네임스페이스 (key=hook.name) 로 사용하여 글로벌 mutable
    state 를 회피한다. 예: ctx.metadata["latency"] = {"ttft_ms": 412, ...}
    """

    request_id: str
    agent_id: str
    session_id: str
    provider: str  # "anthropic_api" | "anthropic_cli" | ...
    model: str
    started_at: float  # time.monotonic() 시각
    tool_round: int = 0
    retry_count: int = 0
    metadata: dict[str, Any] = field(default_factory=dict)
    cumulative_usage: UsageSnapshot = field(default_factory=UsageSnapshot)


ErrorActionKind = Literal["suppress", "retry", "escalate"]


@dataclass(frozen=True)
class ErrorAction:
    """on_error 의 결정. retry 면 backoff_seconds 만큼 대기 후 재시도.

    - retry:    backoff 후 동일 요청 재시도
    - escalate: 호출자에게 예외 전파 (기본)
    - suppress: 조용히 무시. 모든 hook 이 suppress 를 반환해야 적용 (만장일치).
    """

    kind: ErrorActionKind
    backoff_seconds: float = 0.0
    reason: str = ""


# ── Protocol ────────────────────────────────────────────────────


@runtime_checkable
class MessageHook(Protocol):
    """Hook 이 구현해야 하는 인터페이스.

    name 은 logging / ctx.metadata 네임스페이스 키로 쓰이므로 고유해야 한다.
    BaseHook 를 상속하면 모든 메서드의 no-op 기본 구현을 얻는다.
    """

    name: str

    async def on_pre_request(self, ctx: RequestCtx) -> Optional[RequestCtx]:
        """요청 진입. 수정된 ctx 반환 가능. None 반환 시 차단 (blocking hook 한정)."""
        ...

    async def on_first_token(self, ctx: RequestCtx, ts: float) -> None:
        """첫 TextDeltaEvent 도착. ts 는 time.monotonic()."""
        ...

    async def on_tool_round_complete(
        self,
        ctx: RequestCtx,
        round_idx: int,
        usage: UsageSnapshot,
    ) -> None:
        """tool_use 1 라운드 완료. usage 는 해당 라운드의 누적치 (provider 별로 의미 다를 수 있음)."""
        ...

    async def on_post_response(
        self,
        ctx: RequestCtx,
        final_text: str,
        usage: UsageSnapshot,
    ) -> None:
        """MessageStopEvent(reason='complete') 후 1회 호출."""
        ...

    async def on_error(
        self,
        ctx: RequestCtx,
        error: BaseException,
    ) -> Optional[ErrorAction]:
        """예외 발생. None 반환 시 다음 hook 결정에 양보. retry/escalate 가 우선."""
        ...
