"""
OpenDesk WebSocket 프로토콜

-- 미들웨어 -> Unity (5종) --

agent_state            캐릭터 FSM 트리거      state, tool
agent_message          에이전트 대화 메시지     message
agent_thinking         에이전트 추론 과정       thinking
session_list_response  세션 목록 응답          sessions[]
session_switched       세션 전환 완료          chat_history[]

-- Unity -> 미들웨어 (7종) --

chat_message      에이전트에게 메시지      agent_id, message
chat_clear        새 대화 시작            agent_id
session_list      세션 목록 요청          agent_id
session_switch    세션 전환              agent_id, session_id
session_new       새 세션 생성            agent_id
session_delete    세션 삭제              agent_id, session_id
status_request    전체 상태 요청          (없음)
"""
