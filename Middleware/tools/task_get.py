"""백그라운드 작업 단건 조회."""

import json
from .base import BaseTool


class TaskGetTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "task_get"

    @property
    def description(self) -> str:
        return "Get the current status of a background task by task_id."

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "task_id": {"type": "string"},
            },
            "required": ["task_id"],
        }

    async def execute(self, args: dict) -> str:
        task = self._mgr.get(args.get("task_id", ""))
        if not task:
            return "Error: task not found."
        return json.dumps(task, ensure_ascii=False)
