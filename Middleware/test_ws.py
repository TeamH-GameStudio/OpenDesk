"""
WebSocket 통합 테스트 — main.py를 먼저 실행한 뒤 이 스크립트 실행.

사용법:
    python test_ws.py
"""

import asyncio
import json
import websockets


async def test():
    async with websockets.connect("ws://localhost:8765") as ws:
        # 1. 전체 상태 요청
        print("=== status_request ===")
        await ws.send(json.dumps({"type": "status_request"}))

        # 상태 응답 수집 (에이전트 3개 x 2 = 6 메시지)
        for _ in range(6):
            msg = json.loads(await ws.recv())
            print(f"  [{msg['type']}] agent={msg.get('agent_id','')} "
                  f"state={msg.get('state','')} sessions={len(msg.get('sessions',[]))}")

        # 2. researcher에게 메시지
        print("\n=== chat_message -> researcher ===")
        await ws.send(json.dumps({
            "type": "chat_message",
            "agent_id": "researcher",
            "message": "안녕하세요! 간단히 자기소개 해주세요."
        }))

        # 응답 수집
        while True:
            msg = json.loads(await ws.recv())
            if msg["type"] == "agent_message":
                print(f"  [AGENT] {msg.get('role','')}: {msg['message'][:100]}")
            elif msg["type"] == "agent_thinking":
                print(f"  [THINK] {msg['thinking'][:80]}...")
            elif msg["type"] == "agent_action":
                print(f"  [ACTION] {msg['action']}")
            elif msg["type"] == "agent_state":
                print(f"  [STATE] {msg['state']} {msg.get('tool','')}")
                if msg["state"] == "idle":
                    break

        # 3. 춤추기 유도
        print("\n=== chat_message -> researcher (dancing) ===")
        await ws.send(json.dumps({
            "type": "chat_message",
            "agent_id": "researcher",
            "message": "야호! 프로젝트 성공했어! 신나게 축하하자!"
        }))

        while True:
            msg = json.loads(await ws.recv())
            if msg["type"] == "agent_message":
                print(f"  [AGENT] {msg['message'][:100]}")
            elif msg["type"] == "agent_action":
                print(f"  [ACTION] {msg['action']}")
            elif msg["type"] == "agent_state":
                print(f"  [STATE] {msg['state']} {msg.get('tool','')}")
                if msg["state"] == "idle":
                    break

        print("\nTest complete.")


if __name__ == "__main__":
    asyncio.run(test())
