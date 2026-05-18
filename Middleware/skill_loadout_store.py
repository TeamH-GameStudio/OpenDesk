"""
SkillLoadoutStore — 세션별 활성 스킬 본문을 임시 디스크에 머티리얼라이즈.

Unity 가 `set_skill_loadout` 메시지로 보낸 페이로드를 받아:
  - 각 스킬의 body 가 채워져 있으면 임시 디렉토리에 `{id}/SKILL.md` 로 기록한다 (캐시).
  - body 가 비어있으면 사용자 홈의 `~/.opendesk/skills/{id}/SKILL.md` 를 그대로 활용한다.

`materialize()` 가 반환하는 (skills_root, active_ids) 를 stdio MCP 서버에 환경변수로 전달해
서버는 화이트리스트 검증 후 SKILL.md 를 읽어준다.
"""

from __future__ import annotations

import logging
import os
import tempfile
from pathlib import Path
from typing import Any, Optional

logger = logging.getLogger("skill_loadout_store")


class SkillLoadoutStore:
    """세션 1개당 1 인스턴스. 디렉토리 생명주기를 보장한다."""

    def __init__(self) -> None:
        self._tmpdir: Optional[Path] = None
        self._active_ids: list[str] = []
        self._agent_id: str = ""

    def update(self, payload: Optional[dict[str, Any]]) -> None:
        """페이로드를 새로 받으면 디스크 캐시를 갱신한다."""
        self.clear()

        if not payload or not isinstance(payload, dict):
            return

        skills = payload.get("skills") or []
        if not skills:
            return

        self._agent_id = str(payload.get("agentId") or "")
        self._tmpdir = Path(tempfile.mkdtemp(prefix="opendesk-skills-"))
        for entry in skills:
            if not isinstance(entry, dict):
                continue
            sid = (entry.get("id") or "").strip()
            if not sid:
                continue
            self._active_ids.append(sid)

            # body 가 비어있어도 디스크의 ~/.opendesk/skills/{id}/SKILL.md 로 fallback 되므로,
            # 비어 있지 않을 때만 임시 디렉토리에 기록한다.
            body = entry.get("body") or ""
            if not body:
                continue

            skill_dir = self._tmpdir / sid
            skill_dir.mkdir(parents=True, exist_ok=True)
            (skill_dir / "SKILL.md").write_text(body, encoding="utf-8")

        logger.info("Skill loadout materialized: %d skill(s) at %s", len(self._active_ids), self._tmpdir)

    def clear(self) -> None:
        if self._tmpdir is not None and self._tmpdir.exists():
            try:
                for child in self._tmpdir.rglob("*"):
                    if child.is_file():
                        child.unlink(missing_ok=True)
                for child in sorted(self._tmpdir.rglob("*"), key=lambda p: -len(str(p))):
                    if child.is_dir():
                        child.rmdir()
                self._tmpdir.rmdir()
            except Exception as e:  # noqa: BLE001
                logger.warning("Failed to clean skill loadout tmpdir %s: %s", self._tmpdir, e)
        self._tmpdir = None
        self._active_ids = []
        self._agent_id = ""

    @property
    def is_empty(self) -> bool:
        return not self._active_ids

    @property
    def active_ids(self) -> list[str]:
        return list(self._active_ids)

    @property
    def agent_id(self) -> str:
        return self._agent_id

    def build_env(self) -> dict[str, str]:
        """stdio MCP 서버에 전달할 환경변수."""
        env: dict[str, str] = {
            "OPENDESK_ACTIVE_SKILLS": ",".join(self._active_ids),
        }
        if self._agent_id:
            env["OPENDESK_AGENT_ID"] = self._agent_id
        # 임시 캐시 디렉토리가 있으면 SKILL.md 가 거기에 있다 → 사용
        # 없으면 사용자 홈 (~/.opendesk/skills) 의 기본 경로를 그대로 활용.
        if self._tmpdir is not None:
            env["OPENDESK_SKILLS_ROOT"] = str(self._tmpdir)
        else:
            env["OPENDESK_SKILLS_ROOT"] = str(Path.home() / ".opendesk" / "skills")
        return env
