"""build_default_pipeline 의 config 파싱 / 미지정 hook 처리 / emitter 추가 테스트."""

from __future__ import annotations

from typing import Any

import pytest

from hooks.builders import build_default_pipeline
from hooks.builtin import (
    CacheStatHook,
    LatencyHook,
    RateLimitHook,
    RetryHook,
    TelemetryEmitterHook,
)


@pytest.mark.unit
def test_default_chain_with_send_fn():
    async def send(_):
        pass

    pipeline = build_default_pipeline({}, send_fn=send)
    names = [h.name for h in pipeline.hooks]
    assert names == ["retry", "rate_limit", "latency", "cache", "telemetry_emitter"]


@pytest.mark.unit
def test_emitter_skipped_when_no_send_fn():
    pipeline = build_default_pipeline({}, send_fn=None)
    names = [h.name for h in pipeline.hooks]
    assert "telemetry_emitter" not in names
    # 나머지는 정상 등록.
    assert "latency" in names
    assert "cache" in names


@pytest.mark.unit
def test_unknown_hook_in_chain_skipped():
    config: dict[str, Any] = {"hooks": {"chain": ["retry", "made_up", "latency"]}}
    pipeline = build_default_pipeline(config, send_fn=None)
    names = [h.name for h in pipeline.hooks]
    assert names == ["retry", "latency"]


@pytest.mark.unit
def test_retry_hook_uses_config_values():
    async def send(_):
        pass

    config: dict[str, Any] = {
        "hooks": {
            "chain": ["retry"],
            "retry": {
                "max_retries": 7,
                "base_seconds": 2.0,
                "max_backoff_seconds": 99.0,
            },
        },
    }
    pipeline = build_default_pipeline(config, send_fn=send)
    retry = [h for h in pipeline.hooks if isinstance(h, RetryHook)][0]
    assert retry._max_retries == 7
    assert retry._base == 2.0
    assert retry._max_backoff == 99.0


@pytest.mark.unit
def test_telemetry_completeness_passes_through():
    async def send(_):
        pass

    pipeline = build_default_pipeline(
        {},
        send_fn=send,
        telemetry_completeness="partial",
    )
    emitter = [h for h in pipeline.hooks if isinstance(h, TelemetryEmitterHook)][0]
    assert emitter._completeness == "partial"


@pytest.mark.unit
def test_emitter_is_last_in_chain():
    """TelemetryEmitter 는 항상 체인의 마지막에 위치해야 한다."""
    async def send(_):
        pass

    pipeline = build_default_pipeline({}, send_fn=send)
    assert pipeline.hooks[-1].name == "telemetry_emitter"
