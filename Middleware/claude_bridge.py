"""
Claude CLI 서브프로세스 관리
claude -p "prompt" --output-format stream-json --verbose 실행 후
stdout NDJSON 줄 단위 비동기 읽기
"""

import asyncio
import json
import shutil
import logging

logger = logging.getLogger("claude_bridge")


class ClaudeBridge:
    """Claude CLI와의 통신을 담당"""

    def __init__(self, cli_path: str = "claude", timeout: int = 300):
        self._cli_path = cli_path
        self._timeout = timeout
        self._active_process: asyncio.subprocess.Process | None = None
        self._heartbeat_task: asyncio.Task | None = None

    async def check_cli_available(self) -> tuple[bool, str]:
        """Claude CLI 존재 및 인증 확인. (ok, model_or_error) 반환"""
        # CLI 존재 확인
        resolved = shutil.which(self._cli_path)
        if not resolved:
            return False, "cli_not_found"

        try:
            proc = await asyncio.create_subprocess_exec(
                self._cli_path, "--version",
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=10)
            version = stdout.decode().strip()
            logger.info(f"Claude CLI found: {version}")
            return True, version
        except Exception as e:
            return False, str(e)

    async def send_message(
        self,
        prompt: str,
        on_delta,      # async callable(text: str)
        on_final,      # async callable(text: str, cost: float)
        on_error,      # async callable(message: str, code: str)
        on_status=None, # async callable(text: str) — 상태 변화 알림
    ):
        """
        Claude CLI에 메시지를 보내고 스트리밍 응답을 콜백으로 전달.

        on_delta: 부분 텍스트 청크
        on_final: 최종 완성 응답 + 비용
        on_error: 에러
        """
        if self._active_process and self._active_process.returncode is None:
            await on_error("이전 응답을 기다리는 중입니다", "server_busy")
            return

        try:
            self._active_process = await asyncio.create_subprocess_exec(
                self._cli_path,
                "-p", prompt,
                "--output-format", "stream-json",
                "--verbose",
                "--tools", "",
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                limit=1024 * 1024,  # 1MB — 긴 응답 대응
            )

            accumulated_text = ""
            last_delta_text = ""

            # 60초 간격 heartbeat 시작
            self._heartbeat_task = asyncio.create_task(
                self._heartbeat_loop(on_status)
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
                            logger.debug(f"Non-JSON line: {line_str[:100]}")
                            continue

                        msg_type = data.get("type", "")

                        if msg_type == "assistant":
                            # AI 응답 메시지 — content[].text / thinking 추출
                            message = data.get("message", {})
                            content_list = message.get("content", [])
                            for block in content_list:
                                block_type = block.get("type", "")

                                if block_type == "thinking":
                                    # Claude가 사고 중
                                    if on_status:
                                        await on_status("사고 중...")

                                elif block_type == "tool_use":
                                    # 도구 호출 중
                                    tool_name = block.get("name", "도구")
                                    if on_status:
                                        await on_status(f"도구 호출: {tool_name}")

                                elif block_type == "tool_result":
                                    # 도구 결과 수신
                                    if on_status:
                                        await on_status("도구 결과 수신")

                                elif block_type == "text":
                                    text = block.get("text", "")
                                    if text:
                                        # 텍스트 응답 시작 → 상태 갱신
                                        if on_status and not accumulated_text:
                                            await on_status("응답 중...")

                                        # delta = 이번에 새로 온 부분
                                        # stream-json에서는 assistant 메시지가
                                        # 누적 텍스트로 올 수 있으므로 diff 계산
                                        if text.startswith(accumulated_text):
                                            new_part = text[len(accumulated_text):]
                                        else:
                                            new_part = text

                                        if new_part:
                                            accumulated_text = text
                                            last_delta_text = new_part
                                            await on_delta(new_part)

                        elif msg_type == "result":
                            # 최종 결과
                            self._cancel_heartbeat()
                            result_text = data.get("result", accumulated_text)
                            cost = data.get("total_cost_usd", 0.0)
                            await on_final(result_text, cost)
                            return

                        elif msg_type == "system":
                            # init 메시지 — 모델 정보 등 (무시)
                            subtype = data.get("subtype", "")
                            if subtype == "init":
                                model = data.get("model", "unknown")
                                logger.info(f"CLI session init: model={model}")

            except TimeoutError:
                self._cancel_heartbeat()
                self.kill_active_process()
                await on_error("응답 시간 초과", "cli_timeout")
                return

            # stdout이 끝났는데 result가 안 온 경우
            stderr_data = await self._active_process.stderr.read()
            stderr_text = stderr_data.decode("utf-8", errors="replace").strip()

            if self._active_process.returncode != 0 and stderr_text:
                # 에러 분류
                code = "cli_error"
                if "auth" in stderr_text.lower() or "login" in stderr_text.lower():
                    code = "cli_auth_error"
                elif "rate" in stderr_text.lower() or "limit" in stderr_text.lower():
                    code = "rate_limit"
                await on_error(stderr_text[:300], code)
            elif accumulated_text:
                # result 없이 끝났지만 텍스트는 있는 경우
                await on_final(accumulated_text, 0.0)
            else:
                await on_error("Claude CLI에서 응답이 없습니다", "cli_error")

        except FileNotFoundError:
            await on_error(
                f"Claude CLI를 찾을 수 없습니다: {self._cli_path}",
                "cli_not_found"
            )
        except Exception as e:
            await on_error(str(e), "cli_error")
        finally:
            self._cancel_heartbeat()
            self._active_process = None

    def kill_active_process(self):
        """진행 중인 CLI 프로세스 강제 종료"""
        if self._active_process and self._active_process.returncode is None:
            try:
                self._active_process.kill()
                logger.info("Active CLI process killed")
            except ProcessLookupError:
                pass

    async def _heartbeat_loop(self, on_status):
        """60초 간격으로 상태 보고 — CLI 실행 중 Unity에 alive 알림"""
        if on_status is None:
            return
        elapsed = 0
        interval = 60
        try:
            while True:
                await asyncio.sleep(interval)
                elapsed += interval
                await on_status(f"처리 중... (경과: {elapsed}초)")
                logger.debug(f"Heartbeat sent: {elapsed}s elapsed")
        except asyncio.CancelledError:
            pass

    def _cancel_heartbeat(self):
        """heartbeat Task 정리"""
        if self._heartbeat_task and not self._heartbeat_task.done():
            self._heartbeat_task.cancel()
            self._heartbeat_task = None
