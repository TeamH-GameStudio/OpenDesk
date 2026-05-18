"""백그라운드 작업 메타데이터 갱신 (description 등)."""

import json
from .base import BaseTool


class TaskUpdateTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "task_update"

    @property
    def description(self) -> str:
        return "Update a background task's description. Cannot change command or status."

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "task_id": {"type": "string"},
                "description": {"type": "string"},
            },
            "required": ["task_id", "description"],
        }

    async def execute(self, args: dict) -> str:
        updated = self._mgr.update(
            args.get("task_id", ""),
            description=args.get("description", ""),
        )
        if not updated:
            return "Error: task not found."
        return json.dumps(updated, ensure_ascii=False)
