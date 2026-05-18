"""read_tool_history 도구 — 활동 로그 조회 단위 테스트."""

from __future__ import annotations

import json

import pytest

from agent.tool_journal import ToolJournal
from tools.read_tool_history import ReadToolHistoryTool


@pytest.mark.unit
@pytest.mark.asyncio
async def test_execute_returns_recent_entries():
    journal = ToolJournal()
    journal.append("write_file", {"path": "a.txt"}, {"ok": True})
    journal.append("edit_file", {"path": "a.txt"}, {"ok": True})

    tool = ReadToolHistoryTool(journal)
    result = await tool.execute({"limit": 10})

    payload = json.loads(result)
    assert payload["count"] == 2
    assert [e["tool"] for e in payload["entries"]] == ["edit_file", "write_file"]


@pytest.mark.unit
@pytest.mark.asyncio
async def test_execute_default_limit_is_20():
    journal = ToolJournal()
    for i in range(30):
        journal.append(f"t{i}", {}, "")

    tool = ReadToolHistoryTool(journal)
    result = await tool.execute({})

    payload = json.loads(result)
    assert payload["count"] == 20
    assert payload["entries"][0]["tool"] == "t29"


@pytest.mark.unit
@pytest.mark.asyncio
async def test_execute_clamps_limit_to_max_100():
    journal = ToolJournal(capacity=500)
    for i in range(200):
        journal.append(f"t{i}", {}, "")

    tool = ReadToolHistoryTool(journal)
    result = await tool.execute({"limit": 9999})

    payload = json.loads(result)
    assert payload["count"] == 100


@pytest.mark.unit
@pytest.mark.asyncio
async def test_execute_empty_journal_returns_zero_count():
    journal = ToolJournal()
    tool = ReadToolHistoryTool(journal)

    result = await tool.execute({"limit": 10})

    payload = json.loads(result)
    assert payload == {"count": 0, "entries": []}


@pytest.mark.unit
def test_anthropic_schema_shape():
    tool = ReadToolHistoryTool(ToolJournal())
    schema = tool.to_anthropic_schema()

    assert schema["name"] == "read_tool_history"
    assert "input_schema" in schema
    assert "limit" in schema["input_schema"]["properties"]
