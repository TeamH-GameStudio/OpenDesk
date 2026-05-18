"""
server.py 가 ChatSession 별로 in-process 도구를 구성할 때 사용하는 헬퍼.

미들웨어 단일 세션(server.py) 경로에 도구 11종(edit_file / ask_user / spawn_agent /
task_* / cron_*) 을 등록한다. ask_user 의 blocking 라운드트립은 server.py 가
broadcast 함수와 pending future 맵을 주입해 처리한다.

main.py(멀티 에이전트) 경로의 session_manager.py 와는 별개 흐름이지만, 같은
BaseTool 인스턴스를 공유하지 않고 동일 클래스를 재인스턴스화한다 (세션 간 상태 격리).
"""

from __future__ import annotations

import asyncio
import logging
import os
from typing import Awaitable, Callable, Optional

from agent.cron_manager import CronJob, CronManager
from agent.memory import MemoryReadTool, MemoryStore, MemoryWriteTool
from agent.task_queue import TaskQueueManager
from agent.tool_journal import ToolJournal
from route_capability_infra import (
    PluginRegistryPort,
    RoutePreferenceStorePort,
)
from tools import (
    AskUserPort,
    AskUserTool,
    CronCreateTool,
    CronDeleteTool,
    CronListTool,
    EditFileTool,
    FileReadTool,
    FileWriteTool,
    ListFilesTool,
    ReadToolHistoryTool,
    RouteCapabilityTool,
    SpawnAgentTool,
    TaskCreateTool,
    TaskGetTool,
    TaskListTool,
    TaskOutputTool,
    TaskStopTool,
    TaskUpdateTool,
    ToolRegistry,
    WebFetchTool,
    WebSearchTool,
)
from tools.bash import BashTool

logger = logging.getLogger("server_tools")


def shared_command_validator(command: str) -> Optional[str]:
    """BashTool 의 allowlist 검사 — task_queue / cron_create 도 공유."""
    return BashTool("/tmp")._validate_command(command)


class WebSocketAskUserPort:
    """AskUserPort 구현 — server.py 의 broadcast 함수와 pending future 맵을 사용."""

    def __init__(
        self,
        pending: dict[str, asyncio.Future],
        send_fn: Callable[[dict], Awaitable[None]],
    ):
        self._pending = pending
        self._send = send_fn

    async def ask(self, agent_id: str, tool_use_id: str, payload: dict) -> dict:
        loop = asyncio.get_running_loop()
        fut: asyncio.Future = loop.create_future()
        self._pending[tool_use_id] = fut
        try:
            # payload_kind / capability / compatible_plugins 는 route_capability 가 넘기는
            # 확장 필드. 없으면 ask_user 의 기본 카드 UI 가 그대로 그려진다.
            await self._send({
                "type": "tool_user_ask",
                "agent_id": agent_id,
                "tool_use_id": tool_use_id,
                "question": payload.get("question", ""),
                "header": payload.get("header", ""),
                "multi_select": bool(payload.get("multi_select", False)),
                "options": payload.get("options", []),
                "payload_kind": payload.get("payload_kind", "ask_user"),
                "capability": payload.get("capability", ""),
                "compatible_plugins": payload.get("compatible_plugins", []),
            })
            return await fut
        finally:
            self._pending.pop(tool_use_id, None)


def build_in_process_registry(
    *,
    agent_id: str,
    workspace: str,
    api_key: str,
    model: str,
    task_queue: TaskQueueManager,
    cron_manager: CronManager,
    memory_store: MemoryStore,
    ask_port: AskUserPort,
    on_event: Callable[[dict], Awaitable[None]],
    tool_journal: Optional[ToolJournal] = None,
    plugin_registry: Optional[PluginRegistryPort] = None,
    route_preferences: Optional[RoutePreferenceStorePort] = None,
) -> ToolRegistry:
    """세션별 in-process 도구 풀 (총 21개).

    카테고리:
      - 파일/셸 (5):  read_file / write_file / edit_file / list_files / bash
      - 웹 (2):       web_search (BRAVE_API_KEY) / web_fetch
      - 메모리 (2):   save_memory / recall_memory
      - 인터랙티브 (1): ask_user
      - 서브에이전트 (1): spawn_agent
      - 백그라운드 작업 (6): task_create / get / list / update / stop / output
      - 예약 작업 (3): cron_create / list / delete
      - 자기 활동 조회 (1): read_tool_history (tool_journal 제공 시)
    """
    registry = ToolRegistry()
    brave_api_key = os.environ.get("BRAVE_API_KEY", "")
    chosen_model = model or "claude-sonnet-4-5"

    tools = [
        # 파일/셸
        FileReadTool(workspace),
        FileWriteTool(workspace),
        EditFileTool(workspace),
        ListFilesTool(workspace),
        BashTool(workspace),
        # 웹
        WebSearchTool(api_key=brave_api_key),
        WebFetchTool(),
        # 메모리 — 에이전트별 격리
        MemoryWriteTool(memory_store, agent_id),
        MemoryReadTool(memory_store, agent_id),
        # 인터랙티브
        AskUserTool(ask_port, agent_id),
        # 서브에이전트
        SpawnAgentTool(
            api_key=api_key,
            parent_agent_id=agent_id,
            model=chosen_model,
            on_event=on_event,
        ),
        # 백그라운드 작업
        TaskCreateTool(task_queue, agent_id, workspace),
        TaskGetTool(task_queue),
        TaskListTool(task_queue, agent_id),
        TaskUpdateTool(task_queue),
        TaskStopTool(task_queue),
        TaskOutputTool(task_queue),
        # 예약 작업
        CronCreateTool(cron_manager, agent_id, command_validator=shared_command_validator),
        CronListTool(cron_manager),
        CronDeleteTool(cron_manager),
    ]
    if tool_journal is not None:
        tools.append(ReadToolHistoryTool(tool_journal))
    if plugin_registry is not None and route_preferences is not None:
        tools.append(
            RouteCapabilityTool(
                registry=plugin_registry,
                preferences=route_preferences,
                ask_port=ask_port,
                agent_id=agent_id,
            )
        )
    for tool in tools:
        registry.register(tool)
    return registry


__all__ = [
    "WebSocketAskUserPort",
    "build_in_process_registry",
    "shared_command_validator",
]
