# OpenDesk WebSocket Protocol

WebSocket endpoint: `ws://localhost:8765`

---

## Unity -> Middleware (요청)

### 1. chat_message — 채팅 전송
```json
{
  "type": "chat_message",
  "agent_id": "<agent_id>",
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
  "agent_id": "<agent_id>"
}
```

### 4. session_new — 새 세션 생성
```json
{
  "type": "session_new",
  "agent_id": "<agent_id>"
}
```

### 5. session_switch — 세션 전환
```json
{
  "type": "session_switch",
  "agent_id": "<agent_id>",
  "session_id": "s_5acaec60"
}
```

### 6. session_delete — 세션 삭제
```json
{
  "type": "session_delete",
  "agent_id": "<agent_id>",
  "session_id": "s_5acaec60"
}
```

### 7. chat_clear — 대화 초기화 (= session_new)
```json
{
  "type": "chat_clear",
  "agent_id": "<agent_id>"
}
```

### 8. tool_user_response — `ask_user` 도구에 대한 사용자 응답
```json
{
  "type": "tool_user_response",
  "tool_use_id": "ask_a1b2c3d4e5f6g7h8",
  "response": "자유 입력 텍스트 (옵션)",
  "selected": ["옵션 라벨 1", "옵션 라벨 2"]
}
```
> `tool_user_ask` 이벤트의 `tool_use_id` 를 그대로 회신.
> `selected` 는 multi_select 가 false 면 0~1개, true 면 0~N개.
> 사용자가 자유 입력만 제공하면 `selected` 는 빈 배열.

### 9. task_control — 백그라운드 작업 제어
```json
{
  "type": "task_control",
  "action": "stop",
  "task_id": "task_a1b2c3d4e5f6"
}
```
| action | 의미 |
|--------|------|
| `stop` | 실행 중인 작업을 중단 |
| `update` | `patch` 필드의 description 등을 갱신 (필드는 화이트리스트) |

---

## Middleware -> Unity (응답/이벤트)

### 1. agent_state — 에이전트 상태 변화
```json
{
  "type": "agent_state",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
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
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "state": "working",
  "tool": "web_search",
  "tool_input": "{\"query\": \"AI 트렌드 2026\"}"
}
```

**error 상태일 때 추가 필드:**
```json
{
  "type": "agent_state",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "state": "error",
  "error": "api_error",
  "message": "API 속도 제한. 잠시 후 다시 시도해주세요."
}
```

### 2. agent_thinking — AI 추론 과정 (스트리밍)
```json
{
  "type": "agent_thinking",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
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
  "agent_id": "<agent_id>",
  "role": "<역할명>",
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
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "session_id": "s_5acaec60",
  "timestamp": 1775643615.50,
  "message": "최근 AI 트렌드를 조사한 결과..."
}
```
> markdown_to_tmp 포매팅 적용됨 (TMP 리치텍스트)

### 5a. text_delta — 입모양/타이핑용 토큰 스트리밍
```json
{
  "type": "text_delta",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "session_id": "s_5acaec60",
  "timestamp": 1775643613.10,
  "text": "안녕"
}
```
> `agent_delta` 와 병행 송출. `agent_delta` 는 채팅 UI 누적용, `text_delta` 는 캐릭터 입모양/타이핑 효과용 lightweight 채널.
> 한 번의 발화에서 `talking_start` → `text_delta` * N → `talking_stop` 시퀀스로 묶임.

### 5b. talking_start — 발화 시작 신호
```json
{
  "type": "talking_start",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "session_id": "s_5acaec60",
  "timestamp": 1775643613.05
}
```
> 첫 `text_delta` 직전에 1회 emit. 캐릭터가 입을 벌리거나 발화 애니메이션을 시작하는 트리거.

### 5c. talking_stop — 발화 종료 신호
```json
{
  "type": "talking_stop",
  "agent_id": "<agent_id>",
  "role": "<역할명>",
  "session_id": "s_5acaec60",
  "timestamp": 1775643615.50,
  "reason": "complete"
}
```
**reason 값:**
| reason | 의미 |
|--------|------|
| `complete` | 정상 종료 (스트림 EOF, 모든 텍스트 수신 완료) |
| `error` | provider/runner 예외로 중단 |
| `interrupted` | 클라이언트 disconnect / kill 등 외부 중단 |

> `talking_start` 가 emit 된 경우에만 짝으로 emit. 중간에 tool_use 로 분기되면 그 라운드의 텍스트 발화가 끝난 시점에 `complete` 로 1회 닫고, 다음 텍스트 라운드에서 새 `talking_start` 가 다시 열림.

