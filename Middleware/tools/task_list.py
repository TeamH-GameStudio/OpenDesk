"""이 에이전트가 만든 백그라운드 작업 목록 조회."""

import json
from .base import BaseTool


class TaskListTool(BaseTool):
    def __init__(self, manager, agent_id: str):
        self._mgr = manager
        self._agent_id = agent_id

    @property
    def name(self) -> str:
        return "task_list"

    @property
    def description(self) -> str:
        return "List all background tasks this agent has created (newest first)."

    @property
    def parameters(self) -> dict:
        return {"type": "object", "properties": {}, "required": []}

    async def execute(self, args: dict) -> str:
        tasks = self._mgr.list(agent_id=self._agent_id)
        return json.dumps({"tasks": tasks}, ensure_ascii=False)
