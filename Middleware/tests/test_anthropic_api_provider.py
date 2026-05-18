"""AnthropicApiProvider 단위 테스트."""

from __future__ import annotations

import pytest

from providers.anthropic_api import AnthropicApiProvider


@pytest.mark.unit
@pytest.mark.asyncio
async def test_check_available_returns_false_without_api_key():
    provider = AnthropicApiProvider(api_key="")
    ok, msg = await provider.check_available()
    assert ok is False
    assert msg == "anthropic_api_key_missing"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_chat_emits_error_when_api_key_missing():
    provider = AnthropicApiProvider(api_key="")
    errors: list[tuple[str, str]] = []

    async def on_delta(_): pass
    async def on_final(_, __): pass
    async def on_error(msg, code): errors.append((msg, code))

    from providers.base import ProviderCallbacks
    callbacks = ProviderCallbacks(on_delta=on_delta, on_final=on_final, on_error=on_error)

    await provider.chat(prompt="hi", callbacks=callbacks)

    assert errors and errors[0][1] == "anthropic_api_key_missing"
