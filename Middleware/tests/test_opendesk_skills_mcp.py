"""OpenDesk Skills 내장 MCP 서버의 안전 함수 단위 테스트."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

import pytest

from opendesk_skills_mcp import _safe_read_skill_body


@pytest.fixture()
def skills_root(tmp_path: Path) -> Path:
    (tmp_path / "code-reviewer").mkdir()
    (tmp_path / "code-reviewer" / "SKILL.md").write_text("# Code review steps", encoding="utf-8")
    return tmp_path


@pytest.mark.unit
def test_reads_skill_body_when_active_and_present(skills_root, monkeypatch):
    monkeypatch.setenv("OPENDESK_SKILLS_ROOT", str(skills_root))
    monkeypatch.setenv("OPENDESK_ACTIVE_SKILLS", "code-reviewer")
    body = _safe_read_skill_body("code-reviewer")
    assert body == "# Code review steps"


@pytest.mark.unit
def test_rejects_skill_not_in_whitelist(skills_root, monkeypatch):
    monkeypatch.setenv("OPENDESK_SKILLS_ROOT", str(skills_root))
    monkeypatch.setenv("OPENDESK_ACTIVE_SKILLS", "doc-writer")
    body = _safe_read_skill_body("code-reviewer")
    assert "not in the active loadout" in body


@pytest.mark.unit
def test_blocks_path_traversal(skills_root, monkeypatch):
    monkeypatch.setenv("OPENDESK_SKILLS_ROOT", str(skills_root))
    monkeypatch.setenv("OPENDESK_ACTIVE_SKILLS", "../../etc")
    body = _safe_read_skill_body("../../etc")
    assert body.startswith("[error]")


@pytest.mark.unit
def test_returns_not_found_when_skill_dir_missing(skills_root, monkeypatch):
    monkeypatch.setenv("OPENDESK_SKILLS_ROOT", str(skills_root))
    monkeypatch.setenv("OPENDESK_ACTIVE_SKILLS", "ghost,code-reviewer")
    body = _safe_read_skill_body("ghost")
    assert body.startswith("[not-found]")


@pytest.mark.unit
def test_empty_skill_id_is_error(monkeypatch):
    body = _safe_read_skill_body("")
    assert "skill_id is required" in body
