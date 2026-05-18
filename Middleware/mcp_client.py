"""
McpServerHub — provider 무관 공통 MCP 클라이언트.

공식 `mcp` 패키지로 stdio MCP 서버를 스폰하고, 통합 tool 카탈로그를 노출하고,
tool 호출을 적절한 서버로 라우팅한다.

사용 흐름:
    hub = McpServerHub()
    await hub.set_servers(payload)        # McpConfigPayload['servers'] 리스트
    tools = await hub.list_tools()        # [{name, description, input_schema, server}]
    result = await hub.call_tool(name, args)
    await hub.shutdown()

provider(anthropic_api 등) 는 hub.list_tools() 로 받은 도구를 모델에 전달하고,
응답에서 tool_use 가 오면 hub.call_tool 로 실행한 결과를 다시 모델에 보낸다.
"""

from __future__ import annotations

import logging
from contextlib import AsyncExitStack
from dataclasses import dataclass
from typing import Any, Optional

logger = logging.getLogger("mcp_client")


@dataclass(frozen=True)
class ToolInfo:
    name: str             # 모델에 노출되는 도구 이름 (server name 으로 namespacing)
    description: str
    input_schema: dict[str, Any]
    server_name: str
    raw_name: str         # MCP 서버 내부 이름


class McpServerHub:
    """여러 stdio MCP 서버를 묶어 통합 tool 인터페이스를 제공."""

    # tool name namespace 구분자
    NAME_SEP = "__"

    def __init__(self) -> None:
        self._stack: Optional[AsyncExitStack] = None
        self._sessions: dict[str, Any] = {}  # server_name -> ClientSession
        self._tools: list[ToolInfo] = []
        self._tool_index: dict[str, ToolInfo] = {}  # exposed_name -> ToolInfo

    async def set_servers(self, server_specs: list[dict[str, Any]]) -> None:
        """기존 서버 모두 닫고 새 server_specs 로 재구성."""
        await self.shutdown()
        if not server_specs:
            return

        # 지연 import — mcp 패키지가 없는 환경에서도 import 자체는 가능하도록.
        try:
            from mcp import ClientSession, StdioServerParameters
            from mcp.client.stdio import stdio_client
        except ImportError as e:
            raise RuntimeError(
                "mcp 패키지가 필요합니다. `pip install mcp` 또는 requirements.txt 설치 후 재시도하세요."
            ) from e

        self._stack = AsyncExitStack()
        await self._stack.__aenter__()

        for spec in server_specs:
            if not isinstance(spec, dict):
                continue
            name = spec.get("name")
            command = spec.get("command")
            if not name or not command:
                logger.warning("MCP server spec 누락 — name=%r command=%r", name, command)
                continue
            args = list(spec.get("args") or [])
            env_pairs = spec.get("env") or []
            env: dict[str, str] = {}
            for pair in env_pairs:
                key = pair.get("key") if isinstance(pair, dict) else None
                value = pair.get("value") if isinstance(pair, dict) else None
                if key:
                    env[key] = value or ""

            params = StdioServerParameters(command=command, args=args, env=env or None)
            try:
                streams = await self._stack.enter_async_context(stdio_client(params))
                session = await self._stack.enter_async_context(ClientSession(*streams))
                await session.initialize()
                self._sessions[name] = session
                logger.info("MCP server connected: %s", name)
            except Exception as e:  # noqa: BLE001
                logger.error("MCP server %s 시작 실패: %s", name, e)

        await self._reload_tools()

    async def _reload_tools(self) -> None:
        self._tools.clear()
        self._tool_index.clear()
        for server_name, session in self._sessions.items():
            try:
                listing = await session.list_tools()
            except Exception as e:  # noqa: BLE001
                logger.error("list_tools 실패 (%s): %s", server_name, e)
                continue

            for tool in getattr(listing, "tools", []):
                raw_name = getattr(tool, "name", "")
                if not raw_name:
                    continue
                exposed = f"{server_name}{self.NAME_SEP}{raw_name}"
                info = ToolInfo(
                    name=exposed,
                    description=getattr(tool, "description", "") or "",
                    input_schema=getattr(tool, "inputSchema", None) or {},
                    server_name=server_name,
                    raw_name=raw_name,
                )
                self._tools.append(info)
                self._tool_index[exposed] = info

    async def list_tools(self) -> list[ToolInfo]:
        return list(self._tools)

    async def call_tool(self, exposed_name: str, arguments: dict[str, Any]) -> str:
        """도구 호출 결과를 평면 텍스트로 반환. 호출 실패 시 에러 문자열."""
        info = self._tool_index.get(exposed_name)
        if info is None:
            return f"[mcp] unknown tool: {exposed_name}"
        session = self._sessions.get(info.server_name)
        if session is None:
            return f"[mcp] no session for server: {info.server_name}"

        try:
            result = await session.call_tool(info.raw_name, arguments or {})
        except Exception as e:  # noqa: BLE001
            logger.error("MCP call_tool 실패 (%s): %s", exposed_name, e)
            return f"[mcp] tool error: {e}"

        # 결과 텍스트 추출 (content 블록 또는 fallback)
        parts: list[str] = []
        for block in getattr(result, "content", []) or []:
            text = getattr(block, "text", None)
            if text:
                parts.append(text)
        if parts:
            return "\n".join(parts)
        return str(result)

    async def shutdown(self) -> None:
        if self._stack is not None:
            try:
                await self._stack.aclose()
            finally:
                self._stack = None
        self._sessions.clear()
        self._tools.clear()
        self._tool_index.clear()
