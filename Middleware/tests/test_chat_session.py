"""ChatSession history 무결성 테스트.

provider 호출 도중 에러가 나도 user/assistant 교대 규칙을 깨지 않으면서
이전 turn 의 문맥(=user 발화)을 history 에 보존해야 한다.
"""

from __future__ import annotations

import pytest

from server import ChatSession


def _new_session() -> ChatSession:
    # 최소 config — provider 인스턴스가 등록된 anthropic_api 로 잡힌다.
    return ChatSession({"claude": {"maxTurns": 12}})


@pytest.mark.unit
def test_mark_user_turn_failed_appends_assistant_stub_after_user():
    session = _new_session()
    session.add_user_message("hello")

    session.mark_user_turn_failed()

    assert session.history == [
        {"role": "user", "content": "hello"},
        {"role": "assistant", "content": "[이전 응답 실패]"},
    ]


@pytest.mark.unit
def test_mark_user_turn_failed_is_noop_when_last_is_assistant():
    session = _new_session()
    session.add_user_message("hello")
    session.add_assistant_message("hi")

    session.mark_user_turn_failed()

    # 이미 짝이 맞는 상태에선 stub 을 추가하지 않는다.
    assert session.history == [
        {"role": "user", "content": "hello"},
        {"role": "assistant", "content": "hi"},
    ]


@pytest.mark.unit
def test_mark_user_turn_failed_is_noop_on_empty_history():
    session = _new_session()

    session.mark_user_turn_failed()

    assert session.history == []


@pytest.mark.unit
def test_mark_user_turn_failed_accepts_custom_stub():
    session = _new_session()
    session.add_user_message("hello")

    session.mark_user_turn_failed(stub="[중단됨]")

    assert session.history[-1] == {"role": "assistant", "content": "[중단됨]"}


@pytest.mark.unit
def test_chat_session_has_tool_journal():
    session = _new_session()
    # tool_journal 은 connection 라이프타임의 활동 로그 — history 와 분리.
    assert hasattr(session, "tool_journal")
    assert len(session.tool_journal) == 0


@pytest.mark.unit
def test_clear_also_clears_tool_journal():
    session = _new_session()
    session.tool_journal.append("write_file", {}, "")
    assert len(session.tool_journal) == 1

    session.clear()

    assert len(session.tool_journal) == 0
    assert session.history == []


@pytest.mark.unit
def test_mark_user_turn_failed_preserves_prior_turns():
    # 회귀 가드 — 이전 성공 turn 까지 함께 날려버리지 않는지.
    session = _new_session()
    session.add_user_message("turn1")
    session.add_assistant_message("response1")
    session.add_user_message("turn2-that-failed")

    session.mark_user_turn_failed()

    assert session.history == [
        {"role": "user", "content": "turn1"},
        {"role": "assistant", "content": "response1"},
        {"role": "user", "content": "turn2-that-failed"},
        {"role": "assistant", "content": "[이전 응답 실패]"},
    ]
