# OpenDesk WebSocket Protocol

WebSocket endpoint: `ws://localhost:8765`

---

## Unity -> Middleware (요청)

### 1. chat_message — 채팅 전송
```json
{
  "type": "chat_message",
  "agent_id": "researcher",
  "message": "최근 AI 트렌드 조사해줘"
}
```

### 2. status_request — 전체 에이전트 상태 조회
```json
{
  "type": "status_request"
}
```

### 3. session_list — 세션 목록 조회
```json
{
  "type": "session_list",
  "agent_id": "researcher"
}
```

### 4. session_new — 새 세션 생성
```json
{
  "type": "session_new",
  "agent_id": "researcher"
}
```

### 5. session_switch — 세션 전환
```json
{
  "type": "session_switch",
  "agent_id": "researcher",
  "session_id": "s_5acaec60"
}
```

### 6. session_delete — 세션 삭제
```json
{
  "type": "session_delete",
  "agent_id": "researcher",
  "session_id": "s_5acaec60"
}
```

### 7. chat_clear — 대화 초기화 (= session_new)
```json
{
  "type": "chat_clear",
  "agent_id": "researcher"
}
```

---

## Middleware -> Unity (응답/이벤트)

### 1. agent_state — 에이전트 상태 변화
```json
{
  "type": "agent_state",
  "agent_id": "researcher",
  "role": "리서처",
  "session_id": "s_5acaec60",
  "timestamp": 1775643612.03,
  "state": "thinking"
}
```

**state 값:**
| state | 의미 |
|-------|------|
| `idle` | 대기 중 |
| `thinking` | AI가 생각 중 |
| `working` | 도구 실행 중 (tool 필드 포함) |
| `complete` | 응답 완료 (idle 직전에 1회 발생) |
| `error` | 에러 발생 (error, message 필드 포함) |

**working 상태일 때 추가 필드:**
```json
{
  "type": "agent_state",
  "agent_id": "researcher",
  "role": "리서처",
  "state": "working",
  "tool": "web_search",
  "tool_input": "{\"query\": \"AI 트렌드 2026\"}"
}
```

**error 상태일 때 추가 필드:**
```json
{
  "type": "agent_state",
  "agent_id": "researcher",
  "role": "리서처",
  "state": "error",
  "error": "api_error",
  "message": "API 속도 제한. 잠시 후 다시 시도해주세요."
}
```

### 2. agent_thinking — AI 추론 과정 (스트리밍)
```json
{
  "type": "agent_thinking",
  "agent_id": "researcher",
  "role": "리서처",
  "session_id": "s_5acaec60",
  "timestamp": 1775643612.03,
  "thinking": "사용자가 AI 트렌드에 대해 물어보고 있다. 웹 검색을 해서..."
}
```
> thinking은 스냅샷 방식 — 매번 전체 내용이 옴 (delta 아님)

### 3. agent_delta — 응답 텍스트 스트리밍
```json
{
  "type": "agent_delta",
  "agent_id": "researcher",
  "role": "리서처",
  "session_id": "s_5acaec60",
  "timestamp": 1775643613.10,
  "text": "최근"
}
```
> text는 delta — 이전 delta에 이어붙여서 표시

### 4. agent_message — 최종 응답 (전체 텍스트)
```json
{
  "type": "agent_message",
  "agent_id": "researcher",
  "role": "리서처",
  "session_id": "s_5acaec60",
  "timestamp": 1775643615.50,
  "message": "최근 AI 트렌드를 조사한 결과..."
}
```
> markdown_to_tmp 포매팅 적용됨 (TMP 리치텍스트)

### 5. agent_action — 캐릭터 액션 (애니메이션 트리거)
```json
{
  "type": "agent_action",
  "agent_id": "researcher",
  "action": "dancing",
  "timestamp": 1775643615.50
}
```

**action 값:**
| action | 의미 |
|--------|------|
| `idle` | 평소 대기 |
| `typing` | 작업/문서 작성 중 |
| `walk` | 이동 |
| `cheering` | 기쁨, 축하, 성공 |
| `sitting` | 앉아서 대화 |
| `drinking` | 음료 마시기, 여유 |
| `dancing` | 신남, 파티 |

