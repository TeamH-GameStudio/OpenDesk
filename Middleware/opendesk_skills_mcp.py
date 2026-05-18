"""
OpenDesk Skills — stdio MCP 서버.

CLI/API 양쪽 provider 가 동일하게 활용하는 내장 MCP 서버.
공식 `mcp` 패키지로 stdio 인터페이스를 노출하고, `read_skill_body(skill_id)` 도구 하나를 제공한다.

환경변수:
  OPENDESK_SKILLS_ROOT      — SKILL.md 들이 위치한 디렉토리. 기본 ~/.opendesk/skills
  OPENDESK_ACTIVE_SKILLS    — 콤마 구분 id 목록. 화이트리스트 (임의 파일 읽기 차단).
  OPENDESK_AGENT_ID         — (선택) 로그용

진입점:
  python -m opendesk_skills_mcp   또는
  python opendesk_skills_mcp.py
"""

from __future__ import annotations

import logging
import os
import sys
from pathlib import Path

logger = logging.getLogger("opendesk_skills_mcp")


def _load_skills_root() -> Path:
    raw = os.environ.get("OPENDESK_SKILLS_ROOT", "").strip()
    if raw:
        return Path(raw).expanduser()
    return Path.home() / ".opendesk" / "skills"


def _load_active_skill_ids() -> set[str]:
    raw = os.environ.get("OPENDESK_ACTIVE_SKILLS", "")
    return {s.strip() for s in raw.split(",") if s.strip()}


def _safe_read_skill_body(skill_id: str) -> str:
    if not skill_id or not isinstance(skill_id, str):
        return "[error] skill_id is required"

    active = _load_active_skill_ids()
    if active and skill_id not in active:
        return f"[error] skill '{skill_id}' is not in the active loadout"

    # 경로 트래버설 방지 — 단순 id 만 허용.
    if "/" in skill_id or "\\" in skill_id or skill_id.startswith("."):
        return f"[error] invalid skill_id: {skill_id!r}"

    skills_root = _load_skills_root()
    candidate = skills_root / skill_id / "SKILL.md"
    if not candidate.exists():
        return f"[not-found] {candidate}"

    # 안전: 해석된 절대경로가 skills_root 아래여야 함
    try:
        resolved = candidate.resolve()
        if not str(resolved).startswith(str(skills_root.resolve())):
            return "[error] path traversal blocked"
    except OSError as e:
        return f"[error] path resolution failed: {e}"

    # LRU 캐시 — (skill_id, mtime_ns) 키. SKILL.md 가 변경되면 자동 무효화.
    try:
        from skill_body_cache import read_skill_body_cached
    except ImportError:
        read_skill_body_cached = None

    if read_skill_body_cached is not None:
        body = read_skill_body_cached(skill_id, candidate)
        if body is None:
            return f"[error] read failed: {candidate}"
        return body

    # 폴백 — 캐시 모듈이 없으면 직접 읽기.
    try:
        with open(candidate, "r", encoding="utf-8") as f:
            return f.read()
    except Exception as e:  # noqa: BLE001
        return f"[error] read failed: {e}"


async def serve() -> None:
    try:
        from mcp.server import Server
        from mcp.server.stdio import stdio_server
        from mcp.types import TextContent, Tool
    except ImportError as e:
        sys.stderr.write(
            f"[opendesk_skills_mcp] mcp 패키지가 필요합니다 (`pip install mcp`): {e}\n"
        )
        raise

    server = Server("opendesk-skills")

    @server.list_tools()
    async def _list_tools() -> list[Tool]:
        return [
            Tool(
                name="read_skill_body",
                description=(
                    "장착된 OpenDesk 스킬의 상세 행동 지침(SKILL.md 본문)을 가져옵니다. "
                    "에이전트가 작업 수행 전, 활성화된 스킬 중 적용 가능한 것을 골라 본문을 읽고 따라야 합니다."
                ),
                inputSchema={
                    "type": "object",
                    "properties": {
                        "skill_id": {
                            "type": "string",
                            "description": "available-skills 인덱스에 표시된 skill id",
                        },
                    },
                    "required": ["skill_id"],
                },
            )
        ]

    @server.call_tool()
    async def _call_tool(name: str, arguments: dict) -> list[TextContent]:
        if name != "read_skill_body":
            return [TextContent(type="text", text=f"[error] unknown tool: {name}")]
        body = _safe_read_skill_body((arguments or {}).get("skill_id", ""))
        return [TextContent(type="text", text=body)]

    async with stdio_server() as (read_stream, write_stream):
        await server.run(read_stream, write_stream, server.create_initialization_options())


def main() -> None:
    import asyncio

    logging.basicConfig(level=logging.INFO, stream=sys.stderr)
    try:
        asyncio.run(serve())
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
