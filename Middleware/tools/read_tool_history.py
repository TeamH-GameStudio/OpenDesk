"""read_tool_history — 자신의 도구 호출 활동 로그를 조회.

AI 가 "방금 어떤 도구를 호출했지?" 또는 "edit_file 진짜 실행했어?" 같은 자기
검증 질문을 받으면 이 도구로 정확한 기록을 조회한다. session.history 에는 자연어
응답만 들어가므로 실제 호출 흔적은 ToolJournal 에서만 확인 가능.
"""

from __future__ import annotations

import json
from typing import TYPE_CHECKING

from .base import BaseTool

if TYPE_CHECKING:
    from agent.tool_journal import ToolJournal


_DEFAULT_LIMIT = 20
_MAX_LIMIT = 100


class ReadToolHistoryTool(BaseTool):
    def __init__(self, journal: "ToolJournal"):
        self._journal = journal

    @property
    def name(self) -> str:
        return "read_tool_history"

    @property
    def description(self) -> str:
        return (
            "Return your own recent tool invocations in this conversation, "
            "most-recent first. Use when the user asks what tools you have called, "
            "to verify a past action, or to summarize tool activity. Entries include "
            "ts, tool name, truncated input, truncated output."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "limit": {
                    "type": "integer",
                    "description": f"Max entries (default {_DEFAULT_LIMIT}, capped at {_MAX_LIMIT}).",
                    "default": _DEFAULT_LIMIT,
                    "minimum": 1,
                    "maximum": _MAX_LIMIT,
                },
            },
            "required": [],
        }

    async def execute(self, args: dict) -> str:
        try:
            limit = int(args.get("limit", _DEFAULT_LIMIT))
        except (TypeError, ValueError):
            limit = _DEFAULT_LIMIT
        if limit < 1:
            limit = 1
        if limit > _MAX_LIMIT:
            limit = _MAX_LIMIT
        entries = self._journal.recent(limit=limit)
        return json.dumps(
            {"count": len(entries), "entries": entries},
            ensure_ascii=False,
        )
