"""
Mock routing server — `OPENDESK_ROUTING_URL=embedded` 일 때 동작하는 인메모리 라우터.

실 클라우드 서버 없이 Unity ↔ Middleware 통합 동작을 검증하기 위한 stub.
- 라우팅: 메시지 길이 기반 단순 휴리스틱 (실 Haiku 호출 없음).
- 크레딧: asyncio.Lock 으로 보호되는 인메모리 dict.
- 라이선스: license_key -> (user_id, fingerprints[]) dict.

미들웨어 프로세스 재시작 시 상태 휘발. 개발/테스트 한정.
"""

from __future__ import annotations

import asyncio
import logging
import secrets
import time
from dataclasses import dataclass, field
from typing import Final

from credit_policy import (
    TIER_LARGE,
    TIER_MEDIUM,
    TIER_SMALL,
    Tier,
    tokens_to_credits,
)


logger = logging.getLogger(__name__)


# ── 도메인 타입 ───────────────────────────────────────────────

@dataclass(frozen=True)
class RoutingDecision:
    task_id: str
    model: str
    tier_name: str
    estimated_credits: int
    reasoning: str
    escalation_allowed: bool
    expected_tool_calls: int


@dataclass(frozen=True)
class HoldResult:
    hold_id: str
    balance_after: int


@dataclass(frozen=True)
class SettleResult:
    actual_credits: int
    balance_after: int


@dataclass(frozen=True)
class ActivationResult:
    jwt: str
    refresh_token: str
    user_id: str
    plan_tier: str
    balance: int


@dataclass(frozen=True)
class BalanceResult:
    balance: int
    held: int


class RoutingDeniedError(RuntimeError):
    """크레딧 부족 등으로 라우팅 거부."""

    def __init__(self, code: str, message: str, required: int = 0, balance: int = 0):
        super().__init__(message)
        self.code = code
        self.required = required
        self.balance = balance


