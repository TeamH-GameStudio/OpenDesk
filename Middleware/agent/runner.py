"""
에이전트 러너 — 대화 기반 에이전트 핵심 루프.

- send_message(msg) -> thinking -> tool use -> 응답
- thinking 활성화 -> 추론 과정을 Unity에 실시간 전송
- 세션 저장소와 연동 -> 대화 기록 자동 저장
- compaction: 대화가 길어지면 자동 압축
- memory: 세션 간 영구 기억을 시스템 프롬프트에 주입
"""

import anthropic
import asyncio
import json
import time
from typing import Callable, Awaitable, Any, Optional

from tools.registry import ToolRegistry
from agent.session_store import SessionStore
from agent.compaction import compact_messages, should_compact
from agent.memory import MemoryStore


class AgentRunner:
    def __init__(
        self,
        agent_id: str,
        role: str,
        system_prompt: str,
        tool_registry: ToolRegistry,
        session_store: SessionStore,
        model: str = "claude-sonnet-4-6",
        max_tool_iterations: int = 20,
        thinking_budget: int = 4000,
        api_key: str | None = None,
        on_event: Callable[[dict], Awaitable[None]] | None = None,
        memory_store: MemoryStore | None = None,
    ):
        self.agent_id = agent_id
        self.role = role
        self.system_prompt = system_prompt
        self.tools = tool_registry
        self.model = model
        self.max_tool_iterations = max_tool_iterations
        self.thinking_budget = thinking_budget
        self.client = anthropic.AsyncAnthropic(api_key=api_key)
        self._on_event = on_event
        self._session_store = session_store
        self._memory_store = memory_store

        self.messages: list[dict] = []
        self.current_session_id: Optional[str] = None
        self._is_busy = False

        self._restore_current_session()

    # ── 세션 복원 ──

    def _restore_current_session(self):
        sid = self._session_store.get_current(self.agent_id)
        if sid:
            data = self._session_store.load_session(self.agent_id, sid)
            if data:
                self.current_session_id = sid
                self.messages = _serialize_messages(data.get("messages", []))

    def _auto_save(self):
        if self.current_session_id:
            serializable = _serialize_messages(self.messages)
            self._session_store.save_messages(
                self.agent_id, self.current_session_id, serializable
            )

    # ── 세션 관리 ──

    def new_session(self, title: str = "") -> dict:
        self._auto_save()
        meta = self._session_store.create_session(self.agent_id, title)
        self.current_session_id = meta["session_id"]
        self.messages = []
        return meta

    def switch_session(self, session_id: str) -> bool:
        self._auto_save()
        data = self._session_store.load_session(self.agent_id, session_id)
        if not data:
            return False
        self.current_session_id = session_id
        self.messages = data.get("messages", [])
        self._session_store.set_current(self.agent_id, session_id)
        return True

    def list_sessions(self) -> list:
        return self._session_store.list_sessions(self.agent_id)

    def delete_session(self, session_id: str):
        self._session_store.delete_session(self.agent_id, session_id)
        if self.current_session_id == session_id:
            self.new_session()

    # ── 이벤트 발행 ──

    async def _emit(self, event_type: str, **data):
        event = {
            "type": event_type,
            "agent_id": self.agent_id,
            "role": self.role,
            "session_id": self.current_session_id,
            "timestamp": time.time(),
            **data,
        }
        if self._on_event:
            await self._on_event(event)

    # ── 시스템 프롬프트 ──

    def _build_system_prompt(self) -> str:
        """기본 시스템 프롬프트 + 메모리 컨텍스트 결합"""
        parts = [self.system_prompt]
        if self._memory_store:
            memory_ctx = self._memory_store.build_memory_context(self.agent_id)
            if memory_ctx:
                parts.append(memory_ctx)
        return "\n\n".join(parts)

    # ── 대화 ──

    async def send_message(self, user_message: str) -> str:
        if self._is_busy:
            await self._emit(
                "agent_message", message="잠깐만요, 아직 이전 작업 중이에요."
            )
            return "Agent is busy."

        if not self.current_session_id:
            self.new_session()

        self._is_busy = True
        self.messages.append({"role": "user", "content": user_message})

        # compaction: 대화가 길면 자동 압축
        if should_compact(self.messages):
            self.messages = await compact_messages(
                self.client, self.messages, agent_role=self.role
            )
            self._auto_save()

        await self._emit("agent_state", state="thinking")

        try:
            result = await self._process_response()
            self._auto_save()
            return result
        except Exception as e:
            await self._emit(
                "agent_state", state="error", error="exception", message=str(e)
            )
            self._auto_save()
            return f"Error: {e}"
        finally:
            self._is_busy = False

    async def _process_response(self) -> str:
        iteration = 0

        while iteration < self.max_tool_iterations:
            iteration += 1

            # ── streaming API 호출 + 이벤트 처리 ──
            response_text = ""

            try:
                async with self.client.messages.stream(
                    model=self.model,
                    max_tokens=8000,
                    system=self._build_system_prompt(),
                    tools=self.tools.to_anthropic_schemas(),
                    messages=self.messages,
                    thinking={
                        "type": "enabled",
                        "budget_tokens": self.thinking_budget,
                    },
                ) as stream:
                    async for event in stream:
                        event_name = type(event).__name__

                        # thinking 청크
                        if event_name == "ThinkingEvent":
                            await self._emit(
                                "agent_thinking", thinking=event.snapshot
                            )

                        # 텍스트 청크 (delta)
                        elif event_name == "TextEvent":
                            response_text = event.snapshot
                            await self._emit(
                                "agent_delta", text=event.text
                            )

                    response = await stream.get_final_message()

            except anthropic.RateLimitError:
                await self._emit(
                    "agent_state",
                    state="error",
                    error="rate_limit",
                    message="API 속도 제한. 잠시 후 다시 시도해주세요.",
                )
                await asyncio.sleep(30)
                continue
            except anthropic.APIError as e:
                import logging
                logger = logging.getLogger("runner")
                logger.error(f"API Error [{e.status_code}]: {e.message}")
                logger.error(f"  Body: {e.body}")
                logger.error(f"  Messages count: {len(self.messages)}")
                if self.messages:
                    import json
                    logger.error(f"  Last message: {json.dumps(self.messages[-1], ensure_ascii=False, default=str)[:500]}")
                await self._emit(
                    "agent_state", state="error", error="api_error", message=str(e)
                )
                return f"API Error: {e}"

            # ── 대화 종료 — 최종 응답 ──
            if response.stop_reason == "end_turn":
                self.messages.append(
                    {"role": "assistant", "content": response.content}
                )
                await self._emit("agent_message", message=response_text)
                await self._emit("agent_state", state="complete")
                await self._emit("agent_state", state="idle")
                return response_text

            # ── 도구 호출 ──
            if response.stop_reason == "tool_use":
                self.messages.append(
                    {"role": "assistant", "content": response.content}
                )

                if response_text:
                    await self._emit("agent_message", message=response_text)

                tool_results = []
                for block in response.content:
                    if block.type == "tool_use":
                        tool = self.tools.get(block.name)
                        if not tool:
                            tool_results.append(
                                {
                                    "type": "tool_result",
                                    "tool_use_id": block.id,
                                    "content": f"Error: Unknown tool '{block.name}'",
                                }
                            )
                            continue

                        await self._emit(
                            "agent_state",
                            state="working",
                            tool=block.name,
                            tool_input=_safe_preview(block.input),
                        )

                        try:
                            result = await tool.execute(block.input)
                        except Exception as e:
                            result = f"Tool error: {e}"

                        tool_results.append(
                            {
                                "type": "tool_result",
                                "tool_use_id": block.id,
                                "content": result,
                            }
                        )

                self.messages.append({"role": "user", "content": tool_results})
                await self._emit("agent_state", state="thinking")

        # 최대 반복 초과
        await self._emit(
            "agent_state",
            state="error",
            error="max_iterations",
            message="작업이 너무 길어졌어요.",
        )
        await self._emit("agent_state", state="idle")
        return "Max iterations reached."


