"""
Claude CLI 'claude login' 격리 OAuth 플로우 — 미들웨어 진입점.

Unity 가 auth.start RPC 를 보내면 본 모듈이 `claude login` 을 격리된 CLAUDE_CONFIG_DIR
하에서 subprocess 로 실행한다. stdout 을 파싱해 다음 이벤트를 푸시한다:
    - "url"     : 사용자가 브라우저에서 열어야 하는 인증 URL
    - "code"    : device code (있을 때만)
    - "status"  : "polling" / "authorizing" 등 진행 메시지
    - "success" : 인증 완료 (격리 디렉토리에 토큰 저장됨)
    - "failed"  : 에러/취소

`claude login` 의 출력 포맷이 버전마다 다를 수 있어 정규식으로 광범위 매칭한다.
파싱이 깨져도 종료 코드 (0=성공, !=0=실패) 로 최종 판정 가능.
"""

from __future__ import annotations

import asyncio
import logging
import os
import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Awaitable, Callable, Optional

logger = logging.getLogger("auth_login")

# claude login 출력에서 추출할 패턴 — 광범위 매칭. 어느 한 패턴이라도 잡히면 사용자에게 노출.
_URL_RE = re.compile(r"https?://[^\s'\"]+", re.IGNORECASE)
_CODE_RE = re.compile(r"\b([A-Z0-9]{4}-[A-Z0-9]{4})\b")
_SUCCESS_RE = re.compile(
    r"(success(fully)?|logged\s*in|authenticat(ed|ion\s*complete)|all\s*set)",
    re.IGNORECASE,
)
_FAILURE_RE = re.compile(
    r"(fail(ed)?|denied|expired|cancel(l)?ed|invalid|error)",
    re.IGNORECASE,
)

# 인증 URL 호스트 화이트리스트 — 일반 외부 URL(예: docs link) 을 device URL 로 오인하지 않게.
_AUTH_URL_HOSTS = (
    "claude.ai",
    "console.anthropic.com",
    "anthropic.com/oauth",
    "anthropic.com/auth",
)


def _looks_like_auth_url(url: str) -> bool:
    lowered = url.lower()
    return any(host in lowered for host in _AUTH_URL_HOSTS)


@dataclass
class AuthEvent:
    """미들웨어가 Unity 로 전달하는 OAuth 이벤트."""
    state: str            # "url" | "code" | "status" | "success" | "failed"
    message: str = ""
    url: Optional[str] = None
    code: Optional[str] = None


EventCallback = Callable[[AuthEvent], Awaitable[None]]


