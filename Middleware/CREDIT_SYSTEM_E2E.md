# Credit System (Hybrid Routing) — E2E 검증 체크리스트

본 문서는 `opendesk-credit-system-prompts.md` 가 제안한 크레딧 시스템을 OpenDesk 에
하이브리드 라우팅 형태로 도입한 결과를 검증하기 위한 절차다.
서버는 `OPENDESK_ROUTING_URL=embedded` (기본) 면 미들웨어 프로세스 내부의 mock 으로 동작.

## 1. 미들웨어 단위 (자동)

```bash
cd Middleware
source .venv/bin/activate
python -m pytest tests/test_credit_policy.py tests/test_mock_routing_server.py tests/test_opendesk_routed.py -v
```

기대: **35 passed**.

검증 항목:
- [x] credit_policy: 가격 계산 (haiku/sonnet/opus), 1M context 가산, tier lookup, 음수 입력 거부
- [x] mock_routing_server: 활성화 + 2대 디바이스 캡, 라우팅 휴리스틱, hold/settle/refund, 10% overage cap, double-settle 차단
- [x] opendesk_routed: 정상 사이클 (route → hold → stream → settle), 잔액 부족 거부, 스트림 예외 환불, 미인증 가드, complexity hint 전달

## 2. 미들웨어 import + provider 등록 (자동)

```bash
cd Middleware && source .venv/bin/activate
python -c "import server; print('providers:', __import__('providers').list_providers())"
```

기대 출력: `providers: ['anthropic_cli', 'anthropic_api', 'opendesk_routed']`

## 3. Unity ↔ 미들웨어 통합 (수동)

### 3.1 사전 준비

1. Unity 에디터 열기 (Unity 2022.3+)
2. `Middleware/` 디렉토리에 `.env` 가 있다면 `OPENDESK_ROUTING_URL=embedded` 가 명시되어 있는지 확인 (없으면 기본값 embedded)
3. `Middleware/server.py` 가 import 가능한지 확인 (위 2번 통과)

### 3.2 라이선스 활성화 플로우

1. `OnboardingScene` Play
2. Welcome → Plan → Auth (Google stub 통과) → **License 화면 도달 확인**
3. 라이선스 키 `TEST-1000` 입력, 디바이스 이름 자동 채워짐
4. "활성화" 클릭
   - **NOTE**: Onboarding 씬에 ClaudeWebSocketClient 가 없으면 "미들웨어 연결 대기 중..." 표시 → "건너뛰기 (BYOK)" 로 우회 가능
5. 활성화 성공 시 PlayerPrefs 검증:
   ```
   OpenDesk_License_Jwt = "mock-jwt-..."
   OpenDesk_License_UserId = "user-test"
   OpenDesk_License_PlanTier = "pro"
   ```

### 3.3 Hybrid Routing 동작

1. `AgentOfficeScene` 진입 (에이전트 생성 → 로딩 → 오피스)
2. Inspector 또는 코드로 `PlayerPrefs.SetString("OpenDesk_ChatBackend", "opendesk_routed")` 설정
3. 채팅 송신
4. WebSocket frame 모니터링 (Edit > Project Settings > Network Profiler 또는 디버그 로그):
   ```
   ← credit.routing       (model, tier, estimatedCredits)
   ← credit.balance       (held > 0, 잔액 감소)
   ← text_delta * N
   ← talking_stop
   ← credit.settled       (actualCredits, in/out tokens)
   ← credit.balance       (held = 0, settle 후 잔액)
   ← final
   ```
5. TopBar `CreditBalanceBadge` 가 settle 후 갱신되는지 확인 (UIDocument 가 씬에 배치된 경우)

### 3.4 BYOK 회귀 검증

1. PlayerPrefs `OpenDesk_ChatBackend=anthropic_api` 로 토글
2. 동일 채팅 송신
3. 검증: `credit.*` 이벤트 **0개** 발생, 기존 `final` 만 도착
4. CostMonitor 의 `OnFinal(cost)` 이 기존대로 작동

### 3.5 잔액 부족 시나리오

1. 미들웨어 Python REPL 또는 별도 스크립트로:
   ```python
   from mock_routing_server import get_embedded_server
   import asyncio
   asyncio.run(get_embedded_server().admin_set_balance("user-test", 0))
   ```
2. Unity 측 `opendesk_routed` 모드에서 채팅 송신
3. 검증:
   - `credit.insufficient` 이벤트 수신
   - `InsufficientCreditsModal` 표시
   - inner Anthropic SDK 호출 **발생 안 함** (서버 로그 확인)

### 3.6 디바이스 한도 시나리오

1. 미들웨어 재시작 (mock 상태 휘발)
2. Unity OnboardingScene → 다른 두 디바이스 ID 시뮬레이션으로 활성화 2회
3. 3번째 활성화 시도 → `device_limit_reached` 응답
4. 검증: UI 메시지 "이 라이선스는 이미 최대 디바이스 수에 도달했어요" 표시

### 3.7 회귀: Skill / MCP / Plugin

1. Skill loadout 적용된 상태에서 `opendesk_routed` 모드로 채팅
2. 시스템 프롬프트에 `<available-skills>` 인덱스 포함 확인
3. MCP plugin 도구 호출 정상 (자격증명은 로컬 유지 — 클라우드로 안 나감)

## 4. 알려진 한계 / 다음 단계

- **Phase 2**: opendesk-server FastAPI 실서버 구축 (별도 레포)
- Lemon Squeezy 웹훅 + 결제 UI
- Ed25519 디바이스 서명 (현재는 SHA256 fingerprint)
- Redis Lua 스크립트 (mock 인메모리 → Redis cluster)
- OpenAI/Gemini 라우팅 확장

## 5. 트러블슈팅

| 증상 | 원인 / 해결 |
|------|---|
| `credit.routing` 이 안 옴 | provider 가 opendesk_routed 인지 확인 (PlayerPrefs) |
| `license_not_activated` 에러 | `set_auth` 가 안 보내짐. ClaudeWebSocketClient 가 OnboardingScene 에 있는지 확인 |
| 활성화 시 `ws_not_connected` | Onboarding 씬에 ClaudeWebSocketClient + MiddlewareLauncher 가 없음. AgentOfficeScene 진입 후 settings 에서 활성화 |
| 잔액이 음수 / 동기 안 됨 | `OpenDesk_Credits_BalanceCache` PlayerPrefs 초기화 후 재로그인 |
