---
paths:
  - "**/Core/**"
  - "**/OpenClawBridge*"
  - "**/EventParser*"
  - "**/TerminalChat*"
---

# Gateway 연결 프로토콜

OpenClaw Gateway와의 WebSocket 통신 규격. Core/ 하위 파일 수정 시 반드시 참고.

## 연결

```
URL: ws://127.0.0.1:18789
헤더: Origin: http://127.0.0.1:18789
인증: 쿼리 파라미터 ?token=xxx (Bearer 헤더 아님!)
```

## 핸드셰이크

1. 서버 → `connect.challenge` (nonce + timestamp)
2. 클라이언트 → `connect` 요청:
```json
{
  "type": "req",
  "id": "opendesk-connect-{uuid}",
  "method": "connect",
  "params": {
    "minProtocol": 3, "maxProtocol": 3,
    "client": {"id": "openclaw-control-ui", "version": "1.0.0", "platform": "win32", "mode": "ui"},
    "role": "operator",
    "scopes": ["operator.admin", "operator.read", "operator.write", "operator.approvals"],
    "auth": {"token": "{gateway_token}"}
  }
}
```
3. 서버 → `hello-ok` (protocol, scopes 포함)
4. 하트비트: `health` RPC 30초 간격

## 스코프 주의사항

토큰 인증 + 디바이스 서명 없으면 스코프가 강제 비워짐. 현재 개발용 설정으로 우회:
```json
{
  "gateway.controlUi.allowInsecureAuth": true,
  "gateway.controlUi.dangerouslyDisableDeviceAuth": true,
  "gateway.controlUi.dangerouslyAllowHostHeaderOriginFallback": true
}
```
V1에서 Ed25519 디바이스 서명으로 교체 필요.

| 스코프 | 접근 메서드 |
|--------|------------|
| operator.read | chat.history, sessions.list, config.get |
| operator.write | chat.send, chat.abort, sessions.create |
| operator.admin | agents.create, config.*, sessions.delete |

## 채팅 프로토콜

### 전송 (chat.send)
```json
{
  "type": "req",
  "id": "chat-{uuid}",
  "method": "chat.send",
  "params": {
    "sessionKey": "default",
    "message": "사용자 메시지",
    "idempotencyKey": "{uuid}"
  }
}
```

### 수신 (스트리밍)
```json
// delta (부분 응답)
{"type":"event","event":"chat","payload":{"runId":"...","sessionKey":"default","seq":0,"state":"delta","message":{"role":"assistant","content":[{"type":"text","text":"부분..."}]}}}

// final (최종 응답)
{"type":"event","event":"chat","payload":{"...","state":"final","message":{"role":"assistant","content":[{"type":"text","text":"완전한 응답"}]}}}
```

### 이벤트 파싱 (EventParserService)
- `event:"chat"` + `state:"delta"` → AgentActionType.ChatDelta + Message 텍스트
- `event:"chat"` + `state:"final"` → AgentActionType.ChatFinal + Message 텍스트
- `event:"agent"` → AgentLifecycle (state값으로 Thinking/Planning/Executing 등 매핑)
- `type:"res"` → RPC 응답, 무시

## NativeWebSocket 주의사항

- `Connect()`는 블로킹 (내부 Receive 루프 포함) → fire-and-forget + DispatchLoop 즉시 시작
- 핸드셰이크 완료 폴링 대기 (최대 10초)
- `Dispose()` 시 이벤트 핸들러 4개 해제 + 소켓 Close + `_disposed` 멱등 플래그
