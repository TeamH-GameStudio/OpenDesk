"""WebSocket 서버 — Unity 클라이언트와의 통신"""

import asyncio
import json
import logging
import websockets
from typing import Set, Callable, Awaitable

logger = logging.getLogger("websocket")


class AgentWebSocketServer:
    def __init__(self, host: str = "0.0.0.0", port: int = 8765):
        self.host = host
        self.port = port
        self._clients: Set[websockets.WebSocketServerProtocol] = set()
        self._message_handler: Callable[[dict], Awaitable[None]] | None = None

    def set_message_handler(self, handler: Callable[[dict], Awaitable[None]]):
        self._message_handler = handler

    async def broadcast(self, event: dict):
        """모든 연결된 클라이언트에 이벤트 전송"""
        if not self._clients:
            return
        msg = json.dumps(event, ensure_ascii=False, default=str)
        await asyncio.gather(
            *[c.send(msg) for c in self._clients],
            return_exceptions=True,
        )

    async def _handle_client(self, websocket):
        self._clients.add(websocket)
        addr = websocket.remote_address
        logger.info(f"Client connected: {addr}")
        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                except json.JSONDecodeError:
                    logger.warning(f"Invalid JSON from {addr}")
                    continue
                if self._message_handler:
                    await self._message_handler(data)
        except websockets.ConnectionClosed:
            logger.info(f"Client disconnected: {addr}")
        finally:
            self._clients.discard(websocket)

    async def start(self):
        async with websockets.serve(self._handle_client, self.host, self.port):
            logger.info(f"WebSocket server: ws://{self.host}:{self.port}")
            await asyncio.Future()  # run forever
