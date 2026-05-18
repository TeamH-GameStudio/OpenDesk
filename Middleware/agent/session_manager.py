"""
에이전트 세션 매니저 — Unity 메시지 라우팅 + 에이전트 생명주기 관리.

Unity -> 미들웨어 메시지를 받아 해당 AgentRunner 또는 인터랙티브 도구로 전달.
AgentRunner의 이벤트를 WebSocket broadcast로 Unity에 중계.
"""

import asyncio
import logging
import os
from typing import Dict

from .runner import AgentRunner
from .session_store import SessionStore
from .task_queue import TaskQueueManager
from .cron_manager import CronManager, CronJob
from tools.registry import ToolRegistry
from tools.file_read import FileReadTool
from tools.file_write import FileWriteTool
from tools.web_search import WebSearchTool
from tools.web_fetch import WebFetchTool
from tools.list_files import ListFilesTool
from tools.bash import BashTool
from tools.edit_file import EditFileTool
from tools.ask_user import AskUserTool, AskUserPort
from tools.route_capability import RouteCapabilityTool
from tools.spawn_agent import SpawnAgentTool
from route_capability_infra import PluginRegistry, default_preference_store
from tools.task_create import TaskCreateTool
from tools.task_get import TaskGetTool
from tools.task_list import TaskListTool
from tools.task_update import TaskUpdateTool
from tools.task_stop import TaskStopTool
from tools.task_output import TaskOutputTool
from tools.cron_create import CronCreateTool
from tools.cron_list import CronListTool
from tools.cron_delete import CronDeleteTool
from websocket.server import AgentWebSocketServer
from formatter import markdown_to_tmp, extract_action
from agent.memory import MemoryStore, MemoryWriteTool, MemoryReadTool

logger = logging.getLogger("session_manager")


def _shared_command_validator(command: str):
    """BashTool 의 allowlist 검사 로직을 task/cron 에도 재사용."""
    # workspace 는 검사 로직에 영향 없음 — dummy 사용.
    validator = BashTool("/tmp")
    return validator._validate_command(command)


