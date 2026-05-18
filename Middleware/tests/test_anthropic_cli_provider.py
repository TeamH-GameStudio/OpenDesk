"""AnthropicCliProvider 단위 테스트 — CLI 호출 없이 페이로드 변환만 검증."""

from __future__ import annotations

import pytest

from providers.anthropic_cli import build_cli_mcp_config


@pytest.mark.unit
def test_build_cli_mcp_config_returns_none_for_empty_payload():
    assert build_cli_mcp_config(None) is None
    assert build_cli_mcp_config({}) is None
    assert build_cli_mcp_config({"servers": []}) is None


@pytest.mark.unit
def test_build_cli_mcp_config_converts_single_server():
    payload = {
        "agentId": "a1",
        "servers": [
            {
                "name": "notion",
                "transport": "stdio",
                "command": "npx",
                "args": ["-y", "@notion/mcp-server"],
                "env": [{"key": "NOTION_API_KEY", "value": "secret-xyz"}],
            }
        ],
    }
    result = build_cli_mcp_config(payload)
    assert result == {
        "mcpServers": {
            "notion": {
                "command": "npx",
                "args": ["-y", "@notion/mcp-server"],
                "env": {"NOTION_API_KEY": "secret-xyz"},
            }
        }
    }


@pytest.mark.unit
def test_build_cli_mcp_config_skips_invalid_servers():
    payload = {
        "servers": [
            {"name": "ok", "command": "x"},
            {"name": "", "command": "y"},      # 이름 누락 → 스킵
            {"name": "no-cmd"},                 # command 누락 → 스킵
        ]
    }
    result = build_cli_mcp_config(payload)
    assert result is not None
    assert set(result["mcpServers"].keys()) == {"ok"}


@pytest.mark.unit
def test_build_cli_mcp_config_omits_env_when_empty():
    payload = {"servers": [{"name": "x", "command": "c", "args": [], "env": []}]}
    result = build_cli_mcp_config(payload)
    assert "env" not in result["mcpServers"]["x"]


@pytest.mark.unit
def test_build_cli_mcp_config_handles_multiple_servers():
    payload = {
        "servers": [
            {"name": "notion", "command": "n"},
            {"name": "github", "command": "g"},
        ]
    }
    result = build_cli_mcp_config(payload)
    assert set(result["mcpServers"].keys()) == {"notion", "github"}
