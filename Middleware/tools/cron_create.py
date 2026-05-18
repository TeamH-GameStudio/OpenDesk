"""예약 작업 생성."""

import json
from .base import BaseTool


class CronCreateTool(BaseTool):
    def __init__(self, manager, agent_id: str, command_validator=None):
        self._mgr = manager
        self._agent_id = agent_id
        self._command_validator = command_validator

    @property
    def name(self) -> str:
        return "cron_create"

    @property
    def description(self) -> str:
        return (
            "Schedule a shell command to run on a cron expression. "
            "Use the standard 5-field crontab format (minute hour day month day-of-week). "
            "Examples: '0 9 * * *' (9am daily), '*/15 * * * *' (every 15 minutes)."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "name": {
                    "type": "string",
                    "description": "Short human-readable label",
                },
                "schedule": {
                    "type": "string",
                    "description": "5-field crontab expression",
                },
                "command": {
                    "type": "string",
                    "description": "Shell command (subject to the same allowlist as bash tool)",
                },
            },
            "required": ["name", "schedule", "command"],
        }

    async def execute(self, args: dict) -> str:
        command = (args.get("command") or "").strip()
        schedule = (args.get("schedule") or "").strip()
        name = (args.get("name") or "").strip()
        if not (command and schedule and name):
            return "Error: name, schedule, and command are all required."

        if self._command_validator:
            err = self._command_validator(command)
            if err:
                return f"Error: {err}"

        result = self._mgr.create(
            agent_id=self._agent_id,
            name=name,
            schedule=schedule,
            command=command,
        )
        if "error" in result:
            return f"Error: {result['error']}"
        return json.dumps(result, ensure_ascii=False)
