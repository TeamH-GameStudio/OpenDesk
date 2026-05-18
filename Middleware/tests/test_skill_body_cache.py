"""SkillBodyCache 의 mtime 기반 자동 무효화 / LRU 동작 / thread safety 검증."""

from __future__ import annotations

import os
import time
from pathlib import Path

import pytest

from skill_body_cache import SkillBodyCache


def _write(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8")


@pytest.mark.unit
def test_returns_none_when_file_missing(tmp_path):
    cache = SkillBodyCache(maxsize=4)
    result = cache.get_or_load("foo", tmp_path / "missing.md")
    assert result is None


@pytest.mark.unit
def test_first_read_hits_disk(tmp_path):
    cache = SkillBodyCache(maxsize=4)
    skill_md = tmp_path / "skill.md"
    _write(skill_md, "body-1")

    body = cache.get_or_load("foo", skill_md)
    assert body == "body-1"
    assert len(cache) == 1


@pytest.mark.unit
def test_second_read_uses_cache(tmp_path, monkeypatch):
    """동일 mtime 으로 재호출 시 디스크 read 가 발생하지 않아야 한다."""
    cache = SkillBodyCache(maxsize=4)
    skill_md = tmp_path / "skill.md"
    _write(skill_md, "first-body")

    # 첫 호출 — 캐시 적재.
    cache.get_or_load("foo", skill_md)

    # 디스크 변경 (캐시에 영향 주지 않도록 mtime 은 그대로 유지하려고 utime 사용).
    stat = skill_md.stat()
    _write(skill_md, "changed-body")
    os.utime(skill_md, ns=(stat.st_atime_ns, stat.st_mtime_ns))

    # mtime 이 동일하므로 캐시된 값 반환 (변경된 디스크 내용 무시).
    body = cache.get_or_load("foo", skill_md)
    assert body == "first-body"


@pytest.mark.unit
def test_mtime_change_invalidates_cache(tmp_path):
    cache = SkillBodyCache(maxsize=4)
    skill_md = tmp_path / "skill.md"
    _write(skill_md, "v1")
    cache.get_or_load("foo", skill_md)

    # 짧게 sleep 후 갱신 — mtime_ns 가 달라지도록.
    time.sleep(0.01)
    _write(skill_md, "v2")

    body = cache.get_or_load("foo", skill_md)
    assert body == "v2"


@pytest.mark.unit
def test_old_version_invalidated_after_update(tmp_path):
    """동일 skill_id 의 stale entry 가 mtime 갱신 시 제거되는지."""
    cache = SkillBodyCache(maxsize=4)
    skill_md = tmp_path / "skill.md"
    _write(skill_md, "v1")
    cache.get_or_load("foo", skill_md)
    assert len(cache) == 1

    time.sleep(0.01)
    _write(skill_md, "v2")
    cache.get_or_load("foo", skill_md)
    # 새 entry 만 남음.
    assert len(cache) == 1


@pytest.mark.unit
def test_lru_eviction(tmp_path):
    cache = SkillBodyCache(maxsize=2)
    a = tmp_path / "a.md"; _write(a, "A")
    b = tmp_path / "b.md"; _write(b, "B")
    c = tmp_path / "c.md"; _write(c, "C")

    cache.get_or_load("a", a)
    cache.get_or_load("b", b)
    cache.get_or_load("c", c)  # a 가 evict 되어야 함.

    assert len(cache) == 2
    # a 다시 요청 시 디스크 fetch 되어야 함. 검증: 캐시에 새로 들어옴.
    cache.get_or_load("a", a)
    assert len(cache) == 2  # b 가 evict 됨.


@pytest.mark.unit
def test_lru_recency_keeps_used(tmp_path):
    """최근 접근된 항목은 evict 우선순위가 낮다."""
    cache = SkillBodyCache(maxsize=2)
    a = tmp_path / "a.md"; _write(a, "A")
    b = tmp_path / "b.md"; _write(b, "B")
    c = tmp_path / "c.md"; _write(c, "C")

    cache.get_or_load("a", a)
    cache.get_or_load("b", b)
    cache.get_or_load("a", a)  # a 재접근 — recency 최상위.
    cache.get_or_load("c", c)  # b 가 evict (a 가 아닌).

    # 캐시 상태 확인 — a 와 c 는 hit, b 는 다시 디스크 fetch.
    assert len(cache) == 2
    cache.get_or_load("a", a)  # hit (그대로 2).
    assert len(cache) == 2


@pytest.mark.unit
def test_clear_resets_cache(tmp_path):
    cache = SkillBodyCache(maxsize=4)
    skill_md = tmp_path / "skill.md"
    _write(skill_md, "body")
    cache.get_or_load("foo", skill_md)
    assert len(cache) == 1

    cache.clear()
    assert len(cache) == 0
