"""
에이전트 세션 매니저 — Unity 메시지 라우팅 + 에이전트 생명주기 관리.

Unity -> 미들웨어 7종 메시지를 받아 해당 AgentRunner에 전달.
AgentRunner의 이벤트를 WebSocket broadcast로 Unity에 중계.
"""

import asyncio
import logging
import os
from typing import Dict

from .runner import AgentRunner
from .session_store import SessionStore
from tools.registry import ToolRegistry
from tools.file_read import FileReadTool
from tools.file_write import FileWriteTool
from tools.web_search import WebSearchTool
from tools.web_fetch import WebFetchTool
from tools.list_files import ListFilesTool
from tools.bash import BashTool
from websocket.server import AgentWebSocketServer
from formatter import markdown_to_tmp, extract_action
from agent.memory import MemoryStore, MemoryWriteTool, MemoryReadTool

logger = logging.getLogger("session_manager")


class AgentSessionManager:
    def __init__(self, ws_server: AgentWebSocketServer, api_key: str):
        self._ws = ws_server
        self._api_key = api_key
        self._agents: Dict[str, AgentRunner] = {}
        self._session_store = SessionStore()
        self._memory_store = MemoryStore()

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
        """AgentRunner 이벤트를 WebSocket으로 중계. agent_message에 포매팅 + 액션 파싱."""
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
        else:
            logger.warning(f"Unknown message type: {msg_type}")

    # ── 메시지 핸들러 ──

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
