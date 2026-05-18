"""anthropic_cli._extract_cli_usage 회귀 테스트."""

from __future__ import annotations

import pytest

from providers.anthropic_cli import _extract_cli_usage


@pytest.mark.unit
def test_no_usage_block_marks_unavailable():
    snap = _extract_cli_usage({"result": "ok", "total_cost_usd": 0.1})
    assert snap.available is False
    assert snap.input_tokens == 0


@pytest.mark.unit
def test_usage_with_input_output_only():
    """CLI 가 cache 필드는 안 주고 input/output 만 주는 일반 케이스."""
    snap = _extract_cli_usage({
        "result": "ok",
        "usage": {"input_tokens": 80, "output_tokens": 20},
    })
    assert snap.available is True
    assert snap.input_tokens == 80
    assert snap.output_tokens == 20
    assert snap.cache_creation_input_tokens == 0
    assert snap.cache_read_input_tokens == 0


@pytest.mark.unit
def test_usage_with_cache_fields():
    """CLI 가 cache 필드까지 제공하는 케이스."""
    snap = _extract_cli_usage({
        "usage": {
            "input_tokens": 50,
            "output_tokens": 10,
            "cache_creation_input_tokens": 200,
            "cache_read_input_tokens": 100,
        },
    })
    assert snap.cache_creation_input_tokens == 200
    assert snap.cache_read_input_tokens == 100


@pytest.mark.unit
def test_usage_block_non_dict():
    """usage 가 dict 가 아닌 비정상 케이스."""
    snap = _extract_cli_usage({"usage": "garbage"})
    assert snap.available is False
