"""파일 읽기 도구 — 워크스페이스 내 파일 읽기"""

import os
from .base import BaseTool


class FileReadTool(BaseTool):
    def __init__(self, workspace_dir: str):
        self._workspace = workspace_dir

    @property
    def name(self):
        return "read_file"

    @property
    def description(self):
        return "Read a file in the workspace directory."

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path from workspace root",
                }
            },
            "required": ["path"],
        }

    async def execute(self, args: dict) -> str:
        path = args["path"]
        full_path = os.path.join(self._workspace, path)
        real_path = os.path.realpath(full_path)
        if not real_path.startswith(os.path.realpath(self._workspace)):
            return "Error: Access denied."
        try:
            with open(real_path, "r", encoding="utf-8") as f:
                return f.read()[:50000]
        except FileNotFoundError:
            return f"Error: File not found: {path}"
        except Exception as e:
            return f"Error: {e}"
