"""
OpenDesk Claude 미들웨어 서버

Unity ↔ Claude CLI 사이의 WebSocket 브릿지.
- Unity에서 채팅 메시지를 받아 Claude CLI 서브프로세스로 전달
- Claude 응답을 스트리밍(delta) + 최종(final)으로 Unity에 중계
- 대화 히스토리 관리
- 마크다운 → TMP 리치텍스트 포매팅
"""

import asyncio
import json
import logging
import signal
import sys
from pathlib import Path

import websockets

from claude_bridge import ClaudeBridge
from formatter import markdown_to_tmp

# ── OpenDesk 에이전트 규칙 로드 ──────────────────────────────────

OPENDESK_MD_PATH = Path(__file__).parent / "opendesk.md"


def load_opendesk_rules() -> str:
    """opendesk.md 파일에서 에이전트 행동 규칙 로드"""
    if OPENDESK_MD_PATH.exists():
        return OPENDESK_MD_PATH.read_text(encoding="utf-8")
    return ""

# ── 로깅 설정 ──────────────────────────────────────────────────

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("server")

# ── 설정 로드 ──────────────────────────────────────────────────

CONFIG_PATH = Path(__file__).parent / "config.json"


def load_config() -> dict:
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            return json.load(f)
    return {
        "port": 8765,
        "host": "localhost",
        "claude": {
            "systemPrompt": "당신은 친절한 AI 어시스턴트입니다. 한국어로 대답합니다.",
            "maxTurns": 50,
            "timeoutSeconds": 300,
            "cliPath": "claude",
        },
        "formatting": {
            "markdownToTmp": True,
            "maxResponseLength": 10000,
        },
    }


# ── 세션 관리 ──────────────────────────────────────────────────

class ChatSession:
    """클라이언트별 대화 세션"""

    def __init__(self, config: dict):
        self.config = config
        claude_cfg = config.get("claude", {})
        self.system_prompt: str = claude_cfg.get(
            "systemPrompt", "당신은 친절한 AI 어시스턴트입니다."
        )
        self.max_turns: int = claude_cfg.get("maxTurns", 50)
        self.history: list[dict] = []   # [{"role": "user"/"assistant", "text": "..."}]
        self.bridge = ClaudeBridge(
            cli_path=claude_cfg.get("cliPath", "claude"),
            timeout=claude_cfg.get("timeoutSeconds", 120),
        )
        self.format_enabled: bool = config.get("formatting", {}).get("markdownToTmp", True)

    def add_user_message(self, text: str):
        self.history.append({"role": "user", "text": text})
        # 히스토리 제한
        if len(self.history) > self.max_turns * 2:
            self.history = self.history[-(self.max_turns * 2):]

    def add_assistant_message(self, text: str):
        self.history.append({"role": "assistant", "text": text})

    def build_prompt(self) -> str:
        """히스토리를 Claude CLI용 단일 프롬프트로 조합"""
        parts = []

        # 시스템 프롬프트
        if self.system_prompt:
            parts.append(f"[System]\n{self.system_prompt}\n")

        # OpenDesk 에이전트 규칙
        rules = load_opendesk_rules()
        if rules:
            parts.append(f"[Agent Rules]\n{rules}\n")

        parts.append("중요: 당신은 채팅 에이전트입니다. 파일 읽기, 코드 실행 등의 도구는 사용하지 마세요. 대화로만 응답하세요.\n")

        # 마지막 user 메시지 찾기
        last_user_idx = -1
        for i in range(len(self.history) - 1, -1, -1):
            if self.history[i]["role"] == "user":
                last_user_idx = i
                break

        if last_user_idx < 0:
            return "안녕하세요"

        # 대화 히스토리 (마지막 user 메시지 제외)
        if last_user_idx > 0:
            parts.append("[이전 대화]")
            for msg in self.history[:last_user_idx]:
                role = "사용자" if msg["role"] == "user" else "AI"
                # 히스토리 텍스트는 짧게 자르기 (프롬프트 길이 제한)
                text = msg["text"][:500]
                parts.append(f"{role}: {text}")
            parts.append("")

        # 현재 메시지
        current = self.history[last_user_idx]["text"]
        parts.append(f"[현재 질문]\n{current}")

        return "\n".join(parts)

    def clear(self):
        self.history.clear()


# ── WebSocket 핸들러 ───────────────────────────────────────────

async def handle_client(websocket):
    """개별 클라이언트 연결 처리"""
    config = load_config()
    session = ChatSession(config)
    client_addr = websocket.remote_address
    logger.info(f"Client connected: {client_addr}")

    # CLI 확인 + connected 메시지 전송
    cli_ok, cli_info = await session.bridge.check_cli_available()
    if cli_ok:
        await _send(websocket, {
            "type": "connected",
            "model": cli_info,
        })
    else:
        await _send(websocket, {
            "type": "error",
            "message": f"Claude CLI를 찾을 수 없습니다: {cli_info}",
            "code": "cli_not_found",
        })

    try:
        async for raw in websocket:
            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                await _send(websocket, {
                    "type": "error",
                    "message": "잘못된 JSON 형식",
                    "code": "invalid_json",
                })
                continue

            msg_type = msg.get("type", "")

            if msg_type == "chat":
                await _handle_chat(websocket, session, msg)

            elif msg_type == "clear":
                session.clear()
                await _send(websocket, {"type": "cleared"})
                logger.info("History cleared")

            elif msg_type == "config":
                _handle_config(session, msg)
                await _send(websocket, {"type": "config_updated"})

            elif msg_type == "resume":
                await _handle_resume(websocket, session, msg)

            elif msg_type == "ping":
                await _send(websocket, {"type": "pong"})

            else:
                await _send(websocket, {
                    "type": "error",
                    "message": f"알 수 없는 요청: {msg_type}",
                    "code": "unknown_type",
                })

    except websockets.exceptions.ConnectionClosed:
        logger.info(f"Client disconnected: {client_addr}")
    except Exception as e:
        logger.error(f"Unexpected error: {e}")
    finally:
        # 진행 중인 CLI 프로세스 정리
        session.bridge.kill_active_process()


