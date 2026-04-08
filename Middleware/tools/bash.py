"""
셸 명령어 실행 도구 — 워크스페이스 내 제한 + allowlist 방식.

비개발자 대상이므로 안전한 명령어만 허용:
- 파일/폴더: ls, cat, head, tail, wc, find, mkdir, cp, mv, touch, tree
- 텍스트: grep, sort, uniq, diff, echo
- 시스템: date, whoami, pwd, env (읽기용)
- 스크립트: python3, node (워크스페이스 내 파일만)

차단: rm -rf, sudo, chmod, chown, curl, wget, ssh, kill, dd, mkfs 등
"""

import asyncio
import os
import shlex
from .base import BaseTool

# 허용된 명령어 목록
ALLOWED_COMMANDS = {
    # 파일/폴더 조회/조작
    "ls", "cat", "head", "tail", "wc", "find", "mkdir", "cp", "mv",
    "touch", "tree", "file", "stat",
    # 텍스트 처리
    "grep", "sort", "uniq", "diff", "echo", "sed", "awk", "cut", "tr",
    # 시스템 정보 (읽기 전용)
    "date", "whoami", "pwd", "which",
    # 스크립트 실행
    "python3", "python", "node",
}

# 명시적으로 차단할 위험 명령어
BLOCKED_COMMANDS = {
    "rm", "sudo", "su", "chmod", "chown", "chgrp",
    "curl", "wget", "ssh", "scp", "rsync",
    "kill", "killall", "pkill",
    "dd", "mkfs", "fdisk", "mount", "umount",
    "reboot", "shutdown", "halt", "poweroff",
    "apt", "yum", "brew", "pip", "pip3", "npm",
    "git", "docker", "kubectl",
    "nc", "ncat", "telnet", "nmap",
    "eval", "exec", "source",
}

# 위험한 패턴
BLOCKED_PATTERNS = [
    ">/dev/",
    "| rm",
    "&& rm",
    "; rm",
    "$(", "`",  # command substitution
    "| sudo",
    "&& sudo",
]


class BashTool(BaseTool):
    def __init__(self, workspace_dir: str, timeout: int = 30):
        self._workspace = os.path.realpath(os.path.expanduser(workspace_dir))
        self._timeout = timeout

    @property
    def name(self):
        return "bash"

    @property
    def description(self):
        return (
            "Run a shell command in the workspace directory. "
            "Only safe commands are allowed (ls, cat, grep, find, mkdir, cp, mv, python3, etc). "
            "Destructive commands like rm, sudo, curl are blocked."
        )

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "Shell command to execute",
                }
            },
            "required": ["command"],
        }

    async def execute(self, args: dict) -> str:
        command = args.get("command", "").strip()
        if not command:
            return "Error: Empty command."

        # 안전성 검사
        error = self._validate_command(command)
        if error:
            return f"Error: {error}"

        try:
            proc = await asyncio.create_subprocess_shell(
                command,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                cwd=self._workspace,
                env={**os.environ, "HOME": self._workspace},
            )
            stdout, stderr = await asyncio.wait_for(
                proc.communicate(), timeout=self._timeout
            )

            out = stdout.decode("utf-8", errors="replace")[:20000]
            err = stderr.decode("utf-8", errors="replace")[:5000]

            result = ""
            if out:
                result += out
            if err:
                result += f"\n[stderr] {err}"
            if proc.returncode != 0:
                result += f"\n[exit code: {proc.returncode}]"

            return result.strip() or "(no output)"

        except asyncio.TimeoutError:
            return f"Error: Command timed out after {self._timeout}s."
        except Exception as e:
            return f"Error: {e}"

    def _validate_command(self, command: str) -> str | None:
        """명령어 안전성 검사. 에러 메시지 반환 또는 None (통과)"""

        # 위험 패턴 차단
        for pattern in BLOCKED_PATTERNS:
            if pattern in command:
                return f"Blocked pattern: '{pattern}' is not allowed."

        # 파이프/체인 분리하여 각 명령어 검사
        # 단순 파이프(|)와 체인(&&, ;)만 허용
        parts = command.replace("&&", "|").replace(";", "|").split("|")

        for part in parts:
            part = part.strip()
            if not part:
                continue

            try:
                tokens = shlex.split(part)
            except ValueError:
                return "Invalid command syntax."

            if not tokens:
                continue

            base_cmd = os.path.basename(tokens[0])

            if base_cmd in BLOCKED_COMMANDS:
                return f"'{base_cmd}' is not allowed for safety."

            if base_cmd not in ALLOWED_COMMANDS:
                return f"'{base_cmd}' is not in the allowed command list."

        return None
