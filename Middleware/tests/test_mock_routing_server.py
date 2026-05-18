"""mock_routing_server — 인메모리 라우터/크레딧/라이선스 동작 검증."""

from __future__ import annotations

import pytest

from mock_routing_server import (
    EmbeddedMockServer,
    LicenseError,
    RoutingDeniedError,
    reset_embedded_server,
)


@pytest.fixture
def server():
    reset_embedded_server()
    return EmbeddedMockServer(initial_balance=1000)


@pytest.mark.unit
class TestActivation:
    @pytest.mark.asyncio
    async def test_activate_seeded_license(self, server: EmbeddedMockServer):
        result = await server.activate("TEST-1000", "fp-mac-1", "MacBook")
        assert result.user_id == "user-test"
        assert result.balance == 1000
        assert result.jwt.startswith("mock-jwt-")

    @pytest.mark.asyncio
    async def test_activate_unknown_license_raises(self, server: EmbeddedMockServer):
        with pytest.raises(LicenseError) as exc:
            await server.activate("UNKNOWN", "fp-1", "Test")
        assert exc.value.code == "invalid_license"

    @pytest.mark.asyncio
    async def test_activate_respects_device_limit(self, server: EmbeddedMockServer):
        await server.activate("TEST-1000", "fp-1", "Dev1")
        await server.activate("TEST-1000", "fp-2", "Dev2")
        with pytest.raises(LicenseError) as exc:
            await server.activate("TEST-1000", "fp-3", "Dev3")
        assert exc.value.code == "device_limit_reached"

    @pytest.mark.asyncio
    async def test_reactivate_same_fingerprint_ok(self, server: EmbeddedMockServer):
        first = await server.activate("TEST-1000", "fp-1", "Dev1")
        second = await server.activate("TEST-1000", "fp-1", "Dev1-renamed")
        assert first.user_id == second.user_id
        # 같은 fingerprint 재활성화는 디바이스 카운트에 영향 없음
        await server.activate("TEST-1000", "fp-2", "Dev2")  # still ok


@pytest.mark.unit
class TestRouting:
    @pytest.mark.asyncio
    async def test_short_task_picks_haiku_small(self, server: EmbeddedMockServer):
        decision = await server.route(
            user_id="user-test",
            user_task="hi",
            agent_role="default",
            available_tools=[],
            user_complexity_hint="auto",
            requested_model="",
        )
        assert "haiku" in decision.model.lower()
        assert decision.tier_name == "small"
        assert decision.estimated_credits == 30

    @pytest.mark.asyncio
    async def test_long_task_picks_sonnet_large(self, server: EmbeddedMockServer):
        decision = await server.route(
            user_id="user-test",
            user_task="x" * 3000,
            agent_role="researcher",
            available_tools=["a", "b"],
            user_complexity_hint="auto",
            requested_model="",
        )
        assert "sonnet" in decision.model.lower()
        assert decision.tier_name == "large"

    @pytest.mark.asyncio
    async def test_complex_hint_forces_large(self, server: EmbeddedMockServer):
        decision = await server.route(
            user_id="user-test",
            user_task="hi",
            agent_role="default",
            available_tools=[],
            user_complexity_hint="complex",
            requested_model="",
        )
        assert decision.tier_name == "large"

    @pytest.mark.asyncio
    async def test_explicit_model_overrides(self, server: EmbeddedMockServer):
        decision = await server.route(
            user_id="user-test",
            user_task="hi",
            agent_role="default",
            available_tools=[],
            user_complexity_hint="auto",
            requested_model="claude-opus-4-7",
        )
        assert decision.model == "claude-opus-4-7"
        assert decision.tier_name == "large"


@pytest.mark.unit
class TestCreditFlow:
    @pytest.mark.asyncio
    async def test_hold_consume_normal(self, server: EmbeddedMockServer):
        hold = await server.hold("user-test", 100, "task-1")
        assert hold.balance_after == 900

        # haiku 100 in + 50 out → 매우 적은 토큰 → settle 시 환불
        settle = await server.settle("user-test", hold.hold_id, "haiku", 100, 50)
        assert settle.actual_credits < 100  # 환불 발생
        assert settle.balance_after > 900  # 잔액 회복 (1000 - 100 + refund)

    @pytest.mark.asyncio
    async def test_hold_insufficient_raises(self, server: EmbeddedMockServer):
        with pytest.raises(RoutingDeniedError) as exc:
            await server.hold("user-test", 5000, "task-x")
        assert exc.value.code == "insufficient_credits"
        assert exc.value.required == 5000
        assert exc.value.balance == 1000

    @pytest.mark.asyncio
    async def test_refund_returns_full_amount(self, server: EmbeddedMockServer):
        hold = await server.hold("user-test", 200, "task-2")
        assert (await server.balance("user-test")).balance == 800

        await server.refund("user-test", hold.hold_id, reason="error")
        assert (await server.balance("user-test")).balance == 1000

    @pytest.mark.asyncio
    async def test_double_settle_or_refund_raises(self, server: EmbeddedMockServer):
        hold = await server.hold("user-test", 50, "task-3")
        await server.settle("user-test", hold.hold_id, "haiku", 10, 10)
        # 두 번째 settle → unknown_hold
        with pytest.raises(RoutingDeniedError) as exc:
            await server.settle("user-test", hold.hold_id, "haiku", 10, 10)
        assert exc.value.code == "unknown_hold"

    @pytest.mark.asyncio
    async def test_overage_capped_at_10pct(self, server: EmbeddedMockServer):
        # hold 30, opus 매우 큰 사용량 → 정상 settle 비용 >> 30
        hold = await server.hold("user-test", 30, "task-over")
        settle = await server.settle("user-test", hold.hold_id, "claude-opus-4-7", 1_000_000, 500_000)
        # 캡 = ceil(30 * 1.10) = 33
        assert settle.actual_credits <= 33

    @pytest.mark.asyncio
    async def test_unknown_user_raises(self, server: EmbeddedMockServer):
        with pytest.raises(RoutingDeniedError) as exc:
            await server.hold("user-ghost", 10, "task-y")
        assert exc.value.code == "unknown_user"
