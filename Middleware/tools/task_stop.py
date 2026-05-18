"""백그라운드 작업 중단."""

import json
from .base import BaseTool


class TaskStopTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "task_stop"

    @property
    def description(self) -> str:
        return "Stop a running background task. No-op if already finished."

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
        stopped = self._mgr.stop(args.get("task_id", ""))
        if not stopped:
            return "Error: task not found."
        return json.dumps(stopped, ensure_ascii=False)
