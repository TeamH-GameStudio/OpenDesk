"""anthropic_api._extract_usage / _add_usage / max_tool_rounds 회귀."""

from __future__ import annotations

from types import SimpleNamespace
from typing import Any

import pytest

from hooks import UsageSnapshot
from providers.anthropic_api import _add_usage, _extract_usage


@pytest.mark.unit
def test_extract_usage_full_fields():
    """SDK 의 모든 필드가 채워진 케이스."""
    final_message = SimpleNamespace(usage=SimpleNamespace(
        input_tokens=100,
        output_tokens=50,
        cache_creation_input_tokens=300,
        cache_read_input_tokens=200,
    ))
    snap = _extract_usage(final_message)
    assert snap.input_tokens == 100
    assert snap.output_tokens == 50
    assert snap.cache_creation_input_tokens == 300
    assert snap.cache_read_input_tokens == 200
    assert snap.available is True


@pytest.mark.unit
def test_extract_usage_no_usage_attribute():
    """final_message 에 usage 가 없는 경우 available=False."""
    final_message = SimpleNamespace()
    snap = _extract_usage(final_message)
    assert snap.available is False


@pytest.mark.unit
def test_extract_usage_partial_fields():
    """cache_* 필드가 누락된 SDK 버전 — 0 폴백 + available=True."""
    final_message = SimpleNamespace(usage=SimpleNamespace(
        input_tokens=50,
        output_tokens=10,
    ))
    snap = _extract_usage(final_message)
    assert snap.input_tokens == 50
    assert snap.output_tokens == 10
    assert snap.cache_creation_input_tokens == 0
    assert snap.cache_read_input_tokens == 0
    assert snap.available is True


@pytest.mark.unit
def test_extract_usage_handles_none_values():
    """SDK 가 명시적 None 으로 보고하는 케이스 — 0 으로 폴백."""
    final_message = SimpleNamespace(usage=SimpleNamespace(
        input_tokens=None,
        output_tokens=20,
        cache_creation_input_tokens=None,
        cache_read_input_tokens=None,
    ))
    snap = _extract_usage(final_message)
    assert snap.input_tokens == 0
    assert snap.output_tokens == 20
    assert snap.cache_creation_input_tokens == 0
    assert snap.cache_read_input_tokens == 0


@pytest.mark.unit
def test_add_usage_sums_fields():
    a = UsageSnapshot(
        input_tokens=10, output_tokens=5,
        cache_creation_input_tokens=100, cache_read_input_tokens=50,
    )
    b = UsageSnapshot(
        input_tokens=20, output_tokens=15,
        cache_creation_input_tokens=200, cache_read_input_tokens=100,
    )
    total = _add_usage(a, b)
    assert total.input_tokens == 30
    assert total.output_tokens == 20
    assert total.cache_creation_input_tokens == 300
    assert total.cache_read_input_tokens == 150


@pytest.mark.unit
def test_add_usage_propagates_unavailable():
    """한쪽이라도 available=False 면 결과도 False (보수적)."""
    a = UsageSnapshot(input_tokens=10, available=True)
    b = UsageSnapshot(input_tokens=20, available=False)
    total = _add_usage(a, b)
    assert total.input_tokens == 30
    assert total.available is False


@pytest.mark.unit
def test_add_usage_identity():
    """0 으로 초기화된 snapshot 과 합산해도 변화 없음."""
    zero = UsageSnapshot()
    real = UsageSnapshot(input_tokens=100, output_tokens=50)
    assert _add_usage(zero, real) == real
    assert _add_usage(real, zero) == real