class LicenseError(RuntimeError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


# ── 인메모리 저장소 ──────────────────────────────────────────

@dataclass
class _UserState:
    user_id: str
    balance: int
    holds: dict[str, int] = field(default_factory=dict)


@dataclass
class _LicenseState:
    user_id: str
    plan_tier: str
    max_devices: int
    fingerprints: dict[str, str] = field(default_factory=dict)  # fp -> device_name


# 기본 시드: TEST-1000 키로 user-test 활성화 가능. 잔액 1000.
_DEFAULT_INITIAL_BALANCE: Final[int] = 1000


class EmbeddedMockServer:
    """싱글톤 인메모리 mock 서버."""

    def __init__(self, initial_balance: int = _DEFAULT_INITIAL_BALANCE) -> None:
        self._initial_balance = initial_balance
        self._users: dict[str, _UserState] = {}
        self._licenses: dict[str, _LicenseState] = {}
        self._jwts: dict[str, str] = {}  # jwt -> user_id
        self._lock = asyncio.Lock()
        self._seed_default_license()

    def _seed_default_license(self) -> None:
        self._licenses["TEST-1000"] = _LicenseState(
            user_id="user-test",
            plan_tier="pro",
            max_devices=2,
        )
        self._users["user-test"] = _UserState(
            user_id="user-test",
            balance=self._initial_balance,
        )

    # ── 라이선스 활성화 ─────────────────────────────────────

    async def activate(
        self,
        license_key: str,
        fingerprint: str,
        device_name: str,
    ) -> ActivationResult:
        async with self._lock:
            lic = self._licenses.get(license_key)
            if lic is None:
                raise LicenseError("invalid_license", f"unknown license: {license_key}")

            if fingerprint not in lic.fingerprints:
                if len(lic.fingerprints) >= lic.max_devices:
                    raise LicenseError(
                        "device_limit_reached",
                        f"license {license_key} reached device cap {lic.max_devices}",
                    )
                lic.fingerprints[fingerprint] = device_name

            jwt = f"mock-jwt-{secrets.token_urlsafe(16)}"
            refresh = f"mock-refresh-{secrets.token_urlsafe(16)}"
            self._jwts[jwt] = lic.user_id

            user = self._users[lic.user_id]
            return ActivationResult(
                jwt=jwt,
                refresh_token=refresh,
                user_id=lic.user_id,
                plan_tier=lic.plan_tier,
                balance=user.balance,
            )

    def resolve_jwt(self, jwt: str) -> str:
        user_id = self._jwts.get(jwt)
        if user_id is None:
            raise LicenseError("invalid_jwt", f"unknown jwt: {jwt[:16]}...")
        return user_id

    # ── 라우팅 결정 ────────────────────────────────────────

    async def route(
        self,
        user_id: str,
        user_task: str,
        agent_role: str,
        available_tools: list[str],
        user_complexity_hint: str,
        requested_model: str,
    ) -> RoutingDecision:
        """단순 휴리스틱 라우터.

        실 Haiku 호출 없이:
            - hint=simple 또는 task < 500자 + tool ≤ 3 → haiku/small
            - hint=complex 또는 task > 2000자 또는 tool > 8 → sonnet/large
            - 그 외 → haiku/medium
        requested_model 이 명시되어 있으면 그대로 따른다 (사용자 강제).
        """
        async with self._lock:
            if user_id not in self._users:
                raise LicenseError("unknown_user", f"user not found: {user_id}")

        task_len = len(user_task)
        tool_count = len(available_tools)

        if user_complexity_hint == "complex" or task_len > 2000 or tool_count > 8:
            model = "claude-sonnet-4-5"
            tier = TIER_LARGE
            reasoning = "Complex task or many tools"
            escalation = False
            expected_tools = max(8, tool_count)
        elif user_complexity_hint == "simple" or (task_len < 500 and tool_count <= 3):
            model = "claude-haiku-4-5"
            tier = TIER_SMALL
            reasoning = "Short single-step task"
            escalation = True
            expected_tools = min(3, tool_count)
        else:
            model = "claude-haiku-4-5"
            tier = TIER_MEDIUM
            reasoning = "Moderate task, haiku with tool calls"
            escalation = True
            expected_tools = min(6, tool_count)

        # 강제 모델 hint (claude-opus / claude-sonnet …)
        if requested_model and "auto" not in requested_model.lower():
            model = requested_model
            tier = _force_model_tier(requested_model, tier)

        return RoutingDecision(
            task_id=f"task-{secrets.token_urlsafe(8)}",
            model=model,
            tier_name=tier.name,
            estimated_credits=tier.estimated_credits,
            reasoning=reasoning,
            escalation_allowed=escalation,
            expected_tool_calls=expected_tools,
        )

    # ── 크레딧 hold / settle / refund ───────────────────────

    async def hold(self, user_id: str, amount: int, task_id: str) -> HoldResult:
        async with self._lock:
            user = self._require_user(user_id)
            if user.balance < amount:
                raise RoutingDeniedError(
                    code="insufficient_credits",
                    message=f"need {amount}, have {user.balance}",
                    required=amount,
                    balance=user.balance,
                )
            user.balance -= amount
            hold_id = f"hold-{secrets.token_urlsafe(12)}"
            user.holds[hold_id] = amount
            logger.info(
                "[mock] hold user=%s amount=%d hold_id=%s balance_after=%d",
                user_id, amount, hold_id, user.balance,
            )
            return HoldResult(hold_id=hold_id, balance_after=user.balance)

    async def settle(
        self,
        user_id: str,
        hold_id: str,
        model_id: str,
        input_tokens: int,
        output_tokens: int,
    ) -> SettleResult:
        async with self._lock:
            user = self._require_user(user_id)
            held = user.holds.pop(hold_id, None)
            if held is None:
                raise RoutingDeniedError(
                    code="unknown_hold",
                    message=f"hold not found: {hold_id}",
                )

            actual = tokens_to_credits(model_id, input_tokens, output_tokens)
            overage_cap = max(int(held * 1.10), held)  # 10% 초과 허용
            charged = min(actual, overage_cap)
            refund = held - charged
            if refund > 0:
                user.balance += refund

            logger.info(
                "[mock] settle user=%s hold=%s held=%d actual=%d charged=%d balance=%d",
                user_id, hold_id, held, actual, charged, user.balance,
            )
            return SettleResult(actual_credits=charged, balance_after=user.balance)

    async def refund(self, user_id: str, hold_id: str, reason: str) -> BalanceResult:
        async with self._lock:
            user = self._require_user(user_id)
            held = user.holds.pop(hold_id, None)
            if held is None:
                # 이미 settle 됐거나 모르는 hold — 0 환불, 현재 잔액만 반환
                return BalanceResult(balance=user.balance, held=sum(user.holds.values()))
            user.balance += held
            logger.info(
                "[mock] refund user=%s hold=%s held=%d reason=%s balance=%d",
                user_id, hold_id, held, reason, user.balance,
            )
            return BalanceResult(balance=user.balance, held=sum(user.holds.values()))

    async def balance(self, user_id: str) -> BalanceResult:
        async with self._lock:
            user = self._require_user(user_id)
            return BalanceResult(
                balance=user.balance,
                held=sum(user.holds.values()),
            )

    # ── 테스트 헬퍼 ─────────────────────────────────────────

    async def admin_set_balance(self, user_id: str, balance: int) -> None:
        """테스트용. 잔액을 직접 설정."""
        async with self._lock:
            user = self._users.setdefault(user_id, _UserState(user_id=user_id, balance=0))
            user.balance = balance

    async def admin_grant_license(
        self,
        license_key: str,
        user_id: str,
        plan_tier: str = "pro",
        max_devices: int = 2,
        initial_balance: int | None = None,
    ) -> None:
        """테스트용. 라이선스/사용자 사전 등록."""
        async with self._lock:
            self._licenses[license_key] = _LicenseState(
                user_id=user_id,
                plan_tier=plan_tier,
                max_devices=max_devices,
            )
            self._users.setdefault(
                user_id,
                _UserState(
                    user_id=user_id,
                    balance=initial_balance if initial_balance is not None else self._initial_balance,
                ),
            )

    def _require_user(self, user_id: str) -> _UserState:
        user = self._users.get(user_id)
        if user is None:
            raise RoutingDeniedError("unknown_user", f"user not found: {user_id}")
        return user


# ── 싱글톤 접근 ──────────────────────────────────────────────

_singleton: EmbeddedMockServer | None = None


def get_embedded_server() -> EmbeddedMockServer:
    global _singleton
    if _singleton is None:
        _singleton = EmbeddedMockServer()
    return _singleton


def reset_embedded_server() -> None:
    """테스트 용. 싱글톤 폐기."""
    global _singleton
    _singleton = None


# ── 강제 모델 → tier 매핑 ────────────────────────────────────

def _force_model_tier(model_id: str, default_tier: Tier) -> Tier:
    """사용자가 모델을 직접 골랐을 때 tier 추정."""
    lowered = model_id.lower()
    if "opus" in lowered:
        return TIER_LARGE
    if "sonnet" in lowered:
        return TIER_MEDIUM
    if "haiku" in lowered:
        return TIER_SMALL
    return default_tier
