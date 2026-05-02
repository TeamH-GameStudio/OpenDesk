# 작업 현황 (2026-04-27 최종 갱신)

## AI 백엔드 이중화 — IAiChatService Facade (2026-04-27)

추후 다른 AI 모델(OpenAI, Gemini 등) 통합 가능하도록 인터페이스 일반화 + 두 가지 Anthropic 백엔드 구현.

| 항목 | 상세 |
|------|------|
| 인터페이스 | `IClaudeService` → **`IAiChatService`** (모델/제공자 비종속) |
| 구현체 1 (CLI) | `ClaudeService` → **`AnthropicCliChatService`** (Python 미들웨어 + Claude CLI subprocess, MCP 지원) |
| 구현체 2 (API) | **`AnthropicApiChatService`** 신규 — HttpClient + SSE 스트리밍, 외부 프로세스 불필요 |
| 백엔드 토글 | PlayerPrefs `OpenDesk_ChatBackend` ("cli" \| "api"), 디폴트 "cli" |
| 토글 UI | 에디터 메뉴 `OpenDesk > AI Backend > Use CLI/API/Show current` |
| 미들웨어 자동기동 | `MiddlewareLauncher`가 백엔드 키 읽어 API 모드일 땐 스킵 |
| 키 소스 | `IApiKeyVaultService.GetKeyAsync("anthropic")` |
| 모델 | PlayerPrefs `OpenDesk_AnthropicModel` (디폴트 `claude-sonnet-4-5`) |
| ChatPanelController | `ClaudeWebSocketClient` 직접 의존 → `IAiChatService` DI로 교체 |
| DiskettePrinterController | 동일하게 IAiChatService로 마이그레이션 |

**확장 패턴:** 새 백엔드는 `IAiChatService` 구현 → `AgentOfficeInstaller`의 토글 분기에 추가만 하면 됨.

---

## OpenClaw 레거시 Deprecated 처리 (2026-04-27)

OpenClaw → Anthropic API 전환 후 잔존 코드를 가역적으로 비활성화. **삭제 없음, 모두 코멘트/Obsolete 처리**.

| 작업 | 결과 |
|------|------|
| `[Obsolete]` 부착 | OpenClaw 7개 (`OpenClawBridgeService`, `IOpenClawBridgeService`, `OpenClawInstaller`, `OpenClawDetector`, `IOpenClawInstaller`, `IOpenClawDetector`, `EventParserService`, `IEventParserService`) |
| DI 등록 비활성 | `CoreInstaller`(Bridge/EventParser), `OnboardingInstaller`(Detector/Installer) 코멘트 아웃 |
| `AppBootstrapper` 경량화 | Bridge 의존 제거, CostMonitor만 활성. 거대 주석으로 원본 보존 |
| `OnboardingService` stub | 600줄 본문 → 인자 없는 stub 생성자 + ReadyToEnter 즉시 전환. 원본은 거대 주석 |
| `SecurityAuditService` | `RunExternalAuditAsync` 거대 주석 처리, `~/.openclaw` → `~/.opendesk` |
| Skill 경로 마이그레이션 | `OpenDeskPaths` 신설, `SkillMarketService`/`ChannelService`/`SkillEntry`가 `~/.opendesk/skills/` 사용 |
| UI 컨트롤러 6개 정리 | `[Inject] IOpenClawBridgeService/IOpenClawInstaller` 코멘트 + 사용처 비활성 (`TopBar`, `TerminalChat`, `Settings`, `OfficeWizard`, `OnboardingUI`, `OnboardingAppUI`) |
| PlayerPrefs 키 | `OpenDesk_GatewayUrl`/`OpenDesk_GatewayToken`/`OpenDesk_MockMode` 쓰기 라인 코멘트 |
| Tests 비활성화 | `OnboardingServiceTests`는 `#if OPENCLAW_LEGACY` 가드로 컴파일 제외 |
| **보존** | Python 미들웨어 전체, `IClaudeService` 계열, `AgentEquipment`/`Pipeline` 계열, `AgentStateService`/`SubAgentService`, `ClawRouterService`(보류) |

**규칙:** 새 코드는 OpenClaw 타입을 참조하지 말 것. `[Obsolete]` 경고가 사용처를 가시화한다.

**검증 필요 (사용자 수동):** Unity 에디터에서 컴파일 → AgentOfficeScene/OnboardingScene/TestChattingScene Play로 동작 확인.

---

# 작업 현황 (2026-04-01 갱신)

작업 정리 폴더: `C:/Users/user/Desktop/OpenDesk/작업정리/`

## 방향 전환 (03-31)

- OpenClaw 의존 제거 → Claude CLI/API 단일 백엔드
- 핵심: 공간 파이프라인, 스킬 디스켓 메타포, 커스텀 크래프팅, 바톤터치, 외부 도구 연동
- 기획서: `C:/Users/user/Desktop/OpenDesk/기획/2026-03-31_재정립/`

## 2주 스프린트 (04-01 ~ 04-14)

### Week 1 완료 (04-01)

| Day | 내용 | 상태 |
|-----|------|------|
| 1 | SkillDiskette SO + 프리셋 5개 | 완료 |
| 2 | IClaudeService Facade | 완료 |
| 3 | AgentEquipmentManager + System Prompt 연동 | 완료 |
| 4 | 디스켓 드래그&드롭 (New Input System) | 완료 |
| 5 | 3D Printer + 크래프팅 | 완료 |
| 6 | In-box + Out-box + PipelineManager | 완료 |
| - | UI 개선: DisketteShelfUI + 크래프팅 토글 | 완료 |

### Week 2 예정

| Day | 내용 | 상태 |
|-----|------|------|
| 8-9 | 토큰 입력 UI + Notion MCP 연동 | 미착수 |
| 10-11 | 전체 E2E 테스트 (6개 시나리오) | 미착수 |
| 12-13 | VFX (크래프팅/장착/홀로그램) | 미착수 |
| 14 | 안정화 + 데모 준비 | 미착수 |

## 동작 확인된 플로우 (04-01 기준)

1. 선반 UI 카드 드래그 → 에이전트 드롭 → system prompt 자동 반영 → 채팅 응답 변화
2. 크래프팅 토글 → 프롬프트 입력 → Claude 응답 → 선반에 카드 추가
3. In-box 파일 투입 → 채팅 시 파일 컨텍스트 포함
4. 응답 완료 → Out-box 자동 저장 (TMP 태그 제거)
5. 디스켓 해제 → 선반 복귀 → 다른 디스켓 장착

## 해결된 이슈 (04-01)

| 이슈 | 해결 |
|------|------|
| OnMouseDown UI 차단 | Update + Physics.Raycast 직접 처리 |
| 레거시 Input 에러 | New Input System (Mouse.current) 전환 |
| DI 미주입 NullRef | AgentOfficeInstaller에 등록 |
| 크래프팅 JSON 파싱 실패 | TMP 태그 제거 후 JSON 추출 |
| Out-box TMP 태그 출력 | StripTmpTags() 적용 |
| Mask 투명 Image 문제 | RectMask2D로 변경 |

## 알려진 이슈 (미해결)

| 이슈 | 심각도 | 비고 |
|------|--------|------|
| Claude CLI 응답 시간 초과 (120초) | HIGH | timeout 300초 조정 예정 (Day 14) |
| EquipmentSlotUI 씬 바인딩 미완 | MEDIUM | Patcher 추가 또는 수동 연결 필요 |
| Notion MCP 미연동 | MEDIUM | Day 8-9에서 구현 |
