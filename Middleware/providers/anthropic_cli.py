"""
AnthropicCliProvider — Claude CLI subprocess 호출.

기존 `claude_bridge.py` 의 로직을 ProviderBase 인터페이스에 맞춰 흡수했다.
변경점:
  - `--tools ""` 제거 (도구 호출/MCP 지원을 위해)
  - mcp_config 가 주어지면 임시 `mcp-config.json` 파일을 만들고 `--mcp-config <path>` 옵션 전달
  - 임시 파일은 호출 종료 시 정리
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import shutil
import tempfile
from pathlib import Path
from typing import Any, AsyncIterator, Optional

from hooks.protocol import UsageSnapshot

from .base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)
from .registry import register_provider

logger = logging.getLogger("provider.anthropic_cli")


def _extract_cli_usage(result_data: dict[str, Any]) -> UsageSnapshot:
    """CLI result JSON 의 usage 블록을 best-effort 추출.

    CLI 는 SDK 만큼 일관된 cache 통계를 제공하지 않을 수 있으므로:
      - usage 블록이 있고 input/output 이 있으면 input/output 만 채움 (available=True)
      - cache_* 필드가 있으면 그것도 채움
      - usage 블록 자체가 없으면 available=False
    """
    usage = result_data.get("usage")
    if not isinstance(usage, dict):
        return UsageSnapshot(available=False)
    return UsageSnapshot(
        input_tokens=int(usage.get("input_tokens") or 0),
        output_tokens=int(usage.get("output_tokens") or 0),
        cache_creation_input_tokens=int(usage.get("cache_creation_input_tokens") or 0),
        cache_read_input_tokens=int(usage.get("cache_read_input_tokens") or 0),
        # CLI 가 cache 필드를 제공해도 SDK 만큼 신뢰 가능한지는 모름 — 보수적으로 True 두되
        # telemetry_completeness=partial 로 emitter 에 마킹하여 UI 에서 표시.
        available=True,
    )


class AnthropicCliProvider(ProviderBase):
    name = "anthropic_cli"

    def __init__(self, cli_path: str = "claude", timeout_seconds: int = 120):
        self._cli_path = cli_path
        self._timeout = timeout_seconds
        self._active_process: Optional[asyncio.subprocess.Process] = None
        self._in_process_warning_shown = False  # in_process_tools 미지원 경고 한 번만

    async def check_available(self) -> tuple[bool, str]:
        resolved = shutil.which(self._cli_path)
        if not resolved:
            return False, "cli_not_found"
        try:
            proc = await asyncio.create_subprocess_exec(
                self._cli_path, "--version",
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                env=_build_isolated_env(),
                cwd=str(_isolated_cwd()),
            )
            stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=10)
            version = stdout.decode().strip()
            config_dir = os.environ.get("CLAUDE_CONFIG_DIR", "(unset)")
            logger.info("Claude CLI found: %s [CLAUDE_CONFIG_DIR=%s]", version, config_dir)
            if not _isolated_dir_has_credentials():
                logger.warning(
                    "격리 디렉토리에 인증 정보가 없습니다. 'CLAUDE_CONFIG_DIR=%s claude login' "
                    "또는 ANTHROPIC_API_KEY 환경변수가 필요합니다.",
                    config_dir,
                )
            return True, version
        except Exception as e:  # noqa: BLE001
            return False, str(e)

    async def chat(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        callbacks: ProviderCallbacks,
        in_process_tools=None,  # CLI subprocess 는 호스트 in-process 도구 접근 불가 — 무시.
    ) -> None:
        if in_process_tools is not None and not self._in_process_warning_shown:
            import logging as _lg
            _lg.getLogger("provider.anthropic_cli").warning(
                "[DEPRECATED] anthropic_cli 백엔드는 in_process 도구 (%d개) 를 호출할 수 없습니다. "
                "기본 백엔드 anthropic_api 로 전환하세요 — API key 없어도 `claude login` OAuth 로 동작합니다. "
                "(PlayerPrefs OpenDesk_ChatBackend='anthropic_api')",
                len(in_process_tools._tools) if hasattr(in_process_tools, "_tools") else 0,
            )
            self._in_process_warning_shown = True
        if self._active_process and self._active_process.returncode is None:
            await callbacks.on_error("이전 응답을 기다리는 중입니다", "server_busy")
            return

        flat_prompt = flatten_messages_for_cli(messages, system_prompt)
        if not flat_prompt:
            await callbacks.on_error("메시지가 비어있습니다", "empty_message")
            return

        mcp_config_path: Optional[Path] = None
        try:
            args = [self._cli_path, "-p", flat_prompt, "--output-format", "stream-json", "--verbose"]
            if model:
                args.extend(["--model", model])

            mcp_config_path = _write_mcp_config_to_tempfile(mcp_config)
            if mcp_config_path is not None:
                args.extend(["--mcp-config", str(mcp_config_path)])
                logger.info("MCP config attached: %d server(s)", _count_servers(mcp_config))

            # Claude CLI 격리 — 부모 환경변수를 명시적으로 복사하고 CLAUDE_CONFIG_DIR 를 강제로 채워
            # 글로벌 ~/.claude/ 대신 OpenDesk 격리 디렉토리만 사용하게 한다.
            child_env = _build_isolated_env()

            self._active_process = await asyncio.create_subprocess_exec(
                *args,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                limit=1024 * 1024,
                env=child_env,
                cwd=str(_isolated_cwd()),
            )

            await self._stream_response(callbacks)

        except FileNotFoundError:
            await callbacks.on_error(
                f"Claude CLI를 찾을 수 없습니다: {self._cli_path}",
                "cli_not_found",
            )
        except Exception as e:  # noqa: BLE001
            await callbacks.on_error(str(e), "cli_error")
        finally:
            self._active_process = None
            if mcp_config_path is not None:
                try:
                    os.unlink(mcp_config_path)
                except OSError:
                    pass

    def kill_active(self) -> None:
        if self._active_process and self._active_process.returncode is None:
            try:
                self._active_process.kill()
                logger.info("Active CLI process killed")
            except ProcessLookupError:
                pass

    # ── 신규 스트림 인터페이스 (native, best-effort) ───────────────

    async def run_stream(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        in_process_tools=None,  # CLI subprocess 는 호스트 in-process 도구 접근 불가 — 무시.
    ) -> AsyncIterator[StreamEvent]:
        """Claude CLI stream-json stdout 을 라인 단위로 파싱해 동일한 이벤트 시퀀스로 어댑팅.

        CLI 는 누적 텍스트를 보내므로 diff 로 새 부분만 TextDeltaEvent 로 yield 한다.
        """
        if self._active_process and self._active_process.returncode is None:
            yield MessageStopEvent(
                reason="error",
                error_message="이전 응답을 기다리는 중입니다",
                error_code="server_busy",
            )
            return

        flat_prompt = flatten_messages_for_cli(messages, system_prompt)
        if not flat_prompt:
            yield MessageStopEvent(
                reason="error",
                error_message="메시지가 비어있습니다",
                error_code="empty_message",
            )
            return

        mcp_config_path: Optional[Path] = None
        accumulated = ""
        try:
            args = [self._cli_path, "-p", flat_prompt, "--output-format", "stream-json", "--verbose"]
            if model:
                args.extend(["--model", model])
            mcp_config_path = _write_mcp_config_to_tempfile(mcp_config)
            if mcp_config_path is not None:
                args.extend(["--mcp-config", str(mcp_config_path)])

            child_env = _build_isolated_env()
            self._active_process = await asyncio.create_subprocess_exec(
                *args,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                limit=1024 * 1024,
                env=child_env,
                cwd=str(_isolated_cwd()),
            )

            try:
                async with asyncio.timeout(self._timeout):
                    while True:
                        line = await self._active_process.stdout.readline()
                        if not line:
                            break
                        line_str = line.decode("utf-8", errors="replace").strip()
                        if not line_str:
                            continue
                        try:
                            data = json.loads(line_str)
                        except json.JSONDecodeError:
                            continue

                        msg_type = data.get("type", "")
                        if msg_type == "assistant":
                            message = data.get("message", {})
                            for block in message.get("content", []):
                                btype = block.get("type", "")
                                if btype == "text":
                                    text = block.get("text", "")
                                    if not text:
                                        continue
                                    if text.startswith(accumulated):
                                        new_part = text[len(accumulated):]
                                    else:
                                        new_part = text
                                    if new_part:
                                        accumulated = text
                                        yield TextDeltaEvent(text=new_part)
                                elif btype == "tool_use":
                                    yield ToolUseStartEvent(
                                        tool_use_id=block.get("id", ""),
                                        name=block.get("name", ""),
                                        input=block.get("input", {}) or {},
                                    )
                        elif msg_type == "user":
                            # tool_result 회신
                            message = data.get("message", {})
                            for block in message.get("content", []):
                                if block.get("type") == "tool_result":
                                    content = block.get("content", "")
                                    if isinstance(content, list):
                                        # Claude CLI 가 list-of-blocks 로 줄 수 있음
                                        text_parts = [c.get("text", "") for c in content if isinstance(c, dict)]
                                        result_text = "".join(text_parts)
                                    else:
                                        result_text = str(content)
                                    yield ToolUseResultEvent(
                                        tool_use_id=block.get("tool_use_id", ""),
                                        name="",
                                        result=result_text,
                                        is_error=bool(block.get("is_error", False)),
                                    )
                        elif msg_type == "result":
                            cost = float(data.get("total_cost_usd", 0.0))
                            yield MessageStopEvent(
                                reason="complete",
                                accumulated_text=data.get("result", accumulated),
                                cost=cost,
                                usage=_extract_cli_usage(data),
                            )
                            return
            except TimeoutError:
                self.kill_active()
                yield MessageStopEvent(
                    reason="error",
                    accumulated_text=accumulated,
                    error_message="응답 시간 초과",
                    error_code="cli_timeout",
                )
                return

            # stdout 끝났는데 result 없음 — stderr 확인
            stderr_data = await self._active_process.stderr.read()
            stderr_text = stderr_data.decode("utf-8", errors="replace").strip()
            if self._active_process.returncode and self._active_process.returncode != 0 and stderr_text:
                code = "cli_error"
                lowered = stderr_text.lower()
                if "auth" in lowered or "login" in lowered:
                    code = "cli_auth_error"
                elif "rate" in lowered or "limit" in lowered:
                    code = "rate_limit"
                yield MessageStopEvent(
                    reason="error",
                    accumulated_text=accumulated,
                    error_message=stderr_text[:300],
                    error_code=code,
                )
            elif accumulated:
                yield MessageStopEvent(reason="complete", accumulated_text=accumulated)
            else:
                yield MessageStopEvent(
                    reason="error",
                    error_message="Claude CLI에서 응답이 없습니다",
                    error_code="cli_error",
                )

        except FileNotFoundError:
            yield MessageStopEvent(
                reason="error",
                error_message=f"Claude CLI를 찾을 수 없습니다: {self._cli_path}",
                error_code="cli_not_found",
            )
        except Exception as e:  # noqa: BLE001
            yield MessageStopEvent(
                reason="error",
                accumulated_text=accumulated,
                error_message=str(e),
                error_code="cli_error",
            )
        finally:
            self._active_process = None
            if mcp_config_path is not None:
                try:
                    os.unlink(mcp_config_path)
                except OSError:
                    pass

    # ── 내부 ────────────────────────────────────────────────────

    async def _stream_response(self, callbacks: ProviderCallbacks) -> None:
        assert self._active_process is not None
        accumulated_text = ""

        try:
            async with asyncio.timeout(self._timeout):
                while True:
                    line = await self._active_process.stdout.readline()
                    if not line:
                        break

                    line_str = line.decode("utf-8", errors="replace").strip()
                    if not line_str:
                        continue

                    try:
                        data = json.loads(line_str)
                    except json.JSONDecodeError:
                        logger.debug("Non-JSON line: %s", line_str[:100])
                        continue

                    msg_type = data.get("type", "")

                    if msg_type == "assistant":
                        accumulated_text = await self._process_assistant_block(
                            data, accumulated_text, callbacks
                        )

                    elif msg_type == "result":
                        result_text = data.get("result", accumulated_text)
                        cost = float(data.get("total_cost_usd", 0.0))
                        await callbacks.on_final(result_text, cost)
                        return

                    elif msg_type == "system":
                        subtype = data.get("subtype", "")
                        if subtype == "init":
                            logger.info(
                                "CLI session init: model=%s", data.get("model", "unknown")
                            )

        except TimeoutError:
            self.kill_active()
            await callbacks.on_error("응답 시간 초과", "cli_timeout")
            return

        # stdout 끝났는데 result 없음
        stderr_data = await self._active_process.stderr.read()
        stderr_text = stderr_data.decode("utf-8", errors="replace").strip()

        if self._active_process.returncode and self._active_process.returncode != 0 and stderr_text:
            code = "cli_error"
            lowered = stderr_text.lower()
            if "auth" in lowered or "login" in lowered:
                code = "cli_auth_error"
            elif "rate" in lowered or "limit" in lowered:
                code = "rate_limit"
            await callbacks.on_error(stderr_text[:300], code)
        elif accumulated_text:
            await callbacks.on_final(accumulated_text, 0.0)
        else:
            await callbacks.on_error("Claude CLI에서 응답이 없습니다", "cli_error")

    async def _process_assistant_block(
        self,
        data: dict[str, Any],
        accumulated_text: str,
        callbacks: ProviderCallbacks,
    ) -> str:
        message = data.get("message", {})
        for block in message.get("content", []):
            block_type = block.get("type", "")

            if block_type == "thinking" and callbacks.on_status:
                await callbacks.on_status("사고 중...")

            elif block_type == "tool_use" and callbacks.on_status:
                tool_name = block.get("name", "도구")
                await callbacks.on_status(f"도구 호출: {tool_name}")

            elif block_type == "tool_result" and callbacks.on_status:
                await callbacks.on_status("도구 결과 수신")

            elif block_type == "text":
                text = block.get("text", "")
                if not text:
                    continue
                if callbacks.on_status and not accumulated_text:
                    await callbacks.on_status("응답 중...")

                # Claude CLI stream-json 은 누적 텍스트를 보낸다 → diff 계산
                if text.startswith(accumulated_text):
                    new_part = text[len(accumulated_text):]
                else:
                    new_part = text

                if new_part:
                    accumulated_text = text
                    await callbacks.on_delta(new_part)
        return accumulated_text


def flatten_messages_for_cli(
    messages: list[dict[str, Any]],
    system_prompt: str = "",
    history_chars_per_message: int = 500,
) -> str:
    """messages 리스트와 system_prompt 를 CLI 가 받는 단일 -p 인자로 평탄화.

    포맷:
      [System] {system}
      [이전 대화]
      사용자: ...
      AI: ...
      [현재 질문] {last user}
    """
    if not messages:
        return ""

    # 마지막 user 메시지 인덱스
    last_user_idx = -1
    for i in range(len(messages) - 1, -1, -1):
        if messages[i].get("role") == "user":
            last_user_idx = i
            break
    if last_user_idx < 0:
        return ""

    parts: list[str] = []
    if system_prompt:
        parts.append(f"[System]\n{system_prompt}\n")

    if last_user_idx > 0:
        parts.append("[이전 대화]")
        for m in messages[:last_user_idx]:
            role = m.get("role", "")
            text = str(m.get("content", ""))[:history_chars_per_message]
            label = "사용자" if role == "user" else "AI"
            parts.append(f"{label}: {text}")
        parts.append("")

    current = str(messages[last_user_idx].get("content", ""))
    parts.append(f"[현재 질문]\n{current}")
    return "\n".join(parts)


def build_cli_mcp_config(payload: Optional[dict[str, Any]]) -> Optional[dict[str, Any]]:
    """McpConfigPayload(JSON dict) → Claude CLI 가 받는 mcp-config.json 포맷으로 변환.

    Claude CLI 는 `{"mcpServers": {name: {command, args, env}}}` 구조를 받는다.
    None / 빈 servers 면 None 반환 (호출자가 --mcp-config 생략).
    """
    if not payload:
        return None
    servers = payload.get("servers") or []
    if not servers:
        return None

    out: dict[str, dict[str, Any]] = {}
    for entry in servers:
        if not isinstance(entry, dict):
            continue
        name = entry.get("name")
        command = entry.get("command")
        if not name or not command:
            continue

        args = list(entry.get("args") or [])
        env_pairs = entry.get("env") or []
        env: dict[str, str] = {}
        for pair in env_pairs:
            key = pair.get("key") if isinstance(pair, dict) else None
            value = pair.get("value") if isinstance(pair, dict) else None
            if key:
                env[key] = value or ""

        server_block: dict[str, Any] = {"command": command, "args": args}
        if env:
            server_block["env"] = env
        out[name] = server_block

    if not out:
        return None
    return {"mcpServers": out}


def _write_mcp_config_to_tempfile(payload: Optional[dict[str, Any]]) -> Optional[Path]:
    cli_config = build_cli_mcp_config(payload)
    if cli_config is None:
        return None
    fd, path = tempfile.mkstemp(prefix="opendesk-mcp-", suffix=".json")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(cli_config, f, ensure_ascii=False)
        return Path(path)
    except Exception:
        try:
            os.unlink(path)
        except OSError:
            pass
        raise


def _count_servers(payload: Optional[dict[str, Any]]) -> int:
    if not payload:
        return 0
    return len(payload.get("servers") or [])


# ── 격리 헬퍼 ────────────────────────────────────────────────────

def _isolated_cwd() -> Path:
    """Claude CLI subprocess 의 작업 디렉토리. 글로벌 .claude/ 가 cwd traversal 로 잡히지 않도록
    OpenDesk 격리 디렉토리 하위 빈 폴더에서 실행한다.
    """
    base = os.environ.get("CLAUDE_CONFIG_DIR")
    if not base:
        base = os.path.join(os.path.expanduser("~"), ".opendesk", "claude-cli")
    cwd = Path(base) / "cwd"
    try:
        cwd.mkdir(parents=True, exist_ok=True)
    except OSError as e:
        logger.warning("isolated cwd 생성 실패: %s (%s)", cwd, e)
        return Path(base) if Path(base).exists() else Path(os.path.expanduser("~"))
    return cwd


def _isolated_dir_has_credentials() -> bool:
    """격리 디렉토리에 Claude CLI 인증 흔적이 있는지 가벼운 검사.
    .credentials.json / credentials.json / config.json 중 하나라도 있으면 인증된 것으로 간주.
    ANTHROPIC_API_KEY 환경변수가 있으면 그것도 인증으로 간주.
    """
    if os.environ.get("ANTHROPIC_API_KEY"):
        return True
    base = os.environ.get("CLAUDE_CONFIG_DIR")
    if not base:
        return False
    p = Path(base)
    if not p.exists():
        return False
    candidates = [".credentials.json", "credentials.json", "config.json", ".claude.json"]
    return any((p / name).exists() for name in candidates)


def _build_isolated_env() -> dict[str, str]:
    """현재 프로세스 환경변수를 복사하고 CLAUDE_CONFIG_DIR 를 강제. HOME 은 유지하되 격리 우선."""
    env = dict(os.environ)
    config_dir = env.get("CLAUDE_CONFIG_DIR")
    if not config_dir:
        config_dir = os.path.join(os.path.expanduser("~"), ".opendesk", "claude-cli")
    env["CLAUDE_CONFIG_DIR"] = config_dir
    return env


# ── 자기 등록 ────────────────────────────────────────────────────

def _make_provider() -> AnthropicCliProvider:
    # 환경변수에서 설정 읽기 (server.py 가 config.json 로드 후 환경변수로 노출 가능)
    cli_path = os.environ.get("OPENDESK_CLAUDE_CLI_PATH", "claude")
    timeout = int(os.environ.get("OPENDESK_CLAUDE_TIMEOUT", "120"))
    return AnthropicCliProvider(cli_path=cli_path, timeout_seconds=timeout)


register_provider("anthropic_cli", _make_provider)
