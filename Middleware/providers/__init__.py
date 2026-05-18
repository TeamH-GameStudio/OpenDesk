"""
OpenDesk 미들웨어 — AI provider 통합 게이트웨이.

각 provider 는 ProviderBase 를 구현하여 동일한 인터페이스(chat / set_mcp_config /
check_available) 를 노출한다. server.py 는 들어온 메시지의 `provider` 필드로 분기.

새 AI 모델(OpenAI/Codex, Gemini 등) 추가는 이 폴더에 모듈 한 개 추가 + register_provider.
Unity 측 코드는 변경할 필요 없다.
"""

from .base import ProviderBase, ProviderCallbacks
from .registry import get_provider, register_provider, list_providers

__all__ = [
    "ProviderBase",
    "ProviderCallbacks",
    "get_provider",
    "register_provider",
    "list_providers",
]
