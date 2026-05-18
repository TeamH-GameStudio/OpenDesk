"""백그라운드 작업 stdout 폴링."""

import json
from .base import BaseTool


class TaskOutputTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "task_output"

    @property
    def description(self) -> str:
        return (
            "Read accumulated stdout/stderr from a background task. "
            "Pass `since` (byte offset) to get only new output since the last poll. "
            "Returns up to 20000 chars per call."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "task_id": {"type": "string"},
                "since": {
                    "type": "integer",
                    "description": "Byte offset to start reading from (use 0 for full output)",
                    "default": 0,
                },
            },
            "required": ["task_id"],
        }

    async def execute(self, args: dict) -> str:
        result = self._mgr.output(
            task_id=args.get("task_id", ""),
            since=int(args.get("since", 0) or 0),
        )
        if "error" in result:
            return f"Error: {result['error']}"
        return json.dumps(result, ensure_ascii=False)