async def _handle_chat(websocket, session: ChatSession, msg: dict):
    """채팅 메시지 처리 — CLI 호출 + 스트리밍 중계"""
    user_text = msg.get("message", "").strip()
    if not user_text:
        await _send(websocket, {
            "type": "error",
            "message": "메시지가 비어있습니다",
            "code": "empty_message",
        })
        return

    session.add_user_message(user_text)
    prompt = session.build_prompt()

    logger.info(f"Chat request: {user_text[:50]}...")

    full_response = ""

    async def on_delta(text: str):
        nonlocal full_response
        full_response += text
        await _send(websocket, {
            "type": "delta",
            "text": text,
        })

    async def on_final(text: str, cost: float):
        nonlocal full_response
        # 포매팅 적용 (final에서만)
        formatted = text
        if session.format_enabled:
            formatted = markdown_to_tmp(text)

        session.add_assistant_message(text)
        await _send(websocket, {
            "type": "final",
            "text": formatted,
            "cost": round(cost, 6),
        })
        logger.info(f"Response complete: {len(text)} chars, cost=${cost:.4f}")

    async def on_error(message: str, code: str):
        # 에러 시 히스토리에서 마지막 유저 메시지 제거 (실패한 요청)
        if session.history and session.history[-1]["role"] == "user":
            session.history.pop()
        await _send(websocket, {
            "type": "error",
            "message": message,
            "code": code,
        })
        logger.warning(f"CLI error [{code}]: {message[:100]}")

    async def on_status(text: str):
        await _send(websocket, {
            "type": "status",
            "text": text,
        })
        logger.debug(f"Status: {text}")

    await session.bridge.send_message(prompt, on_delta, on_final, on_error, on_status)


async def _handle_resume(websocket, session: ChatSession, msg: dict):
    """대화 히스토리 JSON을 로드하여 세션에 주입 — 대화 이어나기"""
    conv_json = msg.get("conversation", "")
    if not conv_json:
        await _send(websocket, {
            "type": "error",
            "message": "conversation 데이터가 비어있습니다",
            "code": "empty_resume",
        })
        return

    try:
        conv_data = json.loads(conv_json)
    except json.JSONDecodeError:
        await _send(websocket, {
            "type": "error",
            "message": "conversation JSON 파싱 실패",
            "code": "invalid_resume_json",
        })
        return

    # 히스토리 클리어 후 JSON에서 복원
    session.clear()

    messages = conv_data.get("Messages", [])
    agent_name = conv_data.get("AgentName", "AI 에이전트")

    for m in messages:
        sender = m.get("Sender", 0)  # 0=User, 1=Agent, 2=System
        text = m.get("Text", "")
        if not text:
            continue

        if sender == 0:  # User
            session.history.append({"role": "user", "text": text})
        elif sender == 1:  # Agent
            session.history.append({"role": "assistant", "text": text})
        # System 메시지는 히스토리에 포함하지 않음

    # 시스템 프롬프트에 에이전트 이름 반영
    if agent_name:
        session.system_prompt = (
            f"당신은 '{agent_name}'이라는 이름의 AI 에이전트입니다. "
            f"한국어로 대화하며, 사용자의 요청에 전문적으로 답변합니다. "
            f"이전 대화 맥락을 이어서 답변해주세요."
        )

    resumed_count = len(session.history)
    logger.info(f"Session resumed: {resumed_count} messages, agent='{agent_name}'")

    await _send(websocket, {
        "type": "config_updated",
    })


def _handle_config(session: ChatSession, msg: dict):
    """설정 변경"""
    if "systemPrompt" in msg:
        session.system_prompt = msg["systemPrompt"]
        logger.info(f"System prompt updated: {session.system_prompt[:50]}...")
    if "markdownToTmp" in msg:
        session.format_enabled = bool(msg["markdownToTmp"])
        logger.info(f"Formatting {'enabled' if session.format_enabled else 'disabled'}")


async def _send(websocket, data: dict):
    """JSON 직렬화 후 WebSocket 전송"""
    try:
        await websocket.send(json.dumps(data, ensure_ascii=False))
    except websockets.exceptions.ConnectionClosed:
        pass


# ── 메인 ───────────────────────────────────────────────────────

async def main():
    config = load_config()
    host = config.get("host", "localhost")
    port = config.get("port", 8765)

    logger.info("=" * 50)
    logger.info("  OpenDesk Claude Middleware Server")
    logger.info("=" * 50)
    logger.info(f"  Listening on ws://{host}:{port}")
    logger.info(f"  CLI path: {config.get('claude', {}).get('cliPath', 'claude')}")
    logger.info(f"  Formatting: {'ON' if config.get('formatting', {}).get('markdownToTmp', True) else 'OFF'}")
    logger.info("=" * 50)

    # graceful shutdown
    stop = asyncio.get_event_loop().create_future()

    def _signal_handler():
        if not stop.done():
            stop.set_result(True)

    try:
        loop = asyncio.get_event_loop()
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, _signal_handler)
    except NotImplementedError:
        # Windows에서 add_signal_handler 미지원 — KeyboardInterrupt로 대체
        pass

    async with websockets.serve(handle_client, host, port):
        logger.info("Server ready. Waiting for Unity connection...")
        try:
            await stop
        except asyncio.CancelledError:
            pass

    logger.info("Server stopped.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        logger.info("Server stopped by user (Ctrl+C)")
