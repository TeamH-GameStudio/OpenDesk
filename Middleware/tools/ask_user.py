"""사용자에게 구조화된 질문을 던지는 인터랙티브 도구.

블로킹 라운드트립: 미들웨어가 Unity 에 'tool_user_ask' broadcast → 사용자가 채팅 카드에
응답 → Unity 가 'tool_user_response' op 송신 → AskUserPort 가 future 해소 → 도구 결과 회신.
"""

from __future__ import annotations

import asyncio
import json
import logging
import uuid
from typing import Protocol

from .base import BaseTool

logger = logging.getLogger("ask_user")


class AskUserPort(Protocol):
    async def ask(self, agent_id: str, tool_use_id: str, payload: dict) -> dict: ...


class AskUserTool(BaseTool):
    def __init__(
        self,
        port: AskUserPort,
        agent_id: str,
        timeout_seconds: float = 300.0,
    ):
        self._port = port
        self._agent_id = agent_id
        self._timeout = timeout_seconds

    @property
    def name(self) -> str:
        return "ask_user"

    @property
    def description(self) -> str:
        return (
            "Ask the user a clarifying question and wait for their answer. "
            "Use when you genuinely cannot proceed without their input — choosing between approaches, "
            "confirming intent, or gathering required parameters. "
            "Each option should be a distinct, mutually exclusive choice (unless multi_select=true). "
            "The user can also enter free-text. Avoid for greetings or trivial confirmations."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "question": {
                    "type": "string",
                    "description": "The complete question. Be specific.",
                },
                "header": {
                    "type": "string",
                    "description": "Very short label (max 12 chars). Shown as a chip.",
                },
                "multi_select": {
                    "type": "boolean",
                    "description": "Allow multiple selections (default false)",
                    "default": False,
                },
                "options": {
                    "type": "array",
                    "description": "2-4 options. Each {label, description?}.",
                    "items": {
                        "type": "object",
                        "properties": {
                            "label": {"type": "string"},
                            "description": {"type": "string"},
                        },
                        "required": ["label"],
                    },
                },
            },
            "required": ["question"],
        }

    async def execute(self, args: dict) -> str:
        question = (args.get("question") or "").strip()
        if not question:
            return "Error: question is required."

        # 자체 발급 — Anthropic block.id 와 분리해서 dedupe 충돌 회피.
        tool_use_id = f"ask_{uuid.uuid4().hex[:16]}"

        payload = {
            "question": question,
            "header": (args.get("header") or "")[:12],
            "multi_select": bool(args.get("multi_select", False)),
            "options": args.get("options") or [],
        }

        try:
            answer = await asyncio.wait_for(
                self._port.ask(self._agent_id, tool_use_id, payload),
                timeout=self._timeout,
            )
        except asyncio.TimeoutError:
            return f"Error: ask_user timed out after {int(self._timeout)}s with no response."
        except asyncio.CancelledError:
            raise
        except Exception as e:
            return f"Error: ask_user failed: {e}"

        return json.dumps(answer, ensure_ascii=False)
