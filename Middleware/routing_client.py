"""
Routing client — 백엔드 라우팅 서버와 통신하는 HTTP 클라이언트.

OPENDESK_ROUTING_URL 환경변수로 동작 모드를 분기:
    - "embedded" 또는 미설정 → 인메모리 mock 서버 (mock_routing_server.py)
    - http(s)://... → 실 클라우드 서버 (FastAPI, 후속 구현)

provider 가 이 클라이언트를 통해 라우팅/hold/settle/refund 를 호출하므로
실서버 구현이 끝나면 OPENDESK_ROUTING_URL 만 바꿔서 그대로 운영 전환 가능.

JWT 인증: bind_user(jwt) 로 라이선스 토큰을 주입. 모든 호출에 Authorization 헤더로 전달.
mock 모드에서는 jwt → user_id 변환만 수행.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from typing import Optional

from mock_routing_server import (
    ActivationResult,
    BalanceResult,
    EmbeddedMockServer,
    HoldResult,
    LicenseError,
    RoutingDecision,
    RoutingDeniedError,
    SettleResult,
    get_embedded_server,
)


logger = logging.getLogger(__name__)


# ── 환경 ─────────────────────────────────────────────────────

_DEFAULT_TIMEOUT_SEC = 15.0


def _routing_url() -> str:
    return os.environ.get("OPENDESK_ROUTING_URL", "embedded").strip()


def _is_embedded(url: str) -> bool:
    return url == "" or url.lower() == "embedded"


# ── 클라이언트 ───────────────────────────────────────────────

@dataclass
class _UserBinding:
    jwt: str
    user_id: str


class RoutingClient:
    """라우팅 + 크레딧 + 라이선스 통합 클라이언트.

    embedded 모드에서는 mock 서버를 직접 호출. http 모드는 추후 httpx 로 구현.
    """

    def __init__(
        self,
        url: Optional[str] = None,
        timeout_sec: float = _DEFAULT_TIMEOUT_SEC,
    ) -> None:
        self._url = url if url is not None else _routing_url()
        self._timeout_sec = timeout_sec
        self._binding: _UserBinding | None = None
        self._mock: EmbeddedMockServer | None = (
            get_embedded_server() if _is_embedded(self._url) else None
        )

    @property
    def is_embedded(self) -> bool:
        return self._mock is not None

    @property
    def is_authenticated(self) -> bool:
        return self._binding is not None

    def bind_user(self, jwt: str) -> None:
        """라이선스 JWT 를 주입. 실패 시 LicenseError."""
        if not jwt:
            raise LicenseError("missing_jwt", "jwt is required")

        if self._mock is not None:
            user_id = self._mock.resolve_jwt(jwt)
            self._binding = _UserBinding(jwt=jwt, user_id=user_id)
            logger.info("[routing] bound user=%s (mock)", user_id)
            return

        # TODO: 실서버 모드 — POST /auth/whoami 로 user_id 회수
        raise NotImplementedError("http mode not implemented yet — set OPENDESK_ROUTING_URL=embedded")

    def unbind(self) -> None:
        self._binding = None

    def _require_binding(self) -> _UserBinding:
        if self._binding is None:
            raise LicenseError("not_authenticated", "call bind_user() first")
        return self._binding

    # ── 라이선스 ────────────────────────────────────────────

    async def activate(
        self,
        license_key: str,
        fingerprint: str,
        device_name: str,
    ) -> ActivationResult:
        if self._mock is not None:
            return await self._mock.activate(license_key, fingerprint, device_name)
        raise NotImplementedError("http activate not implemented")

    # ── 라우팅 + 크레딧 ─────────────────────────────────────

    async def route(
        self,
        user_task: str,
        agent_role: str,
        available_tools: list[str],
        user_complexity_hint: str,
        requested_model: str,
    ) -> RoutingDecision:
        binding = self._require_binding()
        if self._mock is not None:
            return await self._mock.route(
                user_id=binding.user_id,
                user_task=user_task,
                agent_role=agent_role,
                available_tools=available_tools,
                user_complexity_hint=user_complexity_hint,
                requested_model=requested_model,
            )
        raise NotImplementedError("http route not implemented")

    async def hold(self, amount: int, task_id: str) -> HoldResult:
        binding = self._require_binding()
        if self._mock is not None:
            return await self._mock.hold(binding.user_id, amount, task_id)
        raise NotImplementedError("http hold not implemented")

    async def settle(
        self,
        hold_id: str,
        model_id: str,
        input_tokens: int,
        output_tokens: int,
    ) -> SettleResult:
        binding = self._require_binding()
        if self._mock is not None:
            return await self._mock.settle(
                user_id=binding.user_id,
                hold_id=hold_id,
                model_id=model_id,
                input_tokens=input_tokens,
                output_tokens=output_tokens,
            )
        raise NotImplementedError("http settle not implemented")

    async def refund(self, hold_id: str, reason: str) -> BalanceResult:
        binding = self._require_binding()
        if self._mock is not None:
            return await self._mock.refund(binding.user_id, hold_id, reason)
        raise NotImplementedError("http refund not implemented")

    async def balance(self) -> BalanceResult:
        binding = self._require_binding()
        if self._mock is not None:
            return await self._mock.balance(binding.user_id)
        raise NotImplementedError("http balance not implemented")


# ── 싱글톤 (process-wide) ────────────────────────────────────
#
# server.py 가 process 시작 시 1개 생성하고 모든 ChatSession 이 공유.

_client: RoutingClient | None = None


def get_routing_client() -> RoutingClient:
    global _client
    if _client is None:
        _client = RoutingClient()
    return _client


def reset_routing_client() -> None:
    """테스트용."""
    global _client
    _client = None
