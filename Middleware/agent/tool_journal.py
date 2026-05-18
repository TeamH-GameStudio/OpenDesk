"""세션 도구 호출 활동 로그.

LLM 의 자연어 응답만 session.history 에 들어가므로 AI 가 "방금 어떤 도구를
호출했지?" 같은 질문에 history 만으로는 정확히 답하지 못한다 (자기 요약문만
보인다). ToolJournal 은 provider 가 tool_use 라운드마다 한 줄씩 append 하는
별도 ledger 로:

  - history 에는 절대 들어가지 않아 토큰 비용 0
  - read_tool_history 도구로 AI 가 명시적으로 조회할 때만 결과로 전달
  - session.clear() 시 함께 비워짐 (history 와 라이프타임 동일)
  - WebSocket connection 라이프타임 — 재연결 시 새 ChatSession 과 함께 비어 시작
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass
from typing import Any


# input/output 요약 상한 문자수. 너무 길면 read_tool_history 한 번 호출이 입력 토큰 폭증을 부른다.
_MAX_PREVIEW_CHARS = 300


def _summarize(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        text = value
    else:
        try:
            text = json.dumps(value, ensure_ascii=False, default=str)
        except (TypeError, ValueError):
            text = str(value)
    if len(text) > _MAX_PREVIEW_CHARS:
        return text[:_MAX_PREVIEW_CHARS] + "…"
    return text


@dataclass(frozen=True)
class ToolJournalEntry:
    ts: str
    tool: str
    input_summary: str
    output_summary: str

    def to_dict(self) -> dict[str, str]:
        return asdict(self)


class ToolJournal:
    """append-only in-memory list. provider 가 round 단위로 append."""

    def __init__(self, capacity: int = 200):
        self._entries: list[ToolJournalEntry] = []
        self._capacity = capacity

    def append(self, tool: str, tool_input: Any, tool_output: Any) -> None:
        entry = ToolJournalEntry(
            ts=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            tool=tool or "",
            input_summary=_summarize(tool_input),
            output_summary=_summarize(tool_output),
        )
        self._entries.append(entry)
        if len(self._entries) > self._capacity:
            # FIFO 절단 — 오래된 호출부터 버린다.
            del self._entries[: len(self._entries) - self._capacity]

    def recent(self, limit: int = 20) -> list[dict[str, str]]:
        """가장 최근 호출부터 limit 개 dict 반환. 비파괴 조회."""
        if limit <= 0:
            return []
        slice_ = self._entries[-limit:]
        return [e.to_dict() for e in reversed(slice_)]

    def clear(self) -> None:
        self._entries.clear()

    def __len__(self) -> int:
        return len(self._entries)
