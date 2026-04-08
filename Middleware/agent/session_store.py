"""
에이전트별 대화 세션을 파일로 관리.

~/.opendesk/sessions/
  researcher/
    s_abc123.json
    _current.txt      <- 현재 활성 세션 ID
  writer/
  analyst/
"""

import json
import os
import time
import uuid
from typing import Optional


class SessionStore:
    def __init__(self, base_dir: str = "~/.opendesk/sessions"):
        self._base_dir = os.path.expanduser(base_dir)

    def _agent_dir(self, agent_id: str) -> str:
        d = os.path.join(self._base_dir, agent_id)
        os.makedirs(d, exist_ok=True)
        return d

    def _session_path(self, agent_id: str, session_id: str) -> str:
        return os.path.join(self._agent_dir(agent_id), f"{session_id}.json")

    def _current_path(self, agent_id: str) -> str:
        return os.path.join(self._agent_dir(agent_id), "_current.txt")

    def create_session(self, agent_id: str, title: str = "") -> dict:
        session_id = f"s_{uuid.uuid4().hex[:8]}"
        meta = {
            "session_id": session_id,
            "agent_id": agent_id,
            "title": title or f"새 대화 {time.strftime('%m/%d %H:%M')}",
            "created_at": time.time(),
            "updated_at": time.time(),
            "message_count": 0,
            "messages": [],
        }
        self._save(agent_id, session_id, meta)
        self.set_current(agent_id, session_id)
        return meta

    def load_session(self, agent_id: str, session_id: str) -> Optional[dict]:
        path = self._session_path(agent_id, session_id)
        if not os.path.exists(path):
            return None
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    def save_messages(
        self, agent_id: str, session_id: str, messages: list, title: str = ""
    ):
        existing = self.load_session(agent_id, session_id) or {}
        existing.update(
            {
                "session_id": session_id,
                "agent_id": agent_id,
                "updated_at": time.time(),
                "message_count": len(messages),
                "messages": messages,
            }
        )
        if not existing.get("title") or existing["title"].startswith("새 대화"):
            for msg in messages:
                if msg.get("role") == "user" and isinstance(msg.get("content"), str):
                    existing["title"] = msg["content"][:30]
                    break
        if title:
            existing["title"] = title
        self._save(agent_id, session_id, existing)

    def list_sessions(self, agent_id: str) -> list:
        d = self._agent_dir(agent_id)
        sessions = []
        for f in os.listdir(d):
            if f.startswith("s_") and f.endswith(".json"):
                with open(os.path.join(d, f), "r", encoding="utf-8") as fp:
                    data = json.load(fp)
                    sessions.append(
                        {
                            "session_id": data["session_id"],
                            "title": data.get("title", ""),
                            "updated_at": data.get("updated_at", 0),
                            "message_count": data.get("message_count", 0),
                        }
                    )
        sessions.sort(key=lambda x: x["updated_at"], reverse=True)
        return sessions

    def delete_session(self, agent_id: str, session_id: str):
        path = self._session_path(agent_id, session_id)
        if os.path.exists(path):
            os.remove(path)

    def get_current(self, agent_id: str) -> Optional[str]:
        path = self._current_path(agent_id)
        if os.path.exists(path):
            with open(path, "r") as f:
                return f.read().strip()
        return None

    def set_current(self, agent_id: str, session_id: str):
        with open(self._current_path(agent_id), "w") as f:
            f.write(session_id)

    def _save(self, agent_id: str, session_id: str, data: dict):
        path = self._session_path(agent_id, session_id)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