def _safe_preview(obj: Any, max_len: int = 100) -> str:
    try:
        s = json.dumps(obj, ensure_ascii=False)
        return s[:max_len] + ("..." if len(s) > max_len else "")
    except Exception:
        return str(obj)[:max_len]


def _clean_content_block(block: dict) -> dict:
    """API가 거부하는 필드(citations, signature 등)를 제거한 깨끗한 블록 반환"""
    btype = block.get("type", "")
    if btype == "text":
        return {"type": "text", "text": block.get("text", "")}
    if btype == "thinking":
        clean = {"type": "thinking", "thinking": block.get("thinking", "")}
        if block.get("signature"):
            clean["signature"] = block["signature"]
        return clean
    if btype == "tool_use":
        return {
            "type": "tool_use",
            "id": block.get("id", ""),
            "name": block.get("name", ""),
            "input": block.get("input", {}),
        }
    if btype == "tool_result":
        return {
            "type": "tool_result",
            "tool_use_id": block.get("tool_use_id", ""),
            "content": block.get("content", ""),
        }
    return block


def _to_dict(block) -> dict:
    """Pydantic 객체 또는 dict를 dict로 변환"""
    if isinstance(block, dict):
        return block
    if hasattr(block, "model_dump"):
        return block.model_dump()
    if hasattr(block, "__dict__"):
        return {"type": block.type, **{k: v for k, v in block.__dict__.items() if not k.startswith("_")}}
    return block


def _serialize_messages(messages: list) -> list:
    """Pydantic 객체가 포함된 messages를 JSON 직렬화 가능한 형태로 변환"""
    result = []
    for msg in messages:
        role = msg.get("role", "")
        content = msg.get("content")

        if isinstance(content, str):
            result.append(msg)
        elif isinstance(content, list):
            blocks = [_clean_content_block(_to_dict(b)) for b in content]
            result.append({"role": role, "content": blocks})
        else:
            result.append(msg)
    return result
