"""백그라운드 작업 생성."""

import json
from .base import BaseTool


class TaskCreateTool(BaseTool):
    def __init__(self, manager, agent_id: str, workspace: str):
        self._mgr = manager
        self._agent_id = agent_id
        self._workspace = workspace

    @property
    def name(self) -> str:
        return "task_create"

    @property
    def description(self) -> str:
        return (
            "Start a long-running shell command in the background. "
            "Returns a task_id you can poll with task_get/task_output. "
            "Use for builds, data processing, monitoring — not for short commands (use bash for those)."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "Shell command to run (subject to the same allowlist as the bash tool)",
                },
                "description": {
                    "type": "string",
                    "description": "Short human-readable label for what this task does",
                },
            },
            "required": ["command", "description"],
        }

    async def execute(self, args: dict) -> str:
        command = (args.get("command") or "").strip()
        description = (args.get("description") or "").strip()
        if not command:
            return "Error: command is required."
        if not description:
            return "Error: description is required."

        result = self._mgr.create(
            agent_id=self._agent_id,
            command=command,
            description=description,
            workspace=self._workspace,
        )
        if "error" in result:
            return f"Error: {result['error']}"
        return json.dumps(result, ensure_ascii=False)
