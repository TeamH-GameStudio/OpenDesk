"""
Provider 레지스트리 — 이름으로 provider 인스턴스를 조회.

server.py 가 들어온 메시지의 `provider` 필드로 여기서 검색.
새 provider 모듈은 register_provider 로 자기 등록한다.
"""

from __future__ import annotations

from typing import Callable

from .base import ProviderBase

# 이름 → 팩토리. 팩토리는 인자 없이 호출되어 provider 인스턴스를 만든다.
_factories: dict[str, Callable[[], ProviderBase]] = {}


def register_provider(name: str, factory: Callable[[], ProviderBase]) -> None:
    if not name:
        raise ValueError("provider name is required")
    _factories[name] = factory


def get_provider(name: str) -> ProviderBase:
    if name not in _factories:
        raise KeyError(f"unknown provider: {name}. available: {list(_factories.keys())}")
    return _factories[name]()


def list_providers() -> list[str]:
    return list(_factories.keys())
