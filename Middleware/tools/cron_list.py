"""예약 작업 목록."""

import json
from .base import BaseTool


class CronListTool(BaseTool):
    def __init__(self, manager):
        self._mgr = manager

    @property
    def name(self) -> str:
        return "cron_list"

    @property
    def description(self) -> str:
        return "List all scheduled cron jobs."

    @property
    def parameters(self) -> dict:
        return {"type": "object", "properties": {}, "required": []}

    async def execute(self, args: dict) -> str:
        return json.dumps({"jobs": self._mgr.list()}, ensure_ascii=False)
