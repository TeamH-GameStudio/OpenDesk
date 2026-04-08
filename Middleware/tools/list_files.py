"""워크스페이스 디렉토리 목록 조회 도구."""

import os
from .base import BaseTool


class ListFilesTool(BaseTool):
    def __init__(self, workspace_dir: str, max_depth: int = 3):
        self._workspace = os.path.realpath(os.path.expanduser(workspace_dir))
        self._max_depth = max_depth

    @property
    def name(self):
        return "list_files"

    @property
    def description(self):
        return (
            "List files and directories in the workspace. "
            "Shows a tree structure with file sizes. "
            "Use this to explore what files exist before reading them."
        )

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path from workspace root (default: root)",
                    "default": ".",
                }
            },
            "required": [],
        }

    async def execute(self, args: dict) -> str:
        rel_path = args.get("path", ".").strip() or "."
        target = os.path.realpath(os.path.join(self._workspace, rel_path))

        if not target.startswith(self._workspace):
            return "Error: Access denied."

        if not os.path.exists(target):
            return f"Error: Path not found: {rel_path}"

        if not os.path.isdir(target):
            return f"Error: Not a directory: {rel_path}"

        lines = []
        self._build_tree(target, "", 0, lines)

        if not lines:
            return "(empty directory)"

        return "\n".join(lines[:500])  # 최대 500줄

    def _build_tree(self, path: str, prefix: str, depth: int, lines: list):
        if depth > self._max_depth:
            lines.append(f"{prefix}... (max depth reached)")
            return

        try:
            entries = sorted(os.listdir(path))
        except PermissionError:
            lines.append(f"{prefix}(permission denied)")
            return

        dirs = [e for e in entries if os.path.isdir(os.path.join(path, e)) and not e.startswith(".")]
        files = [e for e in entries if os.path.isfile(os.path.join(path, e)) and not e.startswith(".")]

        for d in dirs:
            lines.append(f"{prefix}{d}/")
            self._build_tree(os.path.join(path, d), prefix + "  ", depth + 1, lines)

        for f in files:
            size = _format_size(os.path.getsize(os.path.join(path, f)))
            lines.append(f"{prefix}{f}  ({size})")


def _format_size(size: int) -> str:
    if size < 1024:
        return f"{size}B"
    elif size < 1024 * 1024:
        return f"{size / 1024:.1f}KB"
    else:
        return f"{size / (1024 * 1024):.1f}MB"
