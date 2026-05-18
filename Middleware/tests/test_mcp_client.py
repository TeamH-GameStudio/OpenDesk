"""McpServerHub 단위 테스트.

mcp 패키지가 설치돼 있지 않은 환경에서도 동작해야 하는 가드를 검증.
실제 MCP 서버 통신은 e2e 단계에서 검증.
"""

from __future__ import annotations

import importlib
import sys

import pytest

from mcp_client import McpServerHub


@pytest.mark.unit
def test_hub_initial_state_is_empty():
    hub = McpServerHub()
    assert hub._sessions == {}
    assert hub._tools == []


@pytest.mark.unit
@pytest.mark.asyncio
async def test_set_servers_with_empty_list_is_noop():
    hub = McpServerHub()
    await hub.set_servers([])
    await hub.shutdown()


@pytest.mark.unit
@pytest.mark.asyncio
async def test_set_servers_raises_when_mcp_missing(monkeypatch):
    """mcp 패키지가 없으면 친절한 RuntimeError 가 떠야 한다."""
    # 'mcp' 패키지 import 를 강제로 실패시킴
    for mod in list(sys.modules):
        if mod == "mcp" or mod.startswith("mcp."):
            sys.modules.pop(mod, None)
    real_import = importlib.import_module

    def fake_import(name, *args, **kwargs):
        if name == "mcp" or name.startswith("mcp."):
            raise ImportError(f"forced fail: {name}")
        return real_import(name, *args, **kwargs)

    monkeypatch.setattr(importlib, "import_module", fake_import)

    hub = McpServerHub()
    # set_servers 는 빈 리스트면 import 하지 않으므로, 1개 이상 서버 spec 을 줘야 한다.
    server_specs = [{"name": "x", "command": "echo", "args": [], "env": []}]

    # mcp 가 없으면 set_servers 가 RuntimeError 를 raise 해야 한다.
    # (지연 import 가 ImportError 를 잡아 RuntimeError 로 변환)
    with pytest.raises(RuntimeError):
        await hub.set_servers(server_specs)
