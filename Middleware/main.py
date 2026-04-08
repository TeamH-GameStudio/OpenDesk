"""
OpenDesk 에이전트 미들웨어 서버 — 진입점.

사용법:
    python main.py
"""

import asyncio
import logging
import os

from dotenv import load_dotenv
load_dotenv()
import signal

from config import AGENTS_CONFIG
from agent.session_manager import AgentSessionManager
from websocket.server import AgentWebSocketServer

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(levelname)s: %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("main")


async def main():
    api_key = os.environ.get("ANTHROPIC_API_KEY", "")
    if not api_key:
        logger.warning("ANTHROPIC_API_KEY not set. API calls will fail.")

    ws_server = AgentWebSocketServer(host="0.0.0.0", port=8765)
    manager = AgentSessionManager(ws_server=ws_server, api_key=api_key)

    for agent_id, config in AGENTS_CONFIG.items():
        manager.create_agent(agent_id, config)

    ws_server.set_message_handler(manager.handle_unity_message)

    logger.info("=" * 50)
    logger.info("  OpenDesk Agent Middleware Server")
    logger.info("=" * 50)
    logger.info(f"  Agents: {', '.join(AGENTS_CONFIG.keys())}")
    logger.info(f"  WebSocket: ws://0.0.0.0:8765")
    logger.info(f"  API Key: {'set' if api_key else 'NOT SET'}")
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
        pass  # Windows

    await ws_server.start()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        logger.info("Server stopped by user (Ctrl+C)")
