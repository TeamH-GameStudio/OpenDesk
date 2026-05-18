"""
OpenDesk AI 미들웨어 서버.

Unity ↔ AI provider 사이의 WebSocket 게이트웨이.
- Unity 메시지의 `provider` 필드로 provider 분기 (anthropic_cli / anthropic_api / 향후 openai / gemini ...)
- MCP/Plugin 도구 호출은 provider 가 공통 mcp_client 로 처리 → 동작 일치 보장
- 응답은 delta/final 스트리밍

기존 `claude_bridge.py` 는 providers/anthropic_cli.py 로 흡수되었으며 유지 보수상 그대로 두되 신규 코드는 참조하지 않는다.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import signal
import sys
import time
from pathlib import Path
from typing import Any, Optional

import websockets

# .env 자동 로드 (있으면)
try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

from auth_login import AuthEvent, AuthLoginRunner
from formatter import markdown_to_tmp
from hooks import HookPipeline, RequestCtx
from hooks.builders import build_default_pipeline
from hooks.hooked_provider import HookedProvider
from providers import get_provider, list_providers, ProviderCallbacks
from providers.base import (
    MessageStopEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)
# providers/__init__.py 가 base/registry 만 노출하므로 등록을 위해 모듈을 명시 import.
import providers.anthropic_cli  # noqa: F401  — self-register
import providers.anthropic_api  # noqa: F401  — self-register
import providers.opendesk_routed  # noqa: F401  — self-register (hybrid routing)
from skill_loadout_store import SkillLoadoutStore
from routing_client import get_routing_client
from mock_routing_server import LicenseError

# In-process 도구 풀 — 기본 8개 + 신규 12개 = 총 20개
from agent.cron_manager import CronManager
from agent.memory import MemoryStore
from agent.task_queue import TaskQueueManager
from agent.tool_journal import ToolJournal
from route_capability_infra import default_preference_store
from server_tools import (
    WebSocketAskUserPort,
    build_in_process_registry,
)
from tools.registry import ToolRegistry

# ── 로깅 ────────────────────────────────────────────────────────

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("server")

CONFIG_PATH = Path(__file__).parent / "config.json"

DEFAULT_PROVIDER = "anthropic_api"  # OAuth/API key 양쪽 지원. CLI 는 도구 호출 불가로 deprecated.

# In-process 도구 — 프로세스 1개당 공유 (task_queue / cron_manager / memory_store).
# 세션 진입 시 _ensure_started 호출.
_TASK_QUEUE: Optional[TaskQueueManager] = None
_CRON_MANAGER: Optional[CronManager] = None
_MEMORY_STORE: Optional[MemoryStore] = None


def _get_task_queue() -> TaskQueueManager:
    global _TASK_QUEUE
    if _TASK_QUEUE is None:
        _TASK_QUEUE = TaskQueueManager(base_dir="~/.opendesk/tasks")
    return _TASK_QUEUE


def _get_cron_manager() -> CronManager:
    global _CRON_MANAGER
    if _CRON_MANAGER is None:
        _CRON_MANAGER = CronManager(config_path="~/.opendesk/cron.json")
        _CRON_MANAGER.load()
        try:
            _CRON_MANAGER.start()
        except RuntimeError as e:
            logger.warning("Cron scheduler disabled: %s", e)
    return _CRON_MANAGER


def _get_memory_store() -> MemoryStore:
    global _MEMORY_STORE
    if _MEMORY_STORE is None:
        _MEMORY_STORE = MemoryStore()
    return _MEMORY_STORE


def load_config() -> dict[str, Any]:
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    return {
        "port": 8765,
        "host": "localhost",
        "claude": {
            "systemPrompt": "당신은 친절한 AI 어시스턴트입니다. 한국어로 대답합니다.",
            # max_turns * 2 만큼 history 를 보관 (user/assistant 한 쌍 = 2). 너무 크면
            # 매 요청 input 토큰이 분당 한도(429) 직격이라 12 턴(=24 메시지) 으로 묶음.
            "maxTurns": 12,
            "timeoutSeconds": 120,
            "cliPath": "claude",
        },
        "formatting": {
            "markdownToTmp": True,
            "maxResponseLength": 10000,
        },
    }


def apply_env_from_config(config: dict[str, Any]) -> None:
    """config.json 의 일부 값을 환경변수로 노출. provider 들이 자기 등록 시 사용."""
    claude_cfg = config.get("claude") or {}
    cli_path = claude_cfg.get("cliPath")
    if cli_path:
        os.environ.setdefault("OPENDESK_CLAUDE_CLI_PATH", str(cli_path))
    timeout = claude_cfg.get("timeoutSeconds")
    if timeout:
        os.environ.setdefault("OPENDESK_CLAUDE_TIMEOUT", str(int(timeout)))

    # Claude CLI 격리 — 글로벌 ~/.claude/ 가 아닌 OpenDesk 전용 디렉토리.
    # Unity MiddlewareLauncher 가 이미 CLAUDE_CONFIG_DIR 를 주입했다면 그대로 사용.
    # 미들웨어를 직접 실행한 경우(개발자 수동 기동) OPENDESK_BASE_DIR 로부터 폴백.
    if not os.environ.get("CLAUDE_CONFIG_DIR"):
        base = os.environ.get("OPENDESK_BASE_DIR")
        if not base:
            home = os.path.expanduser("~")
            base = os.path.join(home, ".opendesk")
        isolated = os.path.join(base, "claude-cli")
        try:
            os.makedirs(isolated, exist_ok=True)
        except OSError as e:
            logger.warning("CLAUDE_CONFIG_DIR 생성 실패: %s (%s)", isolated, e)
        os.environ["CLAUDE_CONFIG_DIR"] = isolated
    logger.info("Claude CLI 격리: CLAUDE_CONFIG_DIR=%s", os.environ.get("CLAUDE_CONFIG_DIR"))


# ── 세션 ────────────────────────────────────────────────────────

class ChatSession:
    """클라이언트별 대화 세션 — provider/모델/system prompt/mcp config 보유.

    in-process 도구(edit_file / ask_user / spawn_agent / task_* / cron_*) 는
    `in_process_tools` 에 보관하고 provider.chat 호출 시 함께 전달한다.
    `pending_user_asks` 는 ask_user 의 blocking 라운드트립용 future 맵.
    """

    def __init__(self, config: dict[str, Any]):
        self.config = config
        claude_cfg = config.get("claude", {})
        self.system_prompt: str = claude_cfg.get(
            "systemPrompt", "당신은 친절한 AI 어시스턴트입니다."
        )
        self.max_turns: int = claude_cfg.get("maxTurns", 12)
        self.history: list[dict[str, str]] = []   # [{"role": "user"|"assistant", "content": "..."}]
        self.format_enabled: bool = config.get("formatting", {}).get("markdownToTmp", True)
        self.provider_name: str = DEFAULT_PROVIDER
        self.provider = get_provider(self.provider_name)
        self.mcp_config: Optional[dict[str, Any]] = None
        self.model: str = ""
        self.skill_loadout = SkillLoadoutStore()
        # 라이선스 활성화 후 Unity 가 set_auth 로 JWT 를 보낸다. opendesk_routed
        # provider 가 이 값을 routing_client 에 주입.
        self.license_jwt: str = ""
        self.complexity_hint: str = "auto"
        self.agent_role: str = "default"
        # 도구 호출 활동 로그. provider 가 round 마다 append 하고 AI 가
        # read_tool_history 도구로 조회. history 와는 별도 — 토큰 비용 미발생.
        self.tool_journal = ToolJournal()
        self.auth_runner: Optional[AuthLoginRunner] = None
        # In-process 도구 라운드트립용 상태 — handle_client 에서 등록.
        self.in_process_tools: Optional[ToolRegistry] = None
        self.pending_user_asks: dict[str, asyncio.Future] = {}
        # route_capability 가 조회하는 세션별 플러그인 카탈로그. Unity 가 set_plugin_registry op 로 채운다.
        from route_capability_infra import PluginRegistry  # 순환 import 회피용 지연 로드
        self.plugin_registry: PluginRegistry = PluginRegistry()
        # 진행 중인 chat task 핸들 — disconnect 시 cancel.
        # chat 처리를 background task 로 띄워야 ask_user 같은 blocking 도구 사용 중에도
        # message pump 가 tool_user_response 를 받을 수 있다 (deadlock 방지).
        self.active_chat_task: Optional[asyncio.Task] = None
        # Hook pipeline — handle_client 에서 send_fn (broadcast) 을 주입한 후 build.
        # hooks.enabled=false 또는 미구성 시 None 으로 둬서 raw chat() 폴백.
        self.hook_pipeline: Optional[HookPipeline] = None

    def add_user_message(self, text: str):
        self.history.append({"role": "user", "content": text})
        if len(self.history) > self.max_turns * 2:
            self.history = self.history[-(self.max_turns * 2):]

    def add_assistant_message(self, text: str):
        self.history.append({"role": "assistant", "content": text})

    def mark_user_turn_failed(self, stub: str = "[이전 응답 실패]") -> None:
        # provider 가 final 콜백 없이 종료된 turn 처리. 마지막 user 발화를 history 에서
        # 버리면 다음 turn 의 AI 가 이전 맥락을 잊어버리므로, 짝이 안 맞을 때만 assistant
        # stub 을 채워 Anthropic API 의 user/assistant 교대 규칙을 만족시킨다.
        if not self.history:
            return
        if self.history[-1]["role"] != "user":
            return
        self.history.append({"role": "assistant", "content": stub})

    def messages_snapshot(self) -> list[dict[str, str]]:
        # provider 에 전달할 메시지 — content 는 str 만 유지 (assistant tool_use 라운드는 provider 내부에서 관리).
        return [{"role": m["role"], "content": m["content"]} for m in self.history]

    def set_provider(self, name: str) -> bool:
        if name == self.provider_name:
            return True
        if name not in list_providers():
            return False
        # 이전 provider 의 진행 중 호출 종료
        try:
            self.provider.kill_active()
        except Exception:  # noqa: BLE001
            pass
        self.provider_name = name
        self.provider = get_provider(name)
        logger.info("Provider switched: %s", name)
        return True

    def clear(self):
        self.history.clear()
        self.tool_journal.clear()


# ── WebSocket 핸들러 ───────────────────────────────────────────

async def handle_client(websocket):
    config = load_config()
    session = ChatSession(config)
    client_addr = websocket.remote_address
    logger.info("Client connected: %s", client_addr)

    # ── In-process 도구 등록 ──
    # server.py 는 단일 세션이라 agent_id 가 약함 — skill_loadout 이 세팅되면 그 ID 사용, 아니면 빈 문자열.
    agent_id = (getattr(session.skill_loadout, "agent_id", "") or "default")
    workspace = os.path.expanduser(f"~/opendesk/{agent_id}")
    try:
        os.makedirs(workspace, exist_ok=True)
    except OSError as e:
        logger.warning("workspace 생성 실패: %s (%s)", workspace, e)

    async def _broadcast(data: dict[str, Any]):
        await _send(websocket, data)

    # Hook pipeline 구성 — telemetry emitter 가 _broadcast 로 WS 송출.
    hooks_cfg = config.get("hooks", {})
    if hooks_cfg.get("enabled", True):
        # provider 가 CLI 면 completeness=partial 로 마킹.
        completeness = "partial" if session.provider_name == "anthropic_cli" else "full"
        try:
            session.hook_pipeline = build_default_pipeline(
                config=config,
                send_fn=_broadcast,
                telemetry_completeness=completeness,
            )
            logger.info("Hook pipeline initialized: %s", [h.name for h in session.hook_pipeline.hooks])
        except Exception:
            logger.exception("Hook pipeline init failed — falling back to raw chat()")
            session.hook_pipeline = None

    ask_port = WebSocketAskUserPort(
        pending=session.pending_user_asks,
        send_fn=_broadcast,
    )

    session.in_process_tools = build_in_process_registry(
        agent_id=agent_id,
        workspace=workspace,
        api_key=os.environ.get("ANTHROPIC_API_KEY", ""),
        model=session.model,
        task_queue=_get_task_queue(),
        cron_manager=_get_cron_manager(),
        memory_store=_get_memory_store(),
        ask_port=ask_port,
        on_event=_broadcast,
        tool_journal=session.tool_journal,
        plugin_registry=session.plugin_registry,
        route_preferences=default_preference_store(),
    )
    logger.info("In-process tools registered: %d", len(session.in_process_tools._tools))

    # 활성 provider 의 가용성 검사
    ok, info = await session.provider.check_available()
    if ok:
        await _send(websocket, {"type": "connected", "provider": session.provider_name, "model": info})
    else:
        await _send(websocket, {
            "type": "error",
            "message": f"Provider 사용 불가({session.provider_name}): {info}",
            "code": info if info.startswith("cli_") or info.endswith("_missing") else "provider_unavailable",
        })

    try:
        async for raw in websocket:
            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                await _send(websocket, {"type": "error", "message": "잘못된 JSON 형식", "code": "invalid_json"})
                continue

            msg_type = msg.get("type", "")

            if msg_type == "chat":
                # 직접 await 하면 _handle_chat 안의 AskUserTool 이 future 를 대기하는 동안
                # message pump 가 멈춰서 tool_user_response 를 못 받는 deadlock 발생.
                # background task 로 띄우고 즉시 다음 메시지 수신 대기.
                if session.active_chat_task and not session.active_chat_task.done():
                    await _send(websocket, {
                        "type": "error",
                        "message": "이전 응답이 아직 진행 중입니다",
                        "code": "server_busy",
                    })
                else:
                    session.active_chat_task = asyncio.create_task(
                        _handle_chat(websocket, session, msg)
                    )
            elif msg_type == "clear":
                session.clear()
                await _send(websocket, {"type": "cleared"})
            elif msg_type == "config":
                _handle_config(session, msg)
                await _send(websocket, {"type": "config_updated"})
            elif msg_type == "resume":
                await _handle_resume(websocket, session, msg)
            elif msg_type == "set_provider":
                await _handle_set_provider(websocket, session, msg)
            elif msg_type == "set_auth":
                await _handle_set_auth(websocket, session, msg)
            elif msg_type == "license.activate":
                await _handle_license_activate(websocket, session, msg)
            elif msg_type == "set_mcp_config":
                _handle_set_mcp_config(session, msg)
                await _send(websocket, {"type": "mcp_config_updated"})
            elif msg_type == "set_skill_loadout":
                _handle_set_skill_loadout(session, msg)
                await _send(websocket, {"type": "skill_loadout_updated"})
            elif msg_type == "set_plugin_registry":
                count = _handle_set_plugin_registry(session, msg)
                await _send(websocket, {"type": "plugin_registry_updated", "count": count})
            elif msg_type == "cancel":
                try:
                    session.provider.kill_active()
                except Exception:  # noqa: BLE001
                    pass
                await _send(websocket, {"type": "error", "message": "사용자에 의해 중단됨", "code": "user_cancelled"})
            elif msg_type == "auth_start":
                await _handle_auth_start(websocket, session)
            elif msg_type == "auth_cancel":
                await _handle_auth_cancel(websocket, session)
            elif msg_type == "ping":
                await _send(websocket, {"type": "pong"})
            elif msg_type == "tool_user_response":
                _handle_tool_user_response(session, msg)
            elif msg_type == "task_control":
                _handle_task_control(session, msg)
            else:
                await _send(websocket, {
                    "type": "error",
                    "message": f"알 수 없는 요청: {msg_type}",
                    "code": "unknown_type",
                })

    except websockets.exceptions.ConnectionClosed:
        logger.info("Client disconnected: %s", client_addr)
    except Exception as e:  # noqa: BLE001
        logger.error("Unexpected error: %s", e)
    finally:
        # disconnect/예외 시 — 진행 중 chat task 취소 → 매달린 future / subprocess 자동 정리.
        if session.active_chat_task and not session.active_chat_task.done():
            session.active_chat_task.cancel()
        # 종료 시 pending future cancel 해서 leak 방지
        for tool_use_id, fut in list(session.pending_user_asks.items()):
            if not fut.done():
                fut.set_result({"response": "", "selected": [], "cancelled": True})
        session.pending_user_asks.clear()
        try:
            session.provider.kill_active()
        except Exception:  # noqa: BLE001
            pass
        if session.auth_runner is not None:
            try:
                await session.auth_runner.cancel()
            except Exception:  # noqa: BLE001
                pass


async def _handle_chat(websocket, session: ChatSession, msg: dict[str, Any]):
    user_text = msg.get("message", "").strip()
    if not user_text:
        await _send(websocket, {"type": "error", "message": "메시지가 비어있습니다", "code": "empty_message"})
        return

    session.add_user_message(user_text)
    logger.info("Chat request [%s]: %s...", session.provider_name, user_text[:50])

    accumulated = ""
    # 발화(talking) 상태 — 첫 on_delta 직전에 talking_start, 종료 시 짝으로 talking_stop.
    # closure 로 묶어 콜백 간 상태 공유.
    talking_state = {"active": False}
    # session.skill_loadout.agent_id 가 있으면 사용, 없으면 빈 문자열 — Unity 측이 단일 캐릭터 환경에서 모든 메시지 수락.
    agent_id = (getattr(session.skill_loadout, "agent_id", "") or "")
    # server.py 는 단일 세션이라 session_id 개념이 약함 — 빈 문자열로 두면 Unity IsForMe 가 통과.
    session_id = ""

    async def _emit_talking_start():
        if talking_state["active"]:
            return
        talking_state["active"] = True
        await _send(websocket, {
            "type": "talking_start",
            "agent_id": agent_id,
            "role": "",
            "session_id": session_id,
            "timestamp": time.time(),
        })

    async def _emit_talking_stop(reason: str):
        if not talking_state["active"]:
            return
        talking_state["active"] = False
        await _send(websocket, {
            "type": "talking_stop",
            "agent_id": agent_id,
            "role": "",
            "session_id": session_id,
            "timestamp": time.time(),
            "reason": reason,
        })

    async def on_delta(text: str):
        nonlocal accumulated
        accumulated += text
        # 첫 텍스트 토큰 직전에 발화 시작 신호 (lip-sync / 타이핑 효과 트리거)
        await _emit_talking_start()
        # 채팅 UI 누적용 legacy 채널 — 깨지면 안 됨
        await _send(websocket, {"type": "delta", "text": text})
        # 캐릭터 입모양/타이핑용 lightweight 채널 (PROTOCOL.md text_delta)
        await _send(websocket, {
            "type": "text_delta",
            "agent_id": agent_id,
            "role": "",
            "session_id": session_id,
            "timestamp": time.time(),
            "text": text,
        })

    async def on_final(text: str, cost: float):
        formatted = markdown_to_tmp(text) if session.format_enabled else text
        session.add_assistant_message(text)
        # 발화 종료 — final 전에 talking_stop 으로 입모양 닫기 (UI 완성보다 먼저 닫혀야 자연스러움)
        await _emit_talking_stop("complete")
        await _send(websocket, {
            "type": "final",
            "text": formatted,
            "cost": round(cost, 6),
            "provider": session.provider_name,
        })
        logger.info("Response complete [%s]: %d chars, cost=$%.4f", session.provider_name, len(text), cost)

    async def on_error(message: str, code: str):
        # 이전엔 마지막 user 를 pop 해 user/assistant 교대 규칙을 맞췄지만, 그 결과
        # 사용자가 보낸 발화 자체가 미들웨어 history 에서 사라져 다음 turn AI 가
        # 이전 맥락을 잊는 메모리 결손이 발생. 대신 실패 stub 을 끼워 넣어
        # user 발화를 보존한다 (UI L1 영속과도 일관).
        session.mark_user_turn_failed()
        await _emit_talking_stop("error")
        await _send(websocket, {"type": "error", "message": message, "code": code})
        logger.warning("Provider error [%s][%s]: %s", session.provider_name, code, message[:120])

    async def on_status(text: str):
        await _send(websocket, {"type": "status", "text": text})

    async def on_tool_round(tool: str, tool_input: Any, tool_output: Any):
        # provider 가 tool_use 라운드를 끝낼 때마다 호출. session.tool_journal 에 누적되고
        # 후속으로 AI 가 read_tool_history 도구를 호출하면 그 결과로 반환된다.
        session.tool_journal.append(tool, tool_input, tool_output)

    callbacks = ProviderCallbacks(
        on_delta=on_delta,
        on_final=on_final,
        on_error=on_error,
        on_status=on_status,
        on_tool_round=on_tool_round,
    )

    # opendesk_routed provider 가 활성이면 credit 이벤트 sink 와 컨텍스트 주입.
    _configure_routed_provider(session, websocket)

    try:
        # hook chain 이 활성화되어 있으면 run_stream + HookedProvider 로 라우팅하여
        # telemetry/retry/rate_limit hook 이 동작하도록 한다. enabled=false 또는
        # pipeline 미구성 시 기존 chat() 경로로 폴백 (롤백 안전망).
        if session.hook_pipeline is not None:
            await _route_chat_via_hooks(
                session=session,
                callbacks=callbacks,
                on_status=on_status,
                emit_talking_start=_emit_talking_start,
            )
        else:
            await session.provider.chat(
                messages=session.messages_snapshot(),
                system_prompt=session.system_prompt,
                mcp_config=_compose_mcp_config(session),
                model=session.model,
                callbacks=callbacks,
                in_process_tools=session.in_process_tools,
            )
    except Exception as e:  # noqa: BLE001
        await on_error(str(e), "provider_exception")
    finally:
        # 안전망 — 위 분기에서 누락됐으면 interrupted 로 닫는다.
        if talking_state["active"]:
            await _emit_talking_stop("interrupted")


async def _route_chat_via_hooks(
    session: "ChatSession",
    callbacks: ProviderCallbacks,
    on_status,
    emit_talking_start,
) -> None:
    """run_stream + HookedProvider 라우팅. StreamEvent 를 기존 chat() 콜백으로 변환.

    이 함수는 hooks.enabled=true 경로에서만 호출된다.
    """
    import uuid

    reliability = session.config.get("reliability", {})
    base_rounds = int(reliability.get("max_tool_rounds", 8))
    bonus = int(reliability.get("max_tool_rounds_cache_bonus", 4))
    threshold = float(reliability.get("cache_hit_bonus_threshold", 0.6))

    def _ctx_factory() -> RequestCtx:
        return RequestCtx(
            request_id=f"req-{uuid.uuid4().hex[:12]}",
            agent_id=(getattr(session.skill_loadout, "agent_id", "") or ""),
            session_id="",
            provider=session.provider_name,
            model=session.model or "",
            started_at=time.monotonic(),
        )

    hooked = HookedProvider(
        inner=session.provider,
        pipeline=session.hook_pipeline,
        ctx_factory=_ctx_factory,
        base_max_tool_rounds=base_rounds,
        cache_bonus_rounds=bonus,
        cache_hit_bonus_threshold=threshold,
    )

    accumulated_parts: list[str] = []
    final_cost = 0.0

    async for evt in hooked.run_stream(
        messages=session.messages_snapshot(),
        system_prompt=session.system_prompt,
        mcp_config=_compose_mcp_config(session),
        model=session.model,
        in_process_tools=session.in_process_tools,
    ):
        if isinstance(evt, TextDeltaEvent):
            accumulated_parts.append(evt.text)
            await callbacks.on_delta(evt.text)
        elif isinstance(evt, ToolUseStartEvent):
            await on_status(f"도구 호출: {evt.name}")
        elif isinstance(evt, ToolUseResultEvent):
            session.tool_journal.append(evt.name, evt.input if False else {}, evt.result)
        elif isinstance(evt, MessageStopEvent):
            final_cost = float(evt.cost or 0.0)
            if evt.reason == "complete":
                final_text = evt.accumulated_text or "".join(accumulated_parts)
                await callbacks.on_final(final_text, final_cost)
            else:
                msg = evt.error_message or evt.reason
                code = evt.error_code or evt.reason
                await callbacks.on_error(msg, code)
            return


async def _handle_resume(websocket, session: ChatSession, msg: dict[str, Any]):
    conv_json = msg.get("conversation", "")
    if not conv_json:
        await _send(websocket, {"type": "error", "message": "conversation 데이터가 비어있습니다", "code": "empty_resume"})
        return
    try:
        conv_data = json.loads(conv_json)
    except json.JSONDecodeError:
        await _send(websocket, {"type": "error", "message": "conversation JSON 파싱 실패", "code": "invalid_resume_json"})
        return

    session.clear()
    messages = conv_data.get("Messages", [])
    agent_name = conv_data.get("AgentName", "AI 에이전트")

    for m in messages:
        sender = m.get("Sender", 0)
        text = m.get("Text", "")
        if not text:
            continue
        if sender == 0:
            session.history.append({"role": "user", "content": text})
        elif sender == 1:
            session.history.append({"role": "assistant", "content": text})

    if agent_name:
        session.system_prompt = (
            f"당신은 '{agent_name}'이라는 이름의 AI 에이전트입니다. "
            f"한국어로 대화하며, 사용자의 요청에 전문적으로 답변합니다. "
            f"이전 대화 맥락을 이어서 답변해주세요."
        )

    logger.info("Session resumed: %d messages, agent='%s'", len(session.history), agent_name)
    await _send(websocket, {"type": "config_updated"})


async def _handle_auth_start(websocket, session: ChatSession):
    """Claude CLI OAuth 로그인을 격리된 CLAUDE_CONFIG_DIR 하에서 시작."""
    if session.auth_runner is not None and session.auth_runner.is_active:
        await _send(websocket, {
            "type": "auth_event",
            "state": "failed",
            "message": "이미 진행 중인 로그인이 있습니다",
        })
        return

    cli_path = os.environ.get("OPENDESK_CLAUDE_CLI_PATH", "claude")
    config_dir = os.environ.get("CLAUDE_CONFIG_DIR")
    runner = AuthLoginRunner(cli_path=cli_path, config_dir=config_dir)
    session.auth_runner = runner

    async def emit(event: AuthEvent):
        # OAuth 인증 성공 시 anthropic_api provider 의 토큰 캐시 무효화 → 다음 chat 시 재로드.
        if event.state == "success":
            for name in list_providers():
                prov = get_provider(name)
                if hasattr(prov, "invalidate_oauth_cache"):
                    try:
                        prov.invalidate_oauth_cache()
                        logger.info("OAuth cache invalidated for provider %s", name)
                    except Exception as e:  # noqa: BLE001
                        logger.warning("invalidate_oauth_cache 실패 [%s]: %s", name, e)
        await _send(websocket, {
            "type": "auth_event",
            "state": event.state,
            "message": event.message,
            "url": event.url or "",
            "code": event.code or "",
        })

    await runner.start(emit)


async def _handle_auth_cancel(websocket, session: ChatSession):
    if session.auth_runner is None:
        await _send(websocket, {"type": "auth_event", "state": "failed", "message": "진행 중인 로그인이 없습니다"})
        return
    await session.auth_runner.cancel()
    session.auth_runner = None


async def _handle_set_provider(websocket, session: ChatSession, msg: dict[str, Any]):
    name = msg.get("provider", "")
    if not name:
        await _send(websocket, {"type": "error", "message": "provider 이름이 비어있습니다", "code": "invalid_provider"})
        return
    if not session.set_provider(name):
        await _send(websocket, {
            "type": "error",
            "message": f"알 수 없는 provider: {name}. 사용 가능: {list_providers()}",
            "code": "unknown_provider",
        })
        return

    # 즉시 가용성 재검사
    ok, info = await session.provider.check_available()
    await _send(websocket, {
        "type": "provider_changed",
        "provider": name,
        "available": ok,
        "info": info,
    })


def _compose_mcp_config(session: ChatSession) -> Optional[dict[str, Any]]:
    """외부 MCP 서버 + 내장 OpenDesk Skills MCP 서버를 합쳐 provider 에 넘긴다.

    Skill loadout 이 비어있고 외부 MCP 도 없으면 None 반환 → MCP 비활성.
    """
    base = session.mcp_config
    external_servers = list((base or {}).get("servers") or [])

    builtin_servers: list[dict[str, Any]] = []
    if not session.skill_loadout.is_empty:
        env_dict = session.skill_loadout.build_env()
        env_pairs = [{"key": k, "value": v} for k, v in env_dict.items()]
        builtin_servers.append({
            "name": "opendesk-skills",
            "transport": "stdio",
            "command": sys.executable or "python",
            "args": [str(Path(__file__).parent / "opendesk_skills_mcp.py")],
            "env": env_pairs,
        })

    if not external_servers and not builtin_servers:
        return None

    return {
        "agentId": (base or {}).get("agentId") or session.skill_loadout.agent_id,
        # 내장 서버를 먼저 등록 — 모델이 항상 사용 가능하다고 가정할 수 있게.
        "servers": builtin_servers + external_servers,
    }


def _handle_set_skill_loadout(session: ChatSession, msg: dict[str, Any]):
    payload = msg.get("payload")
    if not isinstance(payload, dict):
        session.skill_loadout.clear()
        return
    session.skill_loadout.update(payload)
    logger.info("Skill loadout set: %d skill(s)", len(session.skill_loadout.active_ids))


def _handle_set_mcp_config(session: ChatSession, msg: dict[str, Any]):
    payload = msg.get("payload")
    if payload is None:
        session.mcp_config = None
    elif isinstance(payload, dict):
        session.mcp_config = payload
    else:
        logger.warning("set_mcp_config payload 형식 오류: %r", type(payload).__name__)
        session.mcp_config = None
    count = len((session.mcp_config or {}).get("servers") or []) if session.mcp_config else 0
    logger.info("MCP config set: %d server(s)", count)


def _handle_config(session: ChatSession, msg: dict[str, Any]):
    if "systemPrompt" in msg:
        session.system_prompt = msg["systemPrompt"]
        logger.info("System prompt updated: %s...", session.system_prompt[:60])
    if "markdownToTmp" in msg:
        session.format_enabled = bool(msg["markdownToTmp"])
    if "model" in msg:
        session.model = str(msg["model"] or "")
        logger.info("Model set: %s", session.model or "(default)")
    if "complexityHint" in msg:
        hint = str(msg.get("complexityHint") or "auto").lower()
        if hint not in ("auto", "simple", "complex"):
            hint = "auto"
        session.complexity_hint = hint
    if "agentRole" in msg:
        session.agent_role = str(msg.get("agentRole") or "default")


async def _handle_license_activate(websocket, session: ChatSession, msg: dict[str, Any]):
    """Unity 가 보내는 license.activate 메시지를 routing_client.activate 로 프록시.

    실 서버 모드에서는 라이선스 서비스가 직접 HTTPS 로 클라우드에 붙는 게 정석이지만,
    Phase 1 (mock 서버 인메모리) 에서는 미들웨어가 그 통로를 임시로 대행한다.
    """
    license_key = str(msg.get("licenseKey") or "").strip()
    fingerprint = str(msg.get("fingerprint") or "").strip()
    device_name = str(msg.get("deviceName") or "").strip() or "Unnamed Device"

    if not license_key or not fingerprint:
        await _send(websocket, {
            "type": "license.error",
            "code": "invalid_input",
            "message": "licenseKey 와 fingerprint 가 필요합니다",
        })
        return

    try:
        result = await get_routing_client().activate(license_key, fingerprint, device_name)
    except LicenseError as e:
        await _send(websocket, {
            "type": "license.error",
            "code": e.code,
            "message": str(e),
        })
        return
    except Exception as e:  # noqa: BLE001
        logger.exception("라이선스 활성화 실패")
        await _send(websocket, {
            "type": "license.error",
            "code": "activation_failed",
            "message": str(e),
        })
        return

    await _send(websocket, {
        "type": "license.activated",
        "jwt": result.jwt,
        "refreshToken": result.refresh_token,
        "userId": result.user_id,
        "planTier": result.plan_tier,
        "balance": result.balance,
    })


def _configure_routed_provider(session: ChatSession, websocket) -> None:
    """provider 가 opendesk_routed 면 credit 이벤트 sink + 컨텍스트 주입."""
    provider = session.provider
    # 동적 import 회피 — 메서드 존재 여부로 판별
    if not hasattr(provider, "set_credit_event_sink"):
        return

    async def _sink(payload: dict[str, Any]) -> None:
        await _send(websocket, payload)

    try:
        provider.set_credit_event_sink(_sink)
        provider.set_agent_role(session.agent_role)
        provider.set_complexity_hint(session.complexity_hint)
    except Exception:  # noqa: BLE001
        logger.exception("opendesk_routed provider 컨텍스트 주입 실패")


async def _handle_set_auth(websocket, session: ChatSession, msg: dict[str, Any]):
    """라이선스 JWT 주입. routing_client 에 바인딩하여 이후 opendesk_routed
    provider 가 라우팅/크레딧 API 호출에 사용한다.
    """
    jwt = str(msg.get("jwt") or "").strip()
    if not jwt:
        # 빈 jwt → 언바인딩 (로그아웃)
        session.license_jwt = ""
        try:
            get_routing_client().unbind()
        except Exception:  # noqa: BLE001
            pass
        await _send(websocket, {"type": "auth_status", "authenticated": False})
        return

    try:
        get_routing_client().bind_user(jwt)
    except LicenseError as e:
        await _send(websocket, {
            "type": "error",
            "message": f"라이선스 인증 실패: {e}",
            "code": e.code,
        })
        return
    except Exception as e:  # noqa: BLE001
        await _send(websocket, {
            "type": "error",
            "message": f"라이선스 바인딩 실패: {e}",
            "code": "auth_bind_failed",
        })
        return

    session.license_jwt = jwt
    await _send(websocket, {"type": "auth_status", "authenticated": True})

    # 잔액 즉시 푸시
    try:
        balance = await get_routing_client().balance()
        await _send(websocket, {
            "type": "credit.balance",
            "balance": balance.balance,
            "held": balance.held,
        })
    except Exception:  # noqa: BLE001
        logger.exception("초기 잔액 조회 실패")


def _handle_tool_user_response(session: ChatSession, msg: dict[str, Any]):
    """ask_user / route_capability 도구 응답 — pending future 해소.

    route_capability 의 capability_pick 카드는 ``remember`` 불 필드를 추가로 보낸다.
    기존 ask_user 카드는 이 필드를 보내지 않으며 기본값 False 로 처리.
    """
    tool_use_id = msg.get("tool_use_id", "")
    fut = session.pending_user_asks.get(tool_use_id)
    if not fut or fut.done():
        logger.warning("tool_user_response: unknown/done id %r", tool_use_id)
        return
    fut.set_result({
        "response": msg.get("response", ""),
        "selected": msg.get("selected", []),
        "remember": bool(msg.get("remember", False)),
    })


def _handle_set_plugin_registry(session: ChatSession, msg: dict[str, Any]) -> int:
    """Unity 가 push 한 설치 플러그인 목록을 route_capability 가 볼 수 있게 캐시.

    payload: ``[{id, display_name?, vendor?, author?, capabilities[]}]``.
    payload=None / 빈 배열이면 카탈로그 비움.
    """
    payload = msg.get("payload")
    if payload is None:
        session.plugin_registry.clear()
        logger.info("Plugin registry cleared")
        return 0
    if not isinstance(payload, list):
        logger.warning("set_plugin_registry payload 형식 오류: %r", type(payload).__name__)
        return 0
    count = session.plugin_registry.replace(payload)
    logger.info("Plugin registry set: %d plugin(s)", count)
    return count


def _handle_task_control(session: ChatSession, msg: dict[str, Any]):
    """task_control op — UI 측 stop/update 트리거."""
    action = msg.get("action", "")
    task_id = msg.get("task_id", "")
    queue = _get_task_queue()
    if action == "stop":
        queue.stop(task_id)
    elif action == "update":
        patch = msg.get("patch") or {}
        queue.update(task_id, **patch)
    else:
        logger.warning("Unknown task_control action: %r", action)


async def _send(websocket, data: dict[str, Any]):
    try:
        await websocket.send(json.dumps(data, ensure_ascii=False))
    except websockets.exceptions.ConnectionClosed:
        pass


# ── 메인 ───────────────────────────────────────────────────────

async def main():
    config = load_config()
    apply_env_from_config(config)
    host = config.get("host", "localhost")
    port = config.get("port", 8765)

    logger.info("=" * 50)
    logger.info("  OpenDesk AI Middleware Server")
    logger.info("=" * 50)
    logger.info("  Listening on ws://%s:%s", host, port)
    logger.info("  Default provider: %s", DEFAULT_PROVIDER)
    logger.info("  Available providers: %s", list_providers())
    logger.info("  Formatting: %s", "ON" if config.get("formatting", {}).get("markdownToTmp", True) else "OFF")
    logger.info("=" * 50)

    stop = asyncio.get_event_loop().create_future()

    def _signal_handler():
        if not stop.done():
            stop.set_result(True)

    try:
        loop = asyncio.get_event_loop()
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, _signal_handler)
    except NotImplementedError:
        pass

    async with websockets.serve(handle_client, host, port):
        logger.info("Server ready. Waiting for Unity connection...")
        try:
            await stop
        except asyncio.CancelledError:
            pass

    # 글로벌 자원 정리 — task_queue / cron_manager
    global _TASK_QUEUE, _CRON_MANAGER
    if _TASK_QUEUE is not None:
        try:
            await _TASK_QUEUE.shutdown()
        except Exception as e:  # noqa: BLE001
            logger.warning("task_queue shutdown 실패: %s", e)
    if _CRON_MANAGER is not None:
        try:
            _CRON_MANAGER.shutdown()
        except Exception as e:  # noqa: BLE001
            logger.warning("cron_manager shutdown 실패: %s", e)

    logger.info("Server stopped.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        logger.info("Server stopped by user (Ctrl+C)")
