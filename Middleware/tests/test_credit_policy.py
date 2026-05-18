"""credit_policy 단위 테스트."""

from __future__ import annotations

import pytest

from credit_policy import (
    DEFAULT_ALIAS,
    MODEL_PRICING,
    Tier,
    normalize_model_alias,
    tier_by_name,
    tokens_to_credits,
)


@pytest.mark.unit
class TestNormalizeModelAlias:
    def test_haiku_variants(self):
        assert normalize_model_alias("claude-haiku-4-5") == ("haiku", False)
        assert normalize_model_alias("haiku") == ("haiku", False)
        assert normalize_model_alias("Claude-Haiku-3-5") == ("haiku", False)

    def test_sonnet_variants(self):
        assert normalize_model_alias("claude-sonnet-4-5") == ("sonnet", False)
        assert normalize_model_alias("sonnet") == ("sonnet", False)

    def test_opus_variants(self):
        assert normalize_model_alias("claude-opus-4-7") == ("opus", False)
        assert normalize_model_alias("claude-opus-4-7[1m]") == ("opus", True)

    def test_long_context_flag(self):
        _, long_ctx = normalize_model_alias("claude-sonnet-4-5[1m]")
        assert long_ctx is True

    def test_empty_falls_back_to_default(self):
        assert normalize_model_alias("") == (DEFAULT_ALIAS, False)
        assert normalize_model_alias(None) == (DEFAULT_ALIAS, False)  # type: ignore[arg-type]

    def test_unknown_falls_back(self):
        alias, _ = normalize_model_alias("gpt-5-turbo")
        assert alias == DEFAULT_ALIAS


@pytest.mark.unit
class TestTokensToCredits:
    def test_zero_tokens_returns_zero(self):
        assert tokens_to_credits("haiku", 0, 0) == 0

    def test_haiku_basic(self):
        # haiku 4.5: 1.00/5.00, 1200 in + 380 out
        # cost = 1200/1M * 1.00 + 380/1M * 5.00 = 0.0012 + 0.0019 = 0.0031
        # credits = ceil(0.0031 / 0.003 * 2.5) = ceil(2.583) = 3
        credits = tokens_to_credits("claude-haiku-4-5", 1200, 380)
        assert credits == 3

    def test_sonnet_basic(self):
        # sonnet 4.x: 3.00/15.00, 1000 in + 500 out
        # cost = 0.003 + 0.0075 = 0.0105
        # credits = ceil(0.0105 / 0.003 * 2.5) = ceil(8.75) = 9
        credits = tokens_to_credits("claude-sonnet-4-5", 1000, 500)
        assert credits == 9

    def test_opus_basic(self):
        # opus 4.5+: 5.00/25.00, 1000 in + 500 out (4.1 미만 모델은 별도 단가지만 OpenDesk 미노출)
        # cost = 0.005 + 0.0125 = 0.0175
        # credits = ceil(0.0175 / 0.003 * 2.5) = ceil(14.583) = 15
        credits = tokens_to_credits("claude-opus-4-7", 1000, 500)
        assert credits == 15

    def test_long_context_no_surcharge_by_default(self):
        # 2026-05-18: Anthropic 가 Claude 4.5+ 부터 1M context 를 standard pricing 에 포함.
        # LONG_CONTEXT_SURCHARGE 기본값을 1.0 으로 갱신 → 동일 비용.
        normal = tokens_to_credits("claude-sonnet-4-5", 100_000, 0)
        long = tokens_to_credits("claude-sonnet-4-5[1m]", 100_000, 0)
        assert long == normal

    def test_negative_raises(self):
        with pytest.raises(ValueError):
            tokens_to_credits("haiku", -1, 0)
        with pytest.raises(ValueError):
            tokens_to_credits("haiku", 0, -1)

    def test_unknown_model_uses_default(self):
        # 모르는 모델은 normalize 에서 DEFAULT_ALIAS 로 폴백 → 가격 계산 성공
        credits = tokens_to_credits("totally-fake-model", 1000, 500)
        assert credits > 0


@pytest.mark.unit
class TestTierLookup:
    def test_small_medium_large(self):
        assert tier_by_name("small").estimated_credits == 30
        assert tier_by_name("medium").estimated_credits == 100
        assert tier_by_name("large").estimated_credits == 200

    def test_unknown_raises(self):
        with pytest.raises(KeyError):
            tier_by_name("xlarge")
