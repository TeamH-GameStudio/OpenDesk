"""
OpenDesk WebSocket 프로토콜

-- 미들웨어 -> Unity (8종) --

agent_state            캐릭터 FSM 트리거      state, tool
agent_message          에이전트 대화 메시지     message
agent_thinking         에이전트 추론 과정       thinking
agent_delta            응답 텍스트 누적용       text
text_delta             입모양/타이핑용 토큰     text         (lightweight)
talking_start          발화 시작 신호          (없음)
talking_stop           발화 종료 신호          reason
session_list_response  세션 목록 응답          sessions[]
session_switched       세션 전환 완료          chat_history[]
agent_action           캐릭터 액션 트리거       action

-- Unity -> 미들웨어 (7종) --

chat_message      에이전트에게 메시지      agent_id, message
chat_clear        새 대화 시작            agent_id
session_list      세션 목록 요청          agent_id
session_switch    세션 전환              agent_id, session_id
session_new       새 세션 생성            agent_id
session_delete    세션 삭제              agent_id, session_id
status_request    전체 상태 요청          (없음)

직렬화 헬퍼는 dict 빌더로 제공. 기존 코드는 dict literal 을 그대로 사용해도 무방하지만,
새 코드에서는 헬퍼를 통해 키 이름과 필드 누락을 방지한다.
"""

from __future__ import annotations

import time
from typing import Literal, Optional

# ── outbound 메시지 타입 상수 ──────────────────────────────────

TYPE_AGENT_STATE = "agent_state"
TYPE_AGENT_MESSAGE = "agent_message"
TYPE_AGENT_THINKING = "agent_thinking"
TYPE_AGENT_DELTA = "agent_delta"
TYPE_AGENT_ACTION = "agent_action"
TYPE_TEXT_DELTA = "text_delta"
TYPE_TALKING_START = "talking_start"
TYPE_TALKING_STOP = "talking_stop"
TYPE_SESSION_LIST_RESPONSE = "session_list_response"
TYPE_SESSION_SWITCHED = "session_switched"

TalkingStopReason = Literal["complete", "error", "interrupted"]


# ── 발화/스트리밍 이벤트 빌더 ──────────────────────────────────

def make_text_delta(
    *,
    agent_id: str,
    role: str,
    session_id: Optional[str],
    text: str,
    timestamp: Optional[float] = None,
) -> dict:
    """캐릭터 입모양/타이핑 효과용 lightweight delta. agent_delta 와 짝으로 송출."""
    return {
        "type": TYPE_TEXT_DELTA,
        "agent_id": agent_id,
        "role": role,
        "session_id": session_id,
        "timestamp": timestamp if timestamp is not None else time.time(),
        "text": text,
    }


def make_talking_start(
    *,
    agent_id: str,
    role: str,
    session_id: Optional[str],
    timestamp: Optional[float] = None,
) -> dict:
    """첫 text_delta 직전에 1회 emit. 캐릭터 발화 애니메이션 트리거."""
    return {
        "type": TYPE_TALKING_START,
        "agent_id": agent_id,
        "role": role,
        "session_id": session_id,
        "timestamp": timestamp if timestamp is not None else time.time(),
    }


def make_talking_stop(
    *,
    agent_id: str,
    role: str,
    session_id: Optional[str],
    reason: TalkingStopReason,
    timestamp: Optional[float] = None,
) -> dict:
    """talking_start 가 emit 된 경우에만 짝으로 emit. reason 으로 종료 사유 구분."""
    if reason not in ("complete", "error", "interrupted"):
        raise ValueError(f"invalid talking_stop reason: {reason!r}")
    return {
        "type": TYPE_TALKING_STOP,
        "agent_id": agent_id,
        "role": role,
        "session_id": session_id,
        "timestamp": timestamp if timestamp is not None else time.time(),
        "reason": reason,
    }