class AuthLoginRunner:
    """`claude login` subprocess 를 띄우고 출력 스트림을 파싱하여 이벤트로 변환."""

    def __init__(
        self,
        cli_path: str = "claude",
        config_dir: Optional[str] = None,
        timeout_seconds: int = 300,
    ):
        self._cli_path = cli_path
        self._config_dir = config_dir or os.environ.get("CLAUDE_CONFIG_DIR")
        self._timeout = timeout_seconds
        self._process: Optional[asyncio.subprocess.Process] = None
        self._reader_task: Optional[asyncio.Task] = None
        self._cancelled = False
        self._url_emitted = False
        self._code_emitted = False

    @property
    def is_active(self) -> bool:
        return self._process is not None and self._process.returncode is None

    async def start(self, on_event: EventCallback) -> None:
        if self.is_active:
            await on_event(AuthEvent(state="failed", message="이미 진행 중인 로그인이 있습니다"))
            return

        if not shutil.which(self._cli_path):
            await on_event(AuthEvent(state="failed", message=f"claude CLI 를 찾을 수 없습니다: {self._cli_path}"))
            return

        env = dict(os.environ)
        if self._config_dir:
            env["CLAUDE_CONFIG_DIR"] = self._config_dir
            try:
                Path(self._config_dir).mkdir(parents=True, exist_ok=True)
            except OSError as e:
                logger.warning("CLAUDE_CONFIG_DIR 생성 실패: %s (%s)", self._config_dir, e)

        # cwd 를 격리 디렉토리 안의 빈 폴더로 — 부모 cwd 의 .claude/ 가 영향 미치지 않게.
        cwd = Path(self._config_dir or os.path.expanduser("~")) / "cwd"
        try:
            cwd.mkdir(parents=True, exist_ok=True)
        except OSError:
            cwd = Path(os.path.expanduser("~"))

        self._cancelled = False
        self._url_emitted = False
        self._code_emitted = False

        try:
            self._process = await asyncio.create_subprocess_exec(
                self._cli_path, "login",
                stdin=asyncio.subprocess.PIPE,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.STDOUT,
                env=env,
                cwd=str(cwd),
                limit=1024 * 1024,
            )
        except FileNotFoundError:
            await on_event(AuthEvent(state="failed", message=f"claude CLI 실행 실패: {self._cli_path}"))
            return
        except Exception as e:  # noqa: BLE001
            await on_event(AuthEvent(state="failed", message=f"login subprocess 시작 실패: {e}"))
            return

        await on_event(AuthEvent(state="status", message="login 프로세스 시작"))
        self._reader_task = asyncio.create_task(self._read_loop(on_event))

    async def cancel(self) -> None:
        self._cancelled = True
        if self._process is not None and self._process.returncode is None:
            try:
                self._process.terminate()
            except ProcessLookupError:
                pass
        if self._reader_task is not None:
            try:
                await asyncio.wait_for(self._reader_task, timeout=3.0)
            except (asyncio.TimeoutError, Exception):  # noqa: BLE001
                pass

    async def _read_loop(self, on_event: EventCallback) -> None:
        assert self._process is not None
        accumulated_lines: list[str] = []
        try:
            async with asyncio.timeout(self._timeout):
                while True:
                    line_bytes = await self._process.stdout.readline()
                    if not line_bytes:
                        break
                    line = line_bytes.decode("utf-8", errors="replace").strip()
                    if not line:
                        continue
                    accumulated_lines.append(line)
                    logger.info("[claude login] %s", line[:200])

                    await self._handle_line(line, on_event)
        except asyncio.TimeoutError:
            await on_event(AuthEvent(state="failed", message="login 시간 초과"))
            try:
                self._process.kill()
            except ProcessLookupError:
                pass
            return
        except Exception as e:  # noqa: BLE001
            await on_event(AuthEvent(state="failed", message=f"login 출력 읽기 실패: {e}"))
            return
        finally:
            # 종료 코드 회수.
            try:
                rc = await self._process.wait()
            except Exception:  # noqa: BLE001
                rc = -1

            if self._cancelled:
                await on_event(AuthEvent(state="failed", message="사용자에 의해 취소됨"))
            elif rc == 0:
                await on_event(AuthEvent(state="success", message="인증 완료"))
            else:
                tail = "\n".join(accumulated_lines[-5:])
                await on_event(AuthEvent(
                    state="failed",
                    message=f"claude login 종료 코드 {rc}\n{tail}",
                ))
            self._process = None

    async def _handle_line(self, line: str, on_event: EventCallback) -> None:
        # 1) 인증 URL — 한 번만 발행 (중복 방지)
        if not self._url_emitted:
            urls = _URL_RE.findall(line)
            for url in urls:
                if _looks_like_auth_url(url):
                    self._url_emitted = True
                    await on_event(AuthEvent(state="url", url=url, message=line))
                    break

        # 2) device code — 한 번만 발행
        if not self._code_emitted:
            m = _CODE_RE.search(line)
            if m:
                self._code_emitted = True
                await on_event(AuthEvent(state="code", code=m.group(1), message=line))

        # 3) 성공/실패 키워드 — terminal 메시지로 노출 (실제 success/failed 는 종료 코드로 판정)
        if _SUCCESS_RE.search(line):
            await on_event(AuthEvent(state="status", message=line))
        elif _FAILURE_RE.search(line):
            await on_event(AuthEvent(state="status", message=line))
        elif self._url_emitted or self._code_emitted:
            # URL/code 발행 후의 진행 라인은 사용자에게 그대로 노출.
            await on_event(AuthEvent(state="status", message=line))
