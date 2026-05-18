"""Cron 스케줄러 — APScheduler(AsyncIOScheduler) 기반 셸 명령 예약.

영속: ~/.opendesk/cron.json (재시작 시 재로드).
실행은 task_queue 에 위임해서 stdout/상태/영속을 일관 처리.
"""

from __future__ import annotations

import json
import logging
import os
import time
import uuid
from dataclasses import dataclass, asdict
from typing import Awaitable, Callable, Optional

logger = logging.getLogger("cron_manager")


@dataclass
class CronJob:
    cron_id: str
    agent_id: str
    name: str
    schedule: str  # cron expression
    command: str
    enabled: bool = True
    created_at: float = 0.0
    last_run: Optional[float] = None
    next_run: Optional[float] = None


class CronManager:
    def __init__(
        self,
        config_path: str = "~/.opendesk/cron.json",
        runner: Optional[Callable[[CronJob], Awaitable[None]]] = None,
        on_event: Optional[Callable[[dict], Awaitable[None]]] = None,
    ):
        self._config_path = os.path.expanduser(config_path)
        os.makedirs(os.path.dirname(self._config_path), exist_ok=True)
        self._jobs: dict[str, CronJob] = {}
        self._runner = runner
        self._on_event = on_event
        self._scheduler = None  # AsyncIOScheduler — 첫 add 때 lazy 생성
        self._started = False

    # ── 영속 ────────────────────────────────────────────

    def _persist(self) -> None:
        try:
            payload = {"jobs": [asdict(j) for j in self._jobs.values()]}
            with open(self._config_path, "w", encoding="utf-8") as f:
                json.dump(payload, f, ensure_ascii=False, indent=2)
        except Exception as e:
            logger.warning(f"cron persist failed: {e}")

    def load(self) -> None:
        if not os.path.isfile(self._config_path):
            return
        try:
            with open(self._config_path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            logger.warning(f"cron load failed: {e}")
            return
        for raw in data.get("jobs", []):
            try:
                job = CronJob(**raw)
            except TypeError:
                continue
            self._jobs[job.cron_id] = job

    # ── 스케줄러 lifecycle ───────────────────────────────

    def _ensure_scheduler(self):
        if self._scheduler is None:
            try:
                from apscheduler.schedulers.asyncio import AsyncIOScheduler  # type: ignore
            except ImportError as e:
                raise RuntimeError(
                    "apscheduler is not installed. Run pip install apscheduler>=3.10.0"
                ) from e
            self._scheduler = AsyncIOScheduler()
        return self._scheduler

    def start(self) -> None:
        if self._started:
            return
        sched = self._ensure_scheduler()
        for job in self._jobs.values():
            if job.enabled:
                self._add_to_scheduler(job)
        sched.start()
        self._started = True

    def shutdown(self) -> None:
        if self._scheduler and self._started:
            try:
                self._scheduler.shutdown(wait=False)
            except Exception:
                pass
        self._started = False

    def _add_to_scheduler(self, job: CronJob) -> None:
        from apscheduler.triggers.cron import CronTrigger  # type: ignore
        try:
            trigger = CronTrigger.from_crontab(job.schedule)
        except Exception as e:
            logger.warning(f"invalid cron expression for {job.cron_id}: {e}")
            return
        sched = self._ensure_scheduler()
        sched.add_job(
            self._fire,
            trigger=trigger,
            args=[job.cron_id],
            id=job.cron_id,
            replace_existing=True,
            misfire_grace_time=60,
        )
        try:
            sj = sched.get_job(job.cron_id)
            if sj and sj.next_run_time:
                job.next_run = sj.next_run_time.timestamp()
        except Exception:
            pass

    async def _fire(self, cron_id: str) -> None:
        job = self._jobs.get(cron_id)
        if not job or not job.enabled:
            return
        job.last_run = time.time()
        try:
            sj = self._scheduler.get_job(cron_id) if self._scheduler else None
            if sj and sj.next_run_time:
                job.next_run = sj.next_run_time.timestamp()
        except Exception:
            pass
        self._persist()
        if self._on_event:
            try:
                await self._on_event({
                    "type": "cron_state",
                    "cron_id": job.cron_id,
                    "name": job.name,
                    "schedule": job.schedule,
                    "enabled": job.enabled,
                    "last_run": job.last_run,
                    "next_run": job.next_run,
                })
            except Exception:
                pass
        if self._runner:
            try:
                await self._runner(job)
            except Exception as e:
                logger.warning(f"cron runner failed for {cron_id}: {e}")

    # ── public API ──────────────────────────────────────

    def create(
        self,
        agent_id: str,
        name: str,
        schedule: str,
        command: str,
    ) -> dict:
        # 사전 유효성 검사 — invalid crontab 거부
        try:
            from apscheduler.triggers.cron import CronTrigger  # type: ignore
            CronTrigger.from_crontab(schedule)
        except Exception as e:
            return {"error": f"invalid cron expression: {e}"}

        cron_id = f"cron_{uuid.uuid4().hex[:10]}"
        job = CronJob(
            cron_id=cron_id,
            agent_id=agent_id,
            name=name,
            schedule=schedule,
            command=command,
            enabled=True,
            created_at=time.time(),
        )
        self._jobs[cron_id] = job
        if self._started:
            self._add_to_scheduler(job)
        self._persist()
        return asdict(job)

    def list(self) -> list[dict]:
        return [asdict(j) for j in sorted(
            self._jobs.values(), key=lambda j: j.created_at, reverse=True
        )]

    def delete(self, cron_id: str) -> bool:
        if cron_id not in self._jobs:
            return False
        del self._jobs[cron_id]
        if self._scheduler:
            try:
                self._scheduler.remove_job(cron_id)
            except Exception:
                pass
        self._persist()
        return True

    def get(self, cron_id: str) -> Optional[dict]:
        job = self._jobs.get(cron_id)
        return asdict(job) if job else None
