"""
OpenDesk middleware hook subsystem.

ChatSession 이 1회 빌드한 HookPipeline 을 HookedProvider 가 사용하여 lifecycle 시점에
hook 들을 실행한다. 자세한 architecture 는 ../PROTOCOL.md 의 telemetry 섹션 참고.
"""

from .base import BaseHook
from .pipeline import HookPipeline
from .protocol import ErrorAction, MessageHook, RequestCtx, UsageSnapshot

__all__ = [
    "BaseHook",
    "ErrorAction",
    "HookPipeline",
    "MessageHook",
    "RequestCtx",
    "UsageSnapshot",
]
