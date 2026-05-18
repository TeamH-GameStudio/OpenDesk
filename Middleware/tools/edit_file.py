"""부분 편집 도구 — old_string 을 new_string 으로 치환 (Claude Code Edit 규약)."""

import os
from .base import BaseTool


class EditFileTool(BaseTool):
    def __init__(self, workspace_dir: str):
        self._workspace = workspace_dir

    @property
    def name(self) -> str:
        return "edit_file"

    @property
    def description(self) -> str:
        return (
            "Edit a file by replacing old_string with new_string. "
            "old_string must appear exactly once in the file (unless replace_all=true). "
            "Use this for surgical edits — prefer this over rewriting the whole file."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path from workspace root",
                },
                "old_string": {
                    "type": "string",
                    "description": "Exact text to replace (must match including whitespace)",
                },
                "new_string": {
                    "type": "string",
                    "description": "Replacement text",
                },
                "replace_all": {
                    "type": "boolean",
                    "description": "Replace every occurrence (default false)",
                    "default": False,
                },
            },
            "required": ["path", "old_string", "new_string"],
        }

    async def execute(self, args: dict) -> str:
        path = args.get("path", "")
        old_string = args.get("old_string", "")
        new_string = args.get("new_string", "")
        replace_all = bool(args.get("replace_all", False))

        if not path:
            return "Error: path is required."
        if old_string == new_string:
            return "Error: old_string and new_string are identical."
        if not old_string:
            return "Error: old_string must not be empty."

        full_path = os.path.join(self._workspace, path)
        real_path = os.path.realpath(full_path)
        if not real_path.startswith(os.path.realpath(self._workspace)):
            return "Error: Access denied."

        try:
            with open(real_path, "r", encoding="utf-8") as f:
                content = f.read()
        except FileNotFoundError:
            return f"Error: File not found: {path}"
        except Exception as e:
            return f"Error reading file: {e}"

        count = content.count(old_string)
        if count == 0:
            return (
                f"Error: old_string not found in {path} "
                "(check whitespace/newlines — must match exactly)."
            )
        if count > 1 and not replace_all:
            return (
                f"Error: old_string appears {count} times in {path}. "
                "Provide more surrounding context to make it unique, "
                "or set replace_all=true."
            )

        if replace_all:
            new_content = content.replace(old_string, new_string)
            replacements = count
        else:
            new_content = content.replace(old_string, new_string, 1)
            replacements = 1

        try:
            with open(real_path, "w", encoding="utf-8") as f:
                f.write(new_content)
        except Exception as e:
            return f"Error writing file: {e}"

        delta = len(new_content) - len(content)
        return f"Edited {path} ({replacements} replacement(s), {delta:+d} bytes)."
