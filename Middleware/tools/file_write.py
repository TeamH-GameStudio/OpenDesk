"""파일 쓰기 도구 — 워크스페이스 내 파일 생성/수정"""

import os
from .base import BaseTool


class FileWriteTool(BaseTool):
    def __init__(self, workspace_dir: str):
        self._workspace = workspace_dir

    @property
    def name(self):
        return "write_file"

    @property
    def description(self):
        return "Write content to a file. Creates if not exists."

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path from workspace root",
                },
                "content": {
                    "type": "string",
                    "description": "Content to write",
                },
            },
            "required": ["path", "content"],
        }

    async def execute(self, args: dict) -> str:
        path, content = args["path"], args["content"]
        full_path = os.path.join(self._workspace, path)
        real_path = os.path.realpath(full_path)
        if not real_path.startswith(os.path.realpath(self._workspace)):
            return "Error: Access denied."
        try:
            os.makedirs(os.path.dirname(full_path), exist_ok=True)
            with open(full_path, "w", encoding="utf-8") as f:
                f.write(content)
            return f"Wrote {len(content)} chars to {path}"
        except Exception as e:
            return f"Error: {e}"
