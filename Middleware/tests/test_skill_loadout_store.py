"""SkillLoadoutStore 단위 테스트."""

from __future__ import annotations

from pathlib import Path

import pytest

from skill_loadout_store import SkillLoadoutStore


@pytest.mark.unit
def test_empty_after_construction():
    store = SkillLoadoutStore()
    assert store.is_empty
    assert store.active_ids == []


@pytest.mark.unit
def test_update_with_skills_makes_tmpdir_with_body_files():
    store = SkillLoadoutStore()
    store.update({
        "agentId": "agent-1",
        "skills": [
            {"id": "code-reviewer", "name": "Code Reviewer", "description": "x", "body": "# Code review steps\n..."},
            {"id": "doc-writer", "name": "Doc Writer", "description": "y", "body": ""},
        ],
    })

    try:
        assert not store.is_empty
        assert store.active_ids == ["code-reviewer", "doc-writer"]
        env = store.build_env()
        assert env["OPENDESK_ACTIVE_SKILLS"] == "code-reviewer,doc-writer"
        assert env["OPENDESK_AGENT_ID"] == "agent-1"
        skills_root = Path(env["OPENDESK_SKILLS_ROOT"])
        # body 가 있는 항목은 디스크에 머티리얼라이즈됨
        assert (skills_root / "code-reviewer" / "SKILL.md").exists()
        # body 가 비어있는 항목은 디스크에 만들지 않음 (사용자 홈 SKILL.md fallback)
        assert not (skills_root / "doc-writer" / "SKILL.md").exists()
    finally:
        store.clear()


@pytest.mark.unit
def test_clear_removes_tmpdir():
    store = SkillLoadoutStore()
    store.update({"agentId": "a", "skills": [{"id": "x", "body": "body"}]})
    root = Path(store.build_env()["OPENDESK_SKILLS_ROOT"])
    assert root.exists()

    store.clear()
    assert store.is_empty
    assert not root.exists()


@pytest.mark.unit
def test_update_with_empty_or_none_clears():
    store = SkillLoadoutStore()
    store.update({"agentId": "a", "skills": [{"id": "x", "body": "b"}]})
    store.update(None)
    assert store.is_empty
    store.update({"agentId": "a", "skills": [{"id": "y", "body": "b"}]})
    store.update({})
    assert store.is_empty


@pytest.mark.unit
def test_empty_loadout_env_uses_default_home():
    store = SkillLoadoutStore()
    env = store.build_env()
    assert env["OPENDESK_ACTIVE_SKILLS"] == ""
    # 기본 경로는 ~/.opendesk/skills
    assert env["OPENDESK_SKILLS_ROOT"].endswith("/.opendesk/skills") or env["OPENDESK_SKILLS_ROOT"].endswith("\\.opendesk\\skills")
