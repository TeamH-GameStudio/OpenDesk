"""백그라운드 셸 작업 큐 — asyncio.create_subprocess_shell 기반.

각 작업은 ~/.opendesk/tasks/{task_id}/{stdout.log, status.json} 에 영속.
실행은 BashTool 의 allowlist 검증을 재사용해서 안전하지 않은 명령은 거부.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import time
import uuid
from dataclasses import dataclass, field, asdict
from typing import Awaitable, Callable, Optional

logger = logging.getLogger("task_queue")


# 상태값
STATUS_PENDING = "pending"
STATUS_RUNNING = "running"
STATUS_COMPLETED = "completed"
STATUS_FAILED = "failed"
STATUS_STOPPED = "stopped"


@dataclass
class BackgroundTask:
    task_id: str
    agent_id: str
    command: str
    description: str
    workspace: str
    status: str = STATUS_PENDING
    exit_code: Optional[int] = None
    created_at: float = field(default_factory=time.time)
    started_at: Optional[float] = None
    finished_at: Optional[float] = None
    stdout_path: str = ""
    status_path: str = ""

    def to_public(self) -> dict:
        return {
            "task_id": self.task_id,
            "agent_id": self.agent_id,
            "command": self.command,
            "description": self.description,
            "status": self.status,
            "exit_code": self.exit_code,
            "created_at": self.created_at,
            "started_at": self.started_at,
            "finished_at": self.finished_at,
        }


class TaskQueueManager:
    def __init__(
        self,
        base_dir: str = "~/.opendesk/tasks",
        command_validator: Optional[Callable[[str], Optional[str]]] = None,
        on_event: Optional[Callable[[dict], Awaitable[None]]] = None,
        max_stdout_chars: int = 200_000,
    ):
        self._base_dir = os.path.expanduser(base_dir)
        os.makedirs(self._base_dir, exist_ok=True)
        self._tasks: dict[str, BackgroundTask] = {}
        self._asyncio_tasks: dict[str, asyncio.Task] = {}
        self._command_validator = command_validator
        self._on_event = on_event
        self._max_stdout_chars = max_stdout_chars
        self._load_persisted()

    # ── 영속 ────────────────────────────────────────────

    def _task_dir(self, task_id: str) -> str:
        return os.path.join(self._base_dir, task_id)

    def _load_persisted(self) -> None:
        """미들웨어 시작 시 호출. running 상태였던 항목은 orphaned → failed 로 마킹."""
        if not os.path.isdir(self._base_dir):
            return
        for entry in os.listdir(self._base_dir):
            status_path = os.path.join(self._base_dir, entry, "status.json")
            if not os.path.isfile(status_path):
                continue
            try:
                with open(status_path, "r", encoding="utf-8") as f:
                    data = json.load(f)
                task = BackgroundTask(**data)
            except Exception as e:
                logger.warning(f"Failed to load task {entry}: {e}")
                continue
            if task.status == STATUS_RUNNING:
                task.status = STATUS_FAILED
                task.finished_at = time.time()
                self._persist(task)
                logger.info(f"Marked orphaned task {task.task_id} as failed.")
            self._tasks[task.task_id] = task

    def _persist(self, task: BackgroundTask) -> None:
        try:
            os.makedirs(self._task_dir(task.task_id), exist_ok=True)
            with open(task.status_path, "w", encoding="utf-8") as f:
                json.dump(asdict(task), f, ensure_ascii=False, indent=2)
        except Exception as e:
            logger.warning(f"Persist failed for {task.task_id}: {e}")

    # ── public API ──────────────────────────────────────

    def create(
        self,
        agent_id: str,
        command: str,
        description: str,
        workspace: str,
    ) -> dict:
        if self._command_validator:
            err = self._command_validator(command)
            if err:
                return {"error": err}

        task_id = f"task_{uuid.uuid4().hex[:12]}"
        task_dir = self._task_dir(task_id)
        os.makedirs(task_dir, exist_ok=True)

        task = BackgroundTask(
            task_id=task_id,
            agent_id=agent_id,
            command=command,
            description=description,
            workspace=os.path.realpath(os.path.expanduser(workspace)),
            stdout_path=os.path.join(task_dir, "stdout.log"),
            status_path=os.path.join(task_dir, "status.json"),
        )
        self._tasks[task_id] = task
        self._persist(task)

        # subprocess 실행을 백그라운드 태스크로
        coro = self._run_task(task)
        self._asyncio_tasks[task_id] = asyncio.create_task(coro)

        return task.to_public()

    def get(self, task_id: str) -> Optional[dict]:
        task = self._tasks.get(task_id)
        return task.to_public() if task else None

    def list(self, agent_id: Optional[str] = None) -> list[dict]:
        items = self._tasks.values()
        if agent_id:
            items = [t for t in items if t.agent_id == agent_id]
        return [t.to_public() for t in sorted(items, key=lambda t: t.created_at, reverse=True)]

    def update(self, task_id: str, **patch) -> Optional[dict]:
        task = self._tasks.get(task_id)
        if not task:
            return None
        # 화이트리스트 — 사용자가 임의 필드 덮어쓰지 못하게.
        for key in ("description",):
            if key in patch:
                setattr(task, key, patch[key])
        self._persist(task)
        return task.to_public()

    def stop(self, task_id: str) -> Optional[dict]:
        task = self._tasks.get(task_id)
        if not task:
            return None
        if task.status not in (STATUS_PENDING, STATUS_RUNNING):
            return task.to_public()
        asyncio_task = self._asyncio_tasks.get(task_id)
        if asyncio_task and not asyncio_task.done():
            asyncio_task.cancel()
        task.status = STATUS_STOPPED
        task.finished_at = time.time()
        self._persist(task)
        return task.to_public()

    def output(self, task_id: str, since: int = 0, max_chars: int = 20_000) -> dict:
        task = self._tasks.get(task_id)
        if not task:
            return {"error": "task not found"}
        try:
            with open(task.stdout_path, "r", encoding="utf-8", errors="replace") as f:
                f.seek(since)
                chunk = f.read(max_chars)
                next_offset = f.tell()
        except FileNotFoundError:
            chunk = ""
            next_offset = since
        return {
            "task_id": task_id,
            "status": task.status,
            "stdout": chunk,
            "since": since,
            "next_offset": next_offset,
        }

    async def shutdown(self) -> None:
        for tid, atask in list(self._asyncio_tasks.items()):
            if not atask.done():
                atask.cancel()
        # 모두 종료 대기 (최대 5초)
        if self._asyncio_tasks:
            await asyncio.gather(
                *(t for t in self._asyncio_tasks.values() if not t.done()),
                return_exceptions=True,
            )

    # ── 내부: 백그라운드 실행 ───────────────────────────

    async def _emit(self, task: BackgroundTask) -> None:
        if self._on_event:
            try:
                await self._on_event({
                    "type": "task_state",
                    "agent_id": task.agent_id,
                    "task_id": task.task_id,
                    "status": task.status,
                    "description": task.description,
                    "exit_code": task.exit_code,
                    "timestamp": time.time(),
                })
            except Exception as e:
                logger.warning(f"task_state emit failed: {e}")

    async def _run_task(self, task: BackgroundTask) -> None:
        task.status = STATUS_RUNNING
        task.started_at = time.time()
        self._persist(task)
        await self._emit(task)

        proc: Optional[asyncio.subprocess.Process] = None
        try:
            workspace = task.workspace
            os.makedirs(workspace, exist_ok=True)

            with open(task.stdout_path, "a", encoding="utf-8") as logf:
                proc = await asyncio.create_subprocess_shell(
                    task.command,
                    stdout=asyncio.subprocess.PIPE,
                    stderr=asyncio.subprocess.STDOUT,
                    cwd=workspace,
                    env={**os.environ, "HOME": workspace},
                )

                written = 0
                assert proc.stdout is not None
                while True:
                    line = await proc.stdout.readline()
                    if not line:
                        break
                    text = line.decode("utf-8", errors="replace")
                    logf.write(text)
                    logf.flush()
                    written += len(text)
                    if written >= self._max_stdout_chars:
                        logf.write("\n[truncated — output exceeded limit]\n")
                        break

                exit_code = await proc.wait()
                task.exit_code = exit_code
                task.status = STATUS_COMPLETED if exit_code == 0 else STATUS_FAILED

        except asyncio.CancelledError:
            if proc and proc.returncode is None:
                try:
                    proc.kill()
                except ProcessLookupError:
                    pass
            task.status = STATUS_STOPPED
            raise
        except Exception as e:
            logger.exception(f"Task {task.task_id} crashed")
            task.status = STATUS_FAILED
            try:
                with open(task.stdout_path, "a", encoding="utf-8") as logf:
                    logf.write(f"\n[error] {e}\n")
            except Exception:
                pass
        finally:
            task.finished_at = time.time()
            self._persist(task)
            await self._emit(task)
            self._asyncio_tasks.pop(task.task_id, None)
