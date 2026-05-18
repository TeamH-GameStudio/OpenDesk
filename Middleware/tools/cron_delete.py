"""예약 작업 삭제."""

from .base import BaseTool


class CronDeleteTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "cron_delete"

    @property
    def description(self) -> str:
        return "Delete a cron job by cron_id."

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "cron_id": {"type": "string"},
            },
            "required": ["cron_id"],
        }

    async def execute(self, args: dict) -> str:
        ok = self._mgr.delete(args.get("cron_id", ""))
        return "Deleted." if ok else "Error: cron job not found."
