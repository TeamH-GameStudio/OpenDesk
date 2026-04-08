"""
에이전트 메모리 — 세션을 넘어 유지되는 영구 기억.

각 에이전트가 사용자에 대해 기억할 것들:
- 사용자 선호/스타일
- 반복되는 작업 패턴
- 중요 결정/맥락
- 프로젝트 정보

저장 위치: ~/.opendesk/memory/{agent_id}/
파일 구조:
  ~/.opendesk/memory/
    researcher/
      memories.json    <- 메모리 목록
    writer/
    analyst/
    _shared/           <- 모든 에이전트가 공유하는 메모리
      memories.json
"""

import json
import os
import time
import uuid
import logging
from typing import Optional

logger = logging.getLogger("memory")


class MemoryStore:
    def __init__(self, base_dir: str = "~/.opendesk/memory"):
        self._base_dir = os.path.expanduser(base_dir)

    def _memory_path(self, agent_id: str) -> str:
        d = os.path.join(self._base_dir, agent_id)
        os.makedirs(d, exist_ok=True)
        return os.path.join(d, "memories.json")

    def _load(self, agent_id: str) -> list:
        path = self._memory_path(agent_id)
        if not os.path.exists(path):
            return []
        try:
            with open(path, "r", encoding="utf-8") as f:
                return json.load(f)
        except (json.JSONDecodeError, IOError):
            return []

    def _save(self, agent_id: str, memories: list):
        path = self._memory_path(agent_id)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(memories, f, ensure_ascii=False, indent=2)

    def add(self, agent_id: str, content: str, category: str = "general") -> dict:
        """메모리 추가"""
        memories = self._load(agent_id)
        entry = {
            "id": f"m_{uuid.uuid4().hex[:8]}",
            "content": content,
            "category": category,
            "created_at": time.time(),
            "agent_id": agent_id,
        }
        memories.append(entry)
        self._save(agent_id, memories)
        logger.info(f"Memory added [{agent_id}]: {content[:50]}...")
        return entry

    def list_memories(self, agent_id: str, include_shared: bool = True) -> list:
        """에이전트의 메모리 목록 (공유 메모리 포함)"""
        memories = self._load(agent_id)
        if include_shared:
            shared = self._load("_shared")
            memories = shared + memories
        return sorted(memories, key=lambda x: x.get("created_at", 0), reverse=True)

    def search(self, agent_id: str, query: str) -> list:
        """키워드로 메모리 검색"""
        all_memories = self.list_memories(agent_id)
        query_lower = query.lower()
        return [
            m for m in all_memories
            if query_lower in m["content"].lower()
            or query_lower in m.get("category", "").lower()
        ]

    def delete(self, agent_id: str, memory_id: str) -> bool:
        """메모리 삭제"""
        memories = self._load(agent_id)
        original_len = len(memories)
        memories = [m for m in memories if m["id"] != memory_id]
        if len(memories) < original_len:
            self._save(agent_id, memories)
            return True
        # 공유 메모리에서도 시도
        shared = self._load("_shared")
        original_len = len(shared)
        shared = [m for m in shared if m["id"] != memory_id]
        if len(shared) < original_len:
            self._save("_shared", shared)
            return True
        return False

    def clear(self, agent_id: str):
        """에이전트의 메모리 전체 삭제"""
        self._save(agent_id, [])

    def build_memory_context(self, agent_id: str, max_entries: int = 20) -> str:
        """시스템 프롬프트에 포함할 메모리 컨텍스트 문자열 생성"""
        memories = self.list_memories(agent_id)[:max_entries]
        if not memories:
            return ""

        lines = ["[Agent Memory - remembered from previous sessions]"]
        for m in memories:
            category = m.get("category", "general")
            content = m["content"]
            source = "shared" if m.get("agent_id") == "_shared" else "personal"
            lines.append(f"- [{category}] {content} ({source})")

        return "\n".join(lines)


# ── 메모리 도구 (에이전트가 직접 사용) ──

from tools.base import BaseTool


class MemoryWriteTool(BaseTool):
    """에이전트가 중요 정보를 기억하는 도구"""

    def __init__(self, memory_store: MemoryStore, agent_id: str):
        self._store = memory_store
        self._agent_id = agent_id

    @property
    def name(self):
        return "save_memory"

    @property
    def description(self):
        return (
            "Save important information to remember across conversations. "
            "Use this to remember user preferences, project details, key decisions, "
            "or anything that should persist beyond this session."
        )

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "content": {
                    "type": "string",
                    "description": "What to remember",
                },
                "category": {
                    "type": "string",
                    "description": "Category: user_preference, project, decision, fact, task",
                    "enum": ["user_preference", "project", "decision", "fact", "task"],
                },
                "shared": {
                    "type": "boolean",
                    "description": "If true, all agents can see this memory. Default false.",
                    "default": False,
                },
            },
            "required": ["content", "category"],
        }

    async def execute(self, args: dict) -> str:
        content = args["content"]
        category = args.get("category", "general")
        shared = args.get("shared", False)

        target = "_shared" if shared else self._agent_id
        entry = self._store.add(target, content, category)
        scope = "all agents" if shared else self._agent_id
        return f"Remembered ({scope}): {content[:80]}"


class MemoryReadTool(BaseTool):
    """에이전트가 저장된 기억을 조회하는 도구"""

    def __init__(self, memory_store: MemoryStore, agent_id: str):
        self._store = memory_store
        self._agent_id = agent_id

    @property
    def name(self):
        return "recall_memory"

    @property
    def description(self):
        return (
            "Search and recall saved memories. "
            "Use this to look up previously saved user preferences, "
            "project details, or important context."
        )

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search keyword (empty to list all)",
                    "default": "",
                }
            },
            "required": [],
        }

    async def execute(self, args: dict) -> str:
        query = args.get("query", "").strip()

        if query:
            memories = self._store.search(self._agent_id, query)
        else:
            memories = self._store.list_memories(self._agent_id)

        if not memories:
            return "No memories found."

        lines = []
        for m in memories[:20]:
            cat = m.get("category", "general")
            src = "shared" if m.get("agent_id") == "_shared" else "personal"
            lines.append(f"[{cat}|{src}] {m['content']} (id: {m['id']})")

        return "\n".join(lines)
