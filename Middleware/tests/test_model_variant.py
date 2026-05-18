"""Unity 측에서 보내는 model 식별자의 [variant] 접미사 분해 — 1M context 등."""

from __future__ import annotations

import pytest

from providers.anthropic_api import _resolve_model_variant


@pytest.mark.unit
def test_strips_1m_suffix_and_returns_context_beta():
    model, betas = _resolve_model_variant("claude-opus-4-7[1m]")
    assert model == "claude-opus-4-7"
    assert betas == ["context-1m-2025-08-07"]


@pytest.mark.unit
def test_plain_model_id_passthrough():
    model, betas = _resolve_model_variant("claude-sonnet-4-6")
    assert model == "claude-sonnet-4-6"
    assert betas == []


@pytest.mark.unit
def test_empty_input_returns_empty():
    model, betas = _resolve_model_variant("")
    assert model == ""
    assert betas == []


@pytest.mark.unit
def test_unknown_variant_strips_to_be_safe():
    # 미지의 [variant] 라도 API 가 모를 게 분명하니 strip 한 채로 통과시킨다 — 404 회피.
    model, betas = _resolve_model_variant("claude-opus-4-7[xyz]")
    assert model == "claude-opus-4-7"
    assert betas == []


@pytest.mark.unit
def test_whitespace_is_trimmed():
    model, betas = _resolve_model_variant("  claude-opus-4-7[1m]  ")
    assert model == "claude-opus-4-7"
    assert betas == ["context-1m-2025-08-07"]
