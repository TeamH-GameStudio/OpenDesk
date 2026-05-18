"""
SkillBodyCache — opendesk_skills_mcp.py 의 read_skill_body 디스크 I/O 감소.

(skill_id, mtime_ns) 키로 작은 LRU. SKILL.md 가 갱신되면 mtime_ns 가 변해 캐시 미스
→ 다시 로드. 동일 파일이 짧은 시간 안에 여러 번 요청되면 메모리에서 즉시 반환.

bound:
- maxsize: 32 (보통 활성 스킬 수보다 큼 → 효과적)
- 항목 크기: SKILL.md 본문 길이 — 보통 수 KB
"""

from __future__ import annotations

import os
import threading
from collections import OrderedDict
from pathlib import Path
from typing import Optional


class SkillBodyCache:
    """thread-safe LRU. opendesk_skills_mcp 의 sync read 와 함께 동작.

    동시 read 가 가능하도록 단순 mutex 사용 — 본문 자체는 immutable str 이라 OK.
    """

    def __init__(self, maxsize: int = 32):
        self._maxsize = max(1, int(maxsize))
        self._store: "OrderedDict[tuple[str, int], str]" = OrderedDict()
        self._lock = threading.Lock()

    def get_or_load(self, skill_id: str, path: Path) -> Optional[str]:
        """캐시 hit 면 반환. miss 면 디스크에서 읽고 (skill_id, mtime_ns) 키로 저장.

        반환값:
            본문 str — 정상 케이스
            None — 파일이 존재하지 않거나 읽기 실패
        """
        try:
            stat = path.stat()
        except OSError:
            return None
        mtime_ns = stat.st_mtime_ns
        key = (skill_id, mtime_ns)

        with self._lock:
            cached = self._store.get(key)
            if cached is not None:
                self._store.move_to_end(key)
                return cached

        # cache miss — 락 밖에서 디스크 I/O.
        try:
            body = path.read_text(encoding="utf-8")
        except OSError:
            return None

        with self._lock:
            # 동일 키에 대해 race 가 있을 수 있지만 결과가 같으므로 무해.
            self._store[key] = body
            self._store.move_to_end(key)
            self._invalidate_old_versions(skill_id, mtime_ns)
            self._evict_if_needed()
        return body

    def _invalidate_old_versions(self, skill_id: str, current_mtime: int) -> None:
        """동일 skill_id 의 다른 mtime 항목 제거 — 오래된 본문이 메모리에 머무는 것 방지."""
        stale_keys = [
            k for k in self._store.keys()
            if k[0] == skill_id and k[1] != current_mtime
        ]
        for k in stale_keys:
            self._store.pop(k, None)

    def _evict_if_needed(self) -> None:
        while len(self._store) > self._maxsize:
            self._store.popitem(last=False)

    def clear(self) -> None:
        """테스트용 / 디버깅용. 모든 캐시 비우기."""
        with self._lock:
            self._store.clear()

    def __len__(self) -> int:
        with self._lock:
            return len(self._store)


# 모듈 레벨 싱글톤 — opendesk_skills_mcp.py 가 이걸 사용.
_CACHE = SkillBodyCache(maxsize=int(os.environ.get("OPENDESK_SKILL_CACHE_SIZE", "32")))


def read_skill_body_cached(skill_id: str, path: Path) -> Optional[str]:
    """opendesk_skills_mcp 에서 호출하는 entry point. mtime 기반 자동 무효화."""
    return _CACHE.get_or_load(skill_id, path)


def clear_cache_for_tests() -> None:
    """테스트 후크 — 모듈 레벨 싱글톤 초기화."""
    _CACHE.clear()
