"""
Credit pricing policy — 토큰 사용량을 OpenDesk 크레딧으로 변환.

모델별 단가 테이블과 변환 함수를 제공한다. 라우터/세틀러가 import 해서 사용.

설계 메모:
    - `CREDIT_USD = 0.003` — 1 크레딧 = $0.003 (마진 시뮬레이션 결과)
    - `MARKUP_FACTOR = 2.5` — 운영비/마진 버퍼
    - 실배포 전에 Anthropic 공식 가격 페이지에서 4.x 세대 단가 재확인 필요.
    - 1M context (`[1m]` suffix) 모델은 입력 토큰 단가를 LONG_CONTEXT_SURCHARGE 배 가산.
"""

from __future__ import annotations

import math
import os
from dataclasses import dataclass
from typing import Final


# ── 환경변수로 오버라이드 가능한 상수 ─────────────────────────

CREDIT_USD: Final[float] = float(os.environ.get("OPENDESK_CREDIT_USD", "0.003"))
MARKUP_FACTOR: Final[float] = float(os.environ.get("OPENDESK_MARKUP_FACTOR", "2.5"))
LONG_CONTEXT_SURCHARGE: Final[float] = float(
    os.environ.get("OPENDESK_LONG_CTX_SURCHARGE", "2.0")
)


# ── 모델 가격표 (USD per million tokens) ──────────────────────
#
# 키는 normalize_model_alias 가 반환하는 표준 alias.
# 새 모델 추가 시 alias 매퍼와 이 테이블 둘 다 갱신.

@dataclass(frozen=True)
class ModelPrice:
    input_per_mtok_usd: float
    output_per_mtok_usd: float


MODEL_PRICING: Final[dict[str, ModelPrice]] = {
    "haiku": ModelPrice(input_per_mtok_usd=0.80, output_per_mtok_usd=4.00),
    "sonnet": ModelPrice(input_per_mtok_usd=3.00, output_per_mtok_usd=15.00),
    "opus": ModelPrice(input_per_mtok_usd=15.00, output_per_mtok_usd=75.00),
}

DEFAULT_ALIAS: Final[str] = "sonnet"


# ── 모델 ID → alias 매핑 ─────────────────────────────────────
#
# Anthropic 모델 ID (`claude-opus-4-7`, `claude-sonnet-4-5`, …) 와 OpenDesk
# 내부 alias 사이 변환. `[1m]` suffix 는 long-context 플래그로 분리.

def normalize_model_alias(model_id: str) -> tuple[str, bool]:
    """모델 ID 를 (alias, is_long_context) 로 정규화.

    예:
        "claude-opus-4-7[1m]" → ("opus", True)
        "claude-sonnet-4-6"   → ("sonnet", False)
        "haiku"               → ("haiku", False)
        "" / None             → (DEFAULT_ALIAS, False)
    """
    if not model_id:
        return DEFAULT_ALIAS, False

    raw = model_id.strip().lower()
    is_long_context = "[1m]" in raw
    if is_long_context:
        raw = raw.replace("[1m]", "")

    if "haiku" in raw:
        return "haiku", is_long_context
    if "opus" in raw:
        return "opus", is_long_context
    if "sonnet" in raw:
        return "sonnet", is_long_context
    return DEFAULT_ALIAS, is_long_context


# ── 변환 함수 ────────────────────────────────────────────────

def tokens_to_credits(
    model_id: str,
    input_tokens: int,
    output_tokens: int,
) -> int:
    """토큰 사용량을 크레딧으로 변환 (ceil).

    수식: ceil((cost_usd / CREDIT_USD) * MARKUP_FACTOR)
    long-context 모델은 입력가에 LONG_CONTEXT_SURCHARGE 배 적용.
    """
    if input_tokens < 0 or output_tokens < 0:
        raise ValueError(
            f"token counts must be non-negative: input={input_tokens}, output={output_tokens}"
        )

    alias, is_long_context = normalize_model_alias(model_id)
    price = MODEL_PRICING.get(alias)
    if price is None:
        raise KeyError(f"no pricing for model alias '{alias}' (from '{model_id}')")

    input_price = price.input_per_mtok_usd
    if is_long_context:
        input_price *= LONG_CONTEXT_SURCHARGE

    cost_usd = (
        (input_tokens / 1_000_000.0) * input_price
        + (output_tokens / 1_000_000.0) * price.output_per_mtok_usd
    )

    if cost_usd <= 0:
        return 0
    return math.ceil(cost_usd / CREDIT_USD * MARKUP_FACTOR)


# ── 라우터 tier 정의 ─────────────────────────────────────────
#
# 라우터가 결정하는 hold 크레딧. 실 소진은 settle 단계에서 후정산.

@dataclass(frozen=True)
class Tier:
    name: str
    estimated_credits: int


TIER_SMALL: Final[Tier] = Tier(name="small", estimated_credits=30)
TIER_MEDIUM: Final[Tier] = Tier(name="medium", estimated_credits=100)
TIER_LARGE: Final[Tier] = Tier(name="large", estimated_credits=200)

TIERS_BY_NAME: Final[dict[str, Tier]] = {
    t.name: t for t in (TIER_SMALL, TIER_MEDIUM, TIER_LARGE)
}


def tier_by_name(name: str) -> Tier:
    if name not in TIERS_BY_NAME:
        raise KeyError(f"unknown tier '{name}'. available: {list(TIERS_BY_NAME.keys())}")
    return TIERS_BY_NAME[name]
