"""ToolJournal — 세션 도구 호출 활동 로그 테스트.

LLM history 와 분리된 별도 ledger. provider 가 tool_use 라운드마다 append 하고
AI 가 read_tool_history 도구로 필요할 때만 조회한다.
"""

from __future__ import annotations

import pytest

from agent.tool_journal import ToolJournal


@pytest.mark.unit
def test_append_basic_entry():
    journal = ToolJournal()
    journal.append("write_file", {"path": "a.txt", "content": "hi"}, {"ok": True})

    entries = journal.recent(limit=10)
    assert len(entries) == 1
    e = entries[0]
    assert e["tool"] == "write_file"
    assert "a.txt" in e["input_summary"]
    assert "ok" in e["output_summary"]
    assert e["ts"]  # ISO-ish timestamp


@pytest.mark.unit
def test_recent_returns_most_recent_first():
    journal = ToolJournal()
    journal.append("first", {}, "1")
    journal.append("second", {}, "2")
    journal.append("third", {}, "3")

    entries = journal.recent(limit=10)
    names = [e["tool"] for e in entries]
    assert names == ["third", "second", "first"]


@pytest.mark.unit
def test_recent_caps_to_limit():
    journal = ToolJournal()
    for i in range(10):
        journal.append(f"t{i}", {}, str(i))

    entries = journal.recent(limit=3)
    assert len(entries) == 3
    assert [e["tool"] for e in entries] == ["t9", "t8", "t7"]


@pytest.mark.unit
def test_long_inputs_are_truncated():
    journal = ToolJournal()
    huge = "x" * 5000
    journal.append("edit_file", {"content": huge}, {"result": huge})

    e = journal.recent(limit=1)[0]
    assert len(e["input_summary"]) <= 320  # _MAX_PREVIEW_CHARS + ellipsis margin
    assert e["input_summary"].endswith("…")
    assert len(e["output_summary"]) <= 320
    assert e["output_summary"].endswith("…")


@pytest.mark.unit
def test_capacity_drops_oldest():
    journal = ToolJournal(capacity=3)
    journal.append("a", {}, "")
    journal.append("b", {}, "")
    journal.append("c", {}, "")
    journal.append("d", {}, "")  # should evict "a"

    entries = journal.recent(limit=10)
    names = [e["tool"] for e in entries]
    assert names == ["d", "c", "b"]
    assert len(journal) == 3


@pytest.mark.unit
def test_clear_empties_journal():
    journal = ToolJournal()
    journal.append("x", {}, "")
    assert len(journal) == 1

    journal.clear()
    assert len(journal) == 0
    assert journal.recent(limit=10) == []


@pytest.mark.unit
def test_recent_with_zero_or_negative_limit_returns_empty():
    journal = ToolJournal()
    journal.append("x", {}, "")
    assert journal.recent(limit=0) == []
    assert journal.recent(limit=-5) == []


@pytest.mark.unit
def test_string_input_preserved_without_json_encoding():
    journal = ToolJournal()
    journal.append("bash", "ls -la /tmp", "exit_code=0")

    e = journal.recent(limit=1)[0]
    # str 입력은 JSON 인코딩 없이 그대로 표시 (가독성)
    assert e["input_summary"] == "ls -la /tmp"
    assert e["output_summary"] == "exit_code=0"
