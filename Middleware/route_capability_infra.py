"""route_capability 의 백엔드 인프라.

- ``PluginRegistry``: 세션별 in-memory 플러그인 카탈로그. Unity 가 ``set_plugin_registry``
  op 로 채워 넣는다. 각 항목은 ``{id, display_name, vendor, author, capabilities[]}``.

- ``RoutePreferenceStore``: 에이전트별 capability → plugin_id 매핑을 JSON 파일로 영속.
  ``~/.opendesk/route_prefs/{agent_id}.json``. 동기 I/O — JSON 한 줄, 변경 빈도 낮음.

route_capability 도구의 "4-way 분기" 가 이 두 객체만 의존하도록 설계 — 테스트가
파일 시스템 모킹 없이도 가능하도록 protocol 로 둔다 (Plugin / RoutePreference Port).
"""

from __future__ import annotations

import json
import logging
import os
import threading
from dataclasses import dataclass, field
from typing import Iterable, Protocol

logger = logging.getLogger("route_capability")


# ── 데이터 모델 ───────────────────────────────────────────


@dataclass(frozen=True)
class PluginEntry:
    """설치된 플러그인 한 개. capability 는 정규화된 문자열 (예: 'calendar.create_event')."""

    id: str
    display_name: str
    vendor: str
    author: str
    capabilities: tuple[str, ...]

    def serializable(self) -> dict:
        return {
            "id": self.id,
            "display_name": self.display_name,
            "vendor": self.vendor,
            "author": self.author,
        }


# ── 포트 (테스트 더블을 위해 분리) ────────────────────────


class PluginRegistryPort(Protocol):
    def list_compatible(self, capability: str) -> list[PluginEntry]: ...


class RoutePreferenceStorePort(Protocol):
    def get(self, agent_id: str, capability: str) -> str | None: ...
    def set(self, agent_id: str, capability: str, plugin_id: str) -> None: ...


# ── PluginRegistry — per-session in-memory ────────────────


@dataclass
class PluginRegistry:
    """Unity 가 push 한 플러그인 목록 캐시. 세션당 1개."""

    _entries: list[PluginEntry] = field(default_factory=list)

    def replace(self, payload: list[dict]) -> int:
        """전체 교체. payload 는 ``[{id, display_name?, vendor?, author?, capabilities[]}]``."""
        cleaned: list[PluginEntry] = []
        for raw in payload or []:
            if not isinstance(raw, dict):
                continue
            pid = (raw.get("id") or "").strip()
            if not pid:
                continue
            caps_raw = raw.get("capabilities") or []
            caps = tuple(c.strip() for c in caps_raw if isinstance(c, str) and c.strip())
            cleaned.append(
                PluginEntry(
                    id=pid,
                    display_name=(raw.get("display_name") or raw.get("displayName") or pid).strip(),
                    vendor=(raw.get("vendor") or "").strip(),
                    author=(raw.get("author") or "").strip(),
                    capabilities=caps,
                )
            )
        self._entries = cleaned
        return len(self._entries)

    def list_compatible(self, capability: str) -> list[PluginEntry]:
        cap = (capability or "").strip()
        if not cap:
            return []
        return [e for e in self._entries if cap in e.capabilities]

    def clear(self) -> None:
        self._entries = []


# ── RoutePreferenceStore — JSON 파일 영속 ─────────────────


class RoutePreferenceStore:
    """``~/.opendesk/route_prefs/{agent_id}.json`` 에 ``{capability: plugin_id}`` 저장.

    Thread-safe: 멀티 세션이 같은 에이전트의 선호를 동시에 쓸 가능성에 대비해
    파일 접근에만 lock 을 건다 (변경 빈도가 낮아 contention 무시 가능).
    """

    def __init__(self, base_dir: str = "~/.opendesk/route_prefs"):
        self._dir = os.path.expanduser(base_dir)
        self._lock = threading.Lock()

    def _path(self, agent_id: str) -> str:
        safe = "".join(c if c.isalnum() or c in "-_." else "_" for c in agent_id or "default")
        return os.path.join(self._dir, f"{safe or 'default'}.json")

    def _load(self, agent_id: str) -> dict[str, str]:
        path = self._path(agent_id)
        if not os.path.exists(path):
            return {}
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            return {k: v for k, v in data.items() if isinstance(k, str) and isinstance(v, str)}
        except (OSError, json.JSONDecodeError) as e:
            logger.warning("route_prefs load 실패 (%s): %s — 빈 dict 로 폴백", path, e)
            return {}

    def get(self, agent_id: str, capability: str) -> str | None:
        cap = (capability or "").strip()
        if not cap:
            return None
        with self._lock:
            return self._load(agent_id).get(cap)

    def set(self, agent_id: str, capability: str, plugin_id: str) -> None:
        cap = (capability or "").strip()
        pid = (plugin_id or "").strip()
        if not cap or not pid:
            return
        with self._lock:
            os.makedirs(self._dir, exist_ok=True)
            current = self._load(agent_id)
            if current.get(cap) == pid:
                return
            current[cap] = pid
            path = self._path(agent_id)
            try:
                tmp = path + ".tmp"
                with open(tmp, "w", encoding="utf-8") as f:
                    json.dump(current, f, ensure_ascii=False, indent=2)
                os.replace(tmp, path)
            except OSError as e:
                logger.warning("route_prefs save 실패 (%s): %s", path, e)


# ── 모듈 레벨 싱글톤 헬퍼 ──────────────────────────────────

_DEFAULT_STORE: RoutePreferenceStore | None = None


def default_preference_store() -> RoutePreferenceStore:
    """프로세스 공통 RoutePreferenceStore. 미들웨어 부팅 시 1회 생성된다."""
    global _DEFAULT_STORE
    if _DEFAULT_STORE is None:
        _DEFAULT_STORE = RoutePreferenceStore()
    return _DEFAULT_STORE


__all__ = [
    "PluginEntry",
    "PluginRegistry",
    "PluginRegistryPort",
    "RoutePreferenceStore",
    "RoutePreferenceStorePort",
    "default_preference_store",
]