### 7. agent_action — 캐릭터 액션 (애니메이션 트리거)
```json
{
  "type": "agent_action",
  "agent_id": "<agent_id>",
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
  "agent_id": "<agent_id>",
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

### 8. tool_user_ask — `ask_user` 도구 호출 (인터랙티브)
```json
{
  "type": "tool_user_ask",
  "agent_id": "<agent_id>",
  "tool_use_id": "ask_a1b2c3d4e5f6g7h8",
  "question": "어떤 접근을 사용할까요?",
  "header": "Approach",
  "multi_select": false,
  "options": [
    { "label": "OAuth", "description": "표준이지만 설정이 복잡합니다" },
    { "label": "JWT",   "description": "단순하지만 회수가 어렵습니다" }
  ]
}
```
> 클라이언트는 ChatPanel 인라인 카드로 렌더 후 사용자가 응답하면 `tool_user_response` 송신.
> 응답 전까지 해당 라운드는 blocked (5분 타임아웃).

### 9. sub_agent_spawned / sub_agent_completed / sub_agent_failed — 서브에이전트 생명주기
```json
{ "type": "sub_agent_spawned",   "agent_id": "<agent_id>", "sub_agent_id": "sub_a1b2c3d4", "task_name": "Summarize PDF", "subagent_type": "<role>", "timestamp": 1775643612.03 }
{ "type": "sub_agent_completed", "agent_id": "<agent_id>", "sub_agent_id": "sub_a1b2c3d4", "task_name": "Summarize PDF", "timestamp": 1775643618.50 }
{ "type": "sub_agent_failed",    "agent_id": "<agent_id>", "sub_agent_id": "sub_a1b2c3d4", "error": "rate_limit",        "timestamp": 1775643618.50 }
```
> Unity `SubAgentService.OnSubAgentSpawned/Completed/Failed` 로 매핑.

### 10. task_state — 백그라운드 작업 상태 변화
```json
{
  "type": "task_state",
  "agent_id": "<agent_id>",
  "task_id": "task_a1b2c3d4e5f6",
  "status": "running",
  "description": "Index local docs",
  "exit_code": null,
  "timestamp": 1775643612.03
}
```
| status | 의미 |
|--------|------|
| `pending`   | 생성됨, 아직 실행 전 |
| `running`   | 실행 중 |
| `completed` | exit 0 으로 정상 종료 |
| `failed`    | exit != 0 또는 예외로 실패 |
| `stopped`   | 사용자가 `task_control` 로 중단 |

### 11. cron_state — 예약 작업 실행 시점 변화
```json
{
  "type": "cron_state",
  "cron_id": "cron_a1b2c3d4e5",
  "name": "daily-summary",
  "schedule": "0 9 * * *",
  "enabled": true,
  "last_run": 1775634000.00,
  "next_run": 1775720400.00
}
```

### 6. session_switched — 세션 전환 완료
```json
{
  "type": "session_switched",
  "agent_id": "<agent_id>",
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
Unity -> { type: "chat_message", agent_id: "<agent_id>", message: "안녕!" }

  <- { type: "agent_state",    state: "thinking" }
  <- { type: "agent_thinking", thinking: "사용자가 인사..." }
  <- { type: "talking_start"  }
  <- { type: "text_delta",     text: "안녕" }
  <- { type: "agent_delta",    text: "안녕" }
  <- { type: "text_delta",     text: "하세요" }
  <- { type: "agent_delta",    text: "하세요" }
  <- { type: "text_delta",     text: "!" }
  <- { type: "agent_delta",    text: "!" }
  <- { type: "talking_stop",   reason: "complete" }
  <- { type: "agent_message",  message: "안녕하세요!" }
  <- { type: "agent_action",   action: "idle" }
  <- { type: "agent_state",    state: "complete" }
  <- { type: "agent_state",    state: "idle" }
```

### 채팅 1회 (도구 사용)
```
Unity -> { type: "chat_message", agent_id: "<agent_id>", message: "AI 뉴스 검색해줘" }

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

  <- { type: "agent_state",           agent_id: "<agent_a>", state: "idle", ... }
  <- { type: "session_list_response", agent_id: "<agent_a>", sessions: [...] }
  <- { type: "agent_state",           agent_id: "<agent_b>", state: "idle", ... }
  <- { type: "session_list_response", agent_id: "<agent_b>", sessions: [...] }
  ...
```
> 현재 연결된 모든 사용자 정의 에이전트에 대해 `agent_state` + `session_list_response` 가 한 쌍씩 broadcast 된다.

---

## 에이전트 식별

- `agent_id` 는 위저드에서 생성되는 사용자 정의 ID. 고정 프리셋은 없음.
- `role` 은 위저드의 역할 선택 결과를 사람이 읽는 한국어 라벨로 변환한 값 (예: "기획자", "개발자", "디자이너", ...).
- 동일 에이전트는 전 메시지에서 같은 `agent_id` 로 식별된다.

---

## Telemetry (스키마 v1 — 2026-05-17 freeze)

middleware → client 방향의 **추가 이벤트** (기존 chat protocol 변경 없음, backward-compat).
모든 hook chain 측정값을 모아 발행. Unity 측 `IAgentTelemetryService` 가 소비.

이벤트 종류 (`event` 필드):
- `first_token` — 첫 텍스트 토큰 도착. TTFT 만 즉시 emit.
- `request_complete` — 정상 종료. 전체 metric 포함.
- `error` — 예외 발생. `has_error=true`, `error` 객체 채움.
- `retry` — (예정) retry 결정 시. 현 sprint 에선 미사용.

### Required fields

`type`, `event`, `request_id`, `provider`, `model`, `timestamp` 는 항상 존재.
다른 모든 필드는 누락 시 0 또는 빈 객체로 채워진다 (Unity JsonUtility nested null fragility 회피).

### Full payload schema

```json
{
  "type": "telemetry",
  "event": "request_complete",
  "request_id": "req_8f3a...",
  "agent_id": "agent_abc",
  "session_id": "",
  "provider": "anthropic_api",
  "model": "claude-sonnet-4-5",
  "timestamp": 1747449600.123,
  "latency": {
    "ttft_ms": 412,
    "total_ms": 9180,
    "tool_rounds_ms": [1240, 3380, 4220]
  },
  "tokens": {
    "input": 14820,
    "output": 612,
    "cache_creation_input": 8400,
    "cache_read_input": 6020
  },
  "cache": {
    "available": true,
    "hit_ratio": 0.41,
    "creation_tokens": 8400,
    "read_tokens": 6020
  },
  "reliability": {
    "retry_count": 0,
    "rate_limit_hits": 0,
    "max_tool_rounds": 8,
    "tool_rounds_used": 3,
    "stop_reason": "complete"
  },
  "cost_estimate_usd": 0.0438,
  "telemetry_completeness": "full",
  "has_error": false,
  "error": {}
}
```

### Field types

| Field | Type | Notes |
|-------|------|-------|
| `type` | string | 항상 `"telemetry"` |
| `event` | string | `first_token` / `request_complete` / `error` / `retry` |
| `request_id` | string | 1 회 요청의 고유 ID |
| `agent_id` | string | 빈 문자열 허용 (글로벌 telemetry) |
| `session_id` | string | 빈 문자열 허용 |
| `provider` | string | `anthropic_api` / `anthropic_cli` / ... |
| `model` | string | 모델 식별자 |
| `timestamp` | float | Unix epoch seconds |
| `latency.ttft_ms` | int | 첫 토큰까지 ms |
| `latency.total_ms` | int | 총 응답 ms |
| `latency.tool_rounds_ms` | int[] | round 별 ms |
| `tokens.*` | int | Anthropic SDK usage 의 token count |
| `cache.available` | bool | provider 가 캐시 통계 제공 가능 여부 |
| `cache.hit_ratio` | float | 0.0 ~ 1.0 |
| `reliability.retry_count` | int | 이번 요청에서 발생한 retry 횟수 |
| `reliability.rate_limit_hits` | int | 429 발생 횟수 |
| `reliability.tool_rounds_used` | int | 실제 사용한 tool round 수 |
| `reliability.stop_reason` | string | `complete` / `error` / `interrupted` / `tool_loop_exceeded` |
| `cost_estimate_usd` | float | best-effort 추정치 (정확한 청구는 콘솔 참조) |
| `telemetry_completeness` | string | `full` / `partial` (CLI provider 등은 partial) |
| `has_error` | bool | `error` 객체가 의미를 가지는지 |
| `error.message` | string | 에러 메시지 |
| `error.code` | string | 에러 타입 이름 또는 코드 |
| `error.recoverable` | bool | 복구 가능 여부 |

### Feature flag

```json
{
  "hooks": {
    "enabled": true,
    "chain": ["retry", "rate_limit", "latency", "cache_stats", "telemetry_emitter"]
  }
}
```

`hooks.enabled=false` 시 hook chain 자체가 우회되어 `telemetry` 이벤트가 발행되지 않는다.
C# 클라이언트는 이 경우 unknown event type 으로 ignore 하므로 안전.

---

## 참고

- 모든 메시지는 JSON (UTF-8, ensure_ascii=False)
- broadcast 방식 — 연결된 모든 클라이언트에 동일 메시지 전송
- agent_id로 어떤 에이전트의 메시지인지 구분
- timestamp는 Unix epoch (float)