> agent_message 직후에 발생. 에이전트가 응답 맥락에 맞는 액션을 자동 선택.

### 6. session_list_response — 세션 목록
```json
{
  "type": "session_list_response",
  "agent_id": "researcher",
  "current_session_id": "s_5acaec60",
  "sessions": [
    {
      "session_id": "s_5acaec60",
      "title": "AI 트렌드 조사",
      "updated_at": 1775643612.03,
      "message_count": 6
    },
    {
      "session_id": "s_06900fd5",
      "title": "보고서 작성",
      "updated_at": 1775640000.00,
      "message_count": 4
    }
  ]
}
```

### 6. session_switched — 세션 전환 완료
```json
{
  "type": "session_switched",
  "agent_id": "researcher",
  "session_id": "s_06900fd5",
  "chat_history": [
    { "role": "user", "text": "보고서 써줘" },
    { "role": "assistant", "text": "네, 보고서를 작성하겠습니다..." }
  ]
}
```

---

## 메시지 흐름 예시

### 채팅 1회 (도구 미사용)
```
Unity -> { type: "chat_message", agent_id: "researcher", message: "안녕!" }

  <- { type: "agent_state",    state: "thinking" }
  <- { type: "agent_thinking", thinking: "사용자가 인사..." }
  <- { type: "agent_delta",    text: "안녕" }
  <- { type: "agent_delta",    text: "하세요" }
  <- { type: "agent_delta",    text: "!" }
  <- { type: "agent_message",  message: "안녕하세요!" }
  <- { type: "agent_action",   action: "idle" }
  <- { type: "agent_state",    state: "complete" }
  <- { type: "agent_state",    state: "idle" }
```

### 채팅 1회 (도구 사용)
```
Unity -> { type: "chat_message", agent_id: "researcher", message: "AI 뉴스 검색해줘" }

  <- { type: "agent_state",    state: "thinking" }
  <- { type: "agent_thinking", thinking: "웹 검색이 필요하다..." }
  <- { type: "agent_delta",    text: "검색해 볼게요!" }
  <- { type: "agent_message",  message: "검색해 볼게요!" }
  <- { type: "agent_state",    state: "working", tool: "web_search", tool_input: "{\"query\":\"AI news 2026\"}" }
  <- { type: "agent_state",    state: "thinking" }
  <- { type: "agent_thinking", thinking: "검색 결과를 정리하면..." }
  <- { type: "agent_delta",    text: "검색 결과..." }
  <- { type: "agent_message",  message: "검색 결과를 정리하면..." }
  <- { type: "agent_action",   action: "cheering" }
  <- { type: "agent_state",    state: "complete" }
  <- { type: "agent_state",    state: "idle" }
```

### 전체 상태 조회
```
Unity -> { type: "status_request" }

  <- { type: "agent_state",         agent_id: "researcher", state: "idle", ... }
  <- { type: "session_list_response", agent_id: "researcher", sessions: [...] }
  <- { type: "agent_state",         agent_id: "writer",     state: "idle", ... }
  <- { type: "session_list_response", agent_id: "writer",     sessions: [...] }
  <- { type: "agent_state",         agent_id: "analyst",    state: "idle", ... }
  <- { type: "session_list_response", agent_id: "analyst",    sessions: [...] }
```

---

## 에이전트 목록

| agent_id | role | 설명 |
|----------|------|------|
| `researcher` | 리서처 | 웹 검색, 정보 수집/분석 |
| `writer` | 라이터 | 문서 작성/편집 |
| `analyst` | 분석가 | 데이터 분석 |

## 참고

- 모든 메시지는 JSON (UTF-8, ensure_ascii=False)
- broadcast 방식 — 연결된 모든 클라이언트에 동일 메시지 전송
- agent_id로 어떤 에이전트의 메시지인지 구분
- timestamp는 Unix epoch (float)