class _AskUserBroadcaster:
    """AskUserPort 구현 — future 등록 + Unity broadcast."""

    def __init__(self, manager: "AgentSessionManager"):
        self._mgr = manager

    async def ask(self, agent_id: str, tool_use_id: str, payload: dict) -> dict:
        loop = asyncio.get_running_loop()
        fut: asyncio.Future = loop.create_future()
        self._mgr._pending_user_asks[tool_use_id] = fut
        try:
            # payload_kind / capability / compatible_plugins 는 route_capability 확장 필드.
            await self._mgr._ws.broadcast({
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
            self._mgr._pending_user_asks.pop(tool_use_id, None)


class AgentSessionManager:
    def __init__(self, ws_server: AgentWebSocketServer, api_key: str):
        self._ws = ws_server
        self._api_key = api_key
        self._agents: Dict[str, AgentRunner] = {}
        self._session_store = SessionStore()
        self._memory_store = MemoryStore()
        self._pending_user_asks: Dict[str, asyncio.Future] = {}
        # route_capability — 에이전트별 플러그인 레지스트리 + 프로세스 공통 선호 저장소.
        self._plugin_registries: Dict[str, PluginRegistry] = {}
        self._route_preferences = default_preference_store()

        # 백그라운드 작업 / 예약 — 매니저 공유 (에이전트 간 가시성은 agent_id 필터링으로 제한)
        self._task_queue = TaskQueueManager(
            base_dir="~/.opendesk/tasks",
            command_validator=_shared_command_validator,
            on_event=self._on_agent_event,
        )
        self._cron_manager = CronManager(
            config_path="~/.opendesk/cron.json",
            runner=self._run_cron_job,
            on_event=self._on_agent_event,
        )
        self._cron_manager.load()

        self._ask_port = _AskUserBroadcaster(self)

    # ── 라이프사이클 ────────────────────────────────────

    def start(self) -> None:
        """미들웨어 부트 후 호출 (main.py)."""
        try:
            self._cron_manager.start()
        except RuntimeError as e:
            logger.warning(f"Cron scheduler disabled: {e}")

    async def shutdown(self) -> None:
        await self._task_queue.shutdown()
        self._cron_manager.shutdown()

    # ── 에이전트 ─────────────────────────────────────────

    def create_agent(self, agent_id: str, config: dict):
        workspace = os.path.expanduser(
            config.get("workspace", f"~/opendesk/{agent_id}")
        )
        os.makedirs(workspace, exist_ok=True)

        registry = ToolRegistry()
        tool_map = {
            "read_file": FileReadTool(workspace),
            "write_file": FileWriteTool(workspace),
            "list_files": ListFilesTool(workspace),
            "bash": BashTool(workspace),
            "web_search": WebSearchTool(
                api_key=os.environ.get("BRAVE_API_KEY", "")
            ),
            "web_fetch": WebFetchTool(),
            "save_memory": MemoryWriteTool(self._memory_store, agent_id),
            "recall_memory": MemoryReadTool(self._memory_store, agent_id),

            # 신규 도구
            "edit_file": EditFileTool(workspace),
            "ask_user": AskUserTool(self._ask_port, agent_id),
            "spawn_agent": SpawnAgentTool(
                api_key=self._api_key,
                parent_agent_id=agent_id,
                model=config.get("model", "claude-sonnet-4-6"),
                on_event=self._on_agent_event,
            ),
            "task_create": TaskCreateTool(self._task_queue, agent_id, workspace),
            "task_get": TaskGetTool(self._task_queue),
            "task_list": TaskListTool(self._task_queue, agent_id),
            "task_update": TaskUpdateTool(self._task_queue),
            "task_stop": TaskStopTool(self._task_queue),
            "task_output": TaskOutputTool(self._task_queue),
            "cron_create": CronCreateTool(
                self._cron_manager, agent_id,
                command_validator=_shared_command_validator,
            ),
            "cron_list": CronListTool(self._cron_manager),
            "cron_delete": CronDeleteTool(self._cron_manager),

            # 도구 라우팅 — 스킬이 capability 만 부르면 호환 플러그인을 찾아 반환.
            # 에이전트별 PluginRegistry 가 없으면 빈 레지스트리로 생성 (Unity 가 추후 채운다).
            "route_capability": RouteCapabilityTool(
                registry=self._plugin_registries.setdefault(agent_id, PluginRegistry()),
                preferences=self._route_preferences,
                ask_port=self._ask_port,
                agent_id=agent_id,
            ),
        }
        for t in config.get("tools", []):
            if t in tool_map:
                registry.register(tool_map[t])
        # 메모리 도구는 모든 에이전트에 자동 등록
        registry.register(tool_map["save_memory"])
        registry.register(tool_map["recall_memory"])

        runner = AgentRunner(
            agent_id=agent_id,
            role=config["role"],
            system_prompt=config["system_prompt"],
            tool_registry=registry,
            session_store=self._session_store,
            model=config.get("model", "claude-sonnet-4-6"),
            thinking_budget=config.get("thinking_budget", 4000),
            api_key=self._api_key,
            on_event=self._on_agent_event,
            memory_store=self._memory_store,
        )
        self._agents[agent_id] = runner
        logger.info(f"Agent created: {agent_id} ({config['role']})")

    async def _on_agent_event(self, event: dict):
        """AgentRunner / TaskQueue / CronManager / SpawnAgent 이벤트 중계."""
        if event.get("type") == "agent_message" and event.get("message"):
            raw_text = event["message"]
            clean_text, action = extract_action(raw_text)
            event["message"] = markdown_to_tmp(clean_text)

            await self._ws.broadcast(event)

            if action:
                await self._ws.broadcast({
                    "type": "agent_action",
                    "agent_id": event.get("agent_id", ""),
                    "action": action,
                    "timestamp": event.get("timestamp", 0),
                })
            return

        await self._ws.broadcast(event)

    async def _run_cron_job(self, job: CronJob) -> None:
        """Cron 발화 → task_queue 에 위임해서 stdout/상태 일관 처리."""
        self._task_queue.create(
            agent_id=job.agent_id,
            command=job.command,
            description=f"[cron:{job.name}]",
            workspace=os.path.expanduser(f"~/opendesk/{job.agent_id}"),
        )

    async def on_client_disconnect(self) -> None:
        """모든 클라이언트가 떨어지면 pending ask 들을 cancel 해서 future leak 방지."""
        if not self._pending_user_asks:
            return
        for tool_use_id, fut in list(self._pending_user_asks.items()):
            if not fut.done():
                fut.set_result({"response": "", "selected": [], "cancelled": True})

    async def handle_unity_message(self, data: dict):
        """Unity에서 수신한 메시지를 타입별로 분기 처리"""
        msg_type = data.get("type")
        agent_id = data.get("agent_id", "")

        if msg_type == "chat_message":
            await self._handle_chat(agent_id, data.get("message", ""))
        elif msg_type == "chat_clear":
            await self._handle_new_session(agent_id)
        elif msg_type == "session_list":
            await self._handle_session_list(agent_id)
        elif msg_type == "session_switch":
            await self._handle_session_switch(
                agent_id, data.get("session_id", "")
            )
        elif msg_type == "session_new":
            await self._handle_new_session(agent_id)
        elif msg_type == "session_delete":
            await self._handle_session_delete(
                agent_id, data.get("session_id", "")
            )
        elif msg_type == "status_request":
            await self._send_all_status()
        elif msg_type == "tool_user_response":
            await self._handle_tool_user_response(data)
        elif msg_type == "task_control":
            await self._handle_task_control(data)
        elif msg_type == "set_plugin_registry":
            count = self.set_plugin_registry(agent_id, data.get("payload") or [])
            await self._ws.broadcast({
                "type": "plugin_registry_updated",
                "agent_id": agent_id,
                "count": count,
            })
        else:
            logger.warning(f"Unknown message type: {msg_type}")

    # ── 인터랙티브 도구 핸들러 ──

    async def _handle_tool_user_response(self, data: dict):
        tool_use_id = data.get("tool_use_id", "")
        fut = self._pending_user_asks.get(tool_use_id)
        if not fut or fut.done():
            logger.warning(f"tool_user_response for unknown/done id: {tool_use_id}")
            return
        fut.set_result({
            "response": data.get("response", ""),
            "selected": data.get("selected", []),
            "remember": bool(data.get("remember", False)),
        })

    def set_plugin_registry(self, agent_id: str, payload: list) -> int:
        """Unity 가 set_plugin_registry op 로 push 한 플러그인 목록 캐시.

        route_capability 가 이 레지스트리에서 capability 호환 플러그인을 찾는다.
        """
        if not agent_id:
            return 0
        registry = self._plugin_registries.get(agent_id)
        if registry is None:
            registry = PluginRegistry()
            self._plugin_registries[agent_id] = registry
        if payload is None:
            registry.clear()
            return 0
        return registry.replace(payload or [])

    async def _handle_task_control(self, data: dict):
        action = data.get("action", "")
        task_id = data.get("task_id", "")
        if action == "stop":
            self._task_queue.stop(task_id)
        elif action == "update":
            patch = data.get("patch") or {}
            self._task_queue.update(task_id, **patch)
        else:
            logger.warning(f"Unknown task_control action: {action}")

    # ── 채팅 핸들러 ──

    async def _handle_chat(self, agent_id: str, message: str):
        runner = self._agents.get(agent_id)
        if not runner:
            await self._ws.broadcast(
                {
                    "type": "agent_message",
                    "agent_id": agent_id,
                    "message": f"'{agent_id}' 에이전트를 찾을 수 없습니다.",
                }
            )
            return
        asyncio.create_task(runner.send_message(message))

    async def _handle_session_list(self, agent_id: str):
        runner = self._agents.get(agent_id)
        if not runner:
            return
        await self._ws.broadcast(
            {
                "type": "session_list_response",
                "agent_id": agent_id,
                "current_session_id": runner.current_session_id,
                "sessions": runner.list_sessions(),
            }
        )

    async def _handle_session_switch(self, agent_id: str, session_id: str):
        runner = self._agents.get(agent_id)
        if not runner:
            return

        if runner.switch_session(session_id):
            chat_history = _extract_chat_history(runner.messages)
            await self._ws.broadcast(
                {
                    "type": "session_switched",
                    "agent_id": agent_id,
                    "session_id": session_id,
                    "chat_history": chat_history,
                }
            )
            await self._ws.broadcast(
                {
                    "type": "agent_state",
                    "agent_id": agent_id,
                    "role": runner.role,
                    "state": "idle",
                    "session_id": session_id,
                }
            )

    async def _handle_new_session(self, agent_id: str):
        runner = self._agents.get(agent_id)
        if not runner:
            return
        meta = runner.new_session()
        await self._ws.broadcast(
            {
                "type": "session_switched",
                "agent_id": agent_id,
                "session_id": meta["session_id"],
                "chat_history": [],
            }
        )
        await self._ws.broadcast(
            {
                "type": "agent_state",
                "agent_id": agent_id,
                "role": runner.role,
                "state": "idle",
            }
        )
        await self._ws.broadcast(
            {
                "type": "agent_message",
                "agent_id": agent_id,
                "role": runner.role,
                "message": "새 대화를 시작합니다. 무엇을 도와드릴까요?",
            }
        )

    async def _handle_session_delete(self, agent_id: str, session_id: str):
        runner = self._agents.get(agent_id)
        if not runner:
            return
        runner.delete_session(session_id)
        await self._handle_session_list(agent_id)

    async def _send_all_status(self):
        for agent_id, runner in self._agents.items():
            await self._ws.broadcast(
                {
                    "type": "agent_state",
                    "agent_id": agent_id,
                    "role": runner.role,
                    "state": "working" if runner._is_busy else "idle",
                    "session_id": runner.current_session_id,
                }
            )
            await self._handle_session_list(agent_id)


def _extract_chat_history(messages: list) -> list:
    """messages 배열에서 Unity 표시용 chat_history 추출"""
    history = []
    for msg in messages:
        role = msg.get("role")
        content = msg.get("content")

        if role == "user" and isinstance(content, str):
            history.append({"role": "user", "text": content})
        elif role == "assistant":
            if isinstance(content, str):
                history.append({"role": "assistant", "text": content})
            elif isinstance(content, list):
                texts = []
                for b in content:
                    if isinstance(b, dict) and b.get("type") == "text":
                        texts.append(b.get("text", ""))
                    elif hasattr(b, "type") and b.type == "text":
                        texts.append(b.text)
                if texts:
                    history.append({"role": "assistant", "text": "\n".join(texts)})
    return history
