"""도구 레지스트리 — 에이전트별 도구 목록 관리"""

from typing import Dict, List
from .base import BaseTool


class ToolRegistry:
    def __init__(self):
        self._tools: Dict[str, BaseTool] = {}

    def register(self, tool: BaseTool):
        self._tools[tool.name] = tool

    def get(self, name: str) -> BaseTool | None:
        return self._tools.get(name)

    def to_anthropic_schemas(self) -> List[dict]:
        return [t.to_anthropic_schema() for t in self._tools.values()]
