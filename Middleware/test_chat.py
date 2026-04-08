"""
단독 대화 테스트 — WebSocket 없이 AgentRunner만 동작 확인.

사용법:
    python test_chat.py
"""

import asyncio
import os

from dotenv import load_dotenv
load_dotenv()
from tools.registry import ToolRegistry
from tools.file_read import FileReadTool
from tools.file_write import FileWriteTool
from agent.runner import AgentRunner
from agent.session_store import SessionStore
from formatter import extract_action


async def print_event(event):
    t = event["type"]
    if t == "agent_state":
        print(f"  [STATE] [{event['state']}] {event.get('tool', '')}")
    elif t == "agent_message":
        raw = event['message']
        clean, action = extract_action(raw)
        print(f"  [AGENT] {clean[:200]}")
        if action:
            print(f"  [ACTION] {action}")
    elif t == "agent_thinking":
        print(f"  [THINK] {event['thinking'][:200]}...")


async def test():
    store = SessionStore("/tmp/test-sessions")
    registry = ToolRegistry()
    registry.register(FileReadTool("/tmp/test-workspace"))
    registry.register(FileWriteTool("/tmp/test-workspace"))

    from config import ACTION_INSTRUCTION

    runner = AgentRunner(
        agent_id="test",
        role="테스트",
        system_prompt="You are a helpful assistant. Communicate in Korean.\n\n" + ACTION_INSTRUCTION,
        tool_registry=registry,
        session_store=store,
        thinking_budget=2000,
        on_event=print_event,
    )

    print("=== Test 1: 인사 ===")
    print("나: 안녕!")
    await runner.send_message("안녕!")

    print("\n=== Test 2: 파일 생성 ===")
    print("나: hello.txt에 '안녕하세요!' 적어줘")
    await runner.send_message("hello.txt에 '안녕하세요!' 적어줘")

    print(f"\n세션 목록: {runner.list_sessions()}")

    print("\n=== Test 3: 액션 유도 (dancing) ===")
    print("나: 야호! 프로젝트 대성공! 축하 파티하자!")
    await runner.send_message("야호! 프로젝트 대성공! 축하 파티하자!")

    print(f"\n최종 세션 목록: {runner.list_sessions()}")


if __name__ == "__main__":
    asyncio.run(test())
