"""서브에이전트 위임 도구 — Claude API 단발 호출로 격리된 작업 실행 (v1).

부모 에이전트가 sub_agent_spawned/completed/failed 이벤트를 broadcast 해서
Unity SubAgentService 위젯에 표시된다. v1 은 tool_use 루프 없이 한 번의 응답만 받는다.
"""

from __future__ import annotations

import logging
import time
import uuid
from typing import Awaitable, Callable, Optional

import anthropic

from .base import BaseTool

logger = logging.getLogger("spawn_agent")


class SpawnAgentTool(BaseTool):
    def __init__(
        self,
        api_key: str,
        parent_agent_id: str,
        model: str = "claude-sonnet-4-6",
        on_event: Optional[Callable[[dict], Awaitable[None]]] = None,
        max_tokens: int = 8000,
    ):
        self._api_key = api_key
        self._parent_agent_id = parent_agent_id
        self._model = model
        self._on_event = on_event
        self._max_tokens = max_tokens
        self._client = anthropic.AsyncAnthropic(api_key=api_key)

    @property
    def name(self) -> str:
        return "spawn_agent"

    @property
    def description(self) -> str:
        return (
            "Delegate a focused task to a subagent. The subagent runs in isolation, "
            "returns a single text result, and does NOT have tool access. "
            "Use for: parallel research questions, summarization, focused analysis, second opinions."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "prompt": {
                    "type": "string",
                    "description": "The task or question for the subagent",
                },
                "description": {
                    "type": "string",
                    "description": "Short label shown in the UI (e.g. 'Summarize PDF')",
                },
                "subagent_type": {
                    "type": "string",
                    "description": "Optional role tag (e.g. 'researcher', 'critic'). Influences system prompt.",
                },
            },
            "required": ["prompt", "description"],
        }

    async def _emit(self, event: dict) -> None:
        if self._on_event:
            try:
                await self._on_event(event)
            except Exception as e:
                logger.warning(f"spawn_agent emit failed: {e}")

    async def execute(self, args: dict) -> str:
        prompt = (args.get("prompt") or "").strip()
        description = (args.get("description") or "").strip() or "subagent task"
        subagent_type = (args.get("subagent_type") or "").strip()

        if not prompt:
            return "Error: prompt is required."

        sub_agent_id = f"sub_{uuid.uuid4().hex[:8]}"
        spawned_at = time.time()

        await self._emit({
            "type": "sub_agent_spawned",
            "agent_id": self._parent_agent_id,
            "sub_agent_id": sub_agent_id,
            "task_name": description,
            "subagent_type": subagent_type,
            "timestamp": spawned_at,
        })

        system = (
            f"You are a focused subagent invoked by a parent agent. "
            f"Role: {subagent_type or 'general assistant'}. "
            f"Task summary: {description}. "
            "Return a concise, self-contained answer. "
            "You do NOT have access to tools — work from your own knowledge."
        )

        try:
            response = await self._client.messages.create(
                model=self._model,
                max_tokens=self._max_tokens,
                system=system,
                messages=[{"role": "user", "content": prompt}],
            )
            texts = []
            for block in response.content:
                if getattr(block, "type", None) == "text":
                    texts.append(block.text)
            result = "\n".join(texts).strip() or "(subagent returned no text)"
        except anthropic.APIError as e:
            await self._emit({
                "type": "sub_agent_failed",
                "agent_id": self._parent_agent_id,
                "sub_agent_id": sub_agent_id,
                "error": str(e),
                "timestamp": time.time(),
            })
            return f"Error: subagent API call failed: {e}"
        except Exception as e:
            await self._emit({
                "type": "sub_agent_failed",
                "agent_id": self._parent_agent_id,
                "sub_agent_id": sub_agent_id,
                "error": str(e),
                "timestamp": time.time(),
            })
            return f"Error: subagent crashed: {e}"

        await self._emit({
            "type": "sub_agent_completed",
            "agent_id": self._parent_agent_id,
            "sub_agent_id": sub_agent_id,
            "task_name": description,
            "timestamp": time.time(),
        })
        return result
