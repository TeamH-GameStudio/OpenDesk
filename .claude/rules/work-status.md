# 작업 현황 (2026-05-17 최종 갱신)

## Agent Performance Overhaul: Hook Chain + Telemetry + Reliability (2026-05-17)

신뢰성 + 관측가능성 토대를 깔기 위한 미들웨어 hook chain 시스템. 향후 캐싱/모델라우팅/응답캐시 등은 hook 추가만으로 구현 가능.

| 영역 | 결과 |
|------|------|
| Hook Protocol | `Middleware/hooks/protocol.py` — `MessageHook` Protocol + `RequestCtx`/`UsageSnapshot`/`ErrorAction` dataclasses |
| Pipeline | `Middleware/hooks/pipeline.py` — forward 순서 lifecycle 실행, 에러 격리, 에러 결정 투표 (retry > escalate > suppress unanimous) |
| HookedProvider | `Middleware/hooks/hooked_provider.py` — ProviderBase 데코레이터. dynamic max_tool_rounds (cache hit ratio > 0.6 시 bonus) |
| Builtin Hooks | latency / cache_stats / retry (jittered exp backoff) / rate_limit (글로벌 cooldown) / telemetry_emitter |
| Telemetry 스키마 | `Middleware/PROTOCOL.md` v1 freeze (2026-05-17) — WS `telemetry` 이벤트, 모든 nested 객체 항상 emit (JsonUtility null fragility 회피) |
| Provider Wiring | `anthropic_api.py` — `final_message.usage` → `UsageSnapshot` 추출 + `max_tool_rounds` kwarg. `anthropic_cli.py` — best-effort + `telemetry_completeness=partial` |
| C# 측 | `TelemetryEvent` DTO family in `ClaudeChatProtocol.cs`, `case "telemetry":` arm in `ClaudeWebSocketClient.cs`, `IAgentTelemetryService` / `AgentTelemetryService` (R3 reactive), `MiddlewareChatService` 가 WS→service forward, `CostHudController` 가 TTFT/cache/retry 위젯 바인딩 |
| CostMonitor 통합 | `AgentTelemetryService.Ingest` 가 `CostMonitorService.ReportTokenUsage` 로 forward — single source of truth |
| Feature flag | `config.json` 의 `hooks.enabled=false` 시 raw provider.chat() 폴백. 즉시 롤백 가능 |
| Skill Body LRU | `Middleware/skill_body_cache.py` — (skill_id, mtime_ns) 키 LRU. `opendesk_skills_mcp.py.read_skill_body` 의 디스크 I/O 감소 |
| 테스트 | 84개 hook 관련 단위/통합 테스트 통과 (test_hook_pipeline, test_hooked_provider, test_latency_hook, test_cache_stats_hook, test_retry_hook, test_rate_limit_hook, test_telemetry_emitter, test_hook_builders, test_anthropic_api_usage, test_anthropic_cli_usage, test_skill_body_cache, test_hook_chain_integration) |

**롤백 절차:** `Middleware/config.json` 의 `"hooks": {"enabled": false}` 설정 → 미들웨어 재시작. C# 측은 telemetry 이벤트 무시 default 분기로 안전.

**확장 패턴:** 새 hook = `BaseHook` 상속 + `builders._BUILDERS` 에 등록 + `config.json` 의 `hooks.chain` 에 이름 추가. ResponseCacheHook / ModelRouterHook / ConversationSummaryHook 등 후속 작업 모두 이 패턴.

**Deferred (후속 작업):** 1) `AgentEquipmentManager.BuildSystemPrompt` 의 stable prefix / volatile suffix 분리 (스킬 loadout 변경 시 cache breakpoint 보존). 2) `Middleware/mcp_client.py.set_servers` 의 프로세스 재사용 (env-only 변경 시 respawn 회피). 3) 구간별 timeout (connect/first-token/tool-call) — 현재 단일 total timeout. 4) ResponseCacheHook / ModelRouterHook.

---

## Skill 인덱스 모드 + 디스켓 메타포 가역 deprecation (2026-05-14)

Claude Code 의 SKILL.md 패턴을 차용해 **본문은 지연 로드**. 시스템 프롬프트에는 인덱스만.

| 항목 | 상세 |
|------|------|
| 시스템 프롬프트 | `AgentEquipmentManager.BuildSystemPrompt()` 가 `<available-skills>` 안에 `(id, name, description)` 인덱스만 합성. 본문 합성 X |
| 본문 통로 | 미들웨어 내장 stdio MCP 서버 `Middleware/opendesk_skills_mcp.py` — `read_skill_body(skill_id)` 도구 |
| Provider 자동 등록 | `server.py._compose_mcp_config` 가 외부 MCP + 내장 OpenDesk Skills MCP 를 합쳐 provider 에 전달. CLI/API 모두 동일 |
| Payload | `set_skill_loadout` op + `SkillLoadoutPayload(agentId, skills[id,name,description,body])` |
| 본문 캐시 | `SkillLoadoutStore` 가 임시 디렉토리에 `{id}/SKILL.md` 작성 → MCP 서버에 `OPENDESK_SKILLS_ROOT` 환경변수로 전달. body 비어있으면 `~/.opendesk/skills/{id}/SKILL.md` 디스크 fallback |
| 안전 | 활성 화이트리스트(`OPENDESK_ACTIVE_SKILLS`) + 경로 트래버설 차단 |
| 디스켓 메타포 | `SkillDisketteView`, `SkillDisketteFactory`, `DisketteShelfUI`, `DiskettePrinterController` 모두 `[Obsolete]` + AgentOfficeInstaller 의 DI 등록 코멘트 아웃. 가역 보존 |
| UI 대체 | 마켓플레이스 카드 패턴 (PluginsMarketView 와 통일) |

**확장 패턴:** 새 Skill = `~/.opendesk/skills/{id}/SKILL.md` 추가 + 카탈로그 등록. `SkillDescriptor.PromptContent` 가 body 의 대체 캐시 역할.

## AI 게이트웨이 통합 + Plugin 시스템 신설 (2026-05-14)

Skill = 행동 지침 / Plugin = 외부 앱 연결점(MCP) 으로 도메인 분리. AI 백엔드는 Python 미들웨어가 단일 게이트웨이.

| 항목 | 상세 |
|------|------|
| Unity 진입점 | **`MiddlewareChatService`** 단일 (`IAiChatService` 유일 구현체) |
| Python 미들웨어 | provider 라우팅 게이트웨이. `Middleware/providers/{anthropic_cli, anthropic_api, ...}.py` 모듈로 확장 |
| MCP 클라이언트 | `Middleware/mcp_client.py` — 공식 `mcp` 패키지로 stdio MCP 서버 통합 관리. CLI/API 양쪽에서 동일 동작 |
| provider 토글 | PlayerPrefs `OpenDesk_ChatBackend` ("anthropic_cli" 기본, "anthropic_api"). 레거시 "cli"/"api" 호환 |
| 모델 | PlayerPrefs `OpenDesk_AnthropicModel` (디폴트 `claude-sonnet-4-5`) |
| 미들웨어 자동기동 | `MiddlewareLauncher`가 항상 시작 — 백엔드 분기 분기 제거 (양쪽 provider 모두 미들웨어 의존) |
| MCP 통로 | `IAiChatService.SendMcpConfig(payload)` → `set_mcp_config` op → provider 자동 적용 |
| 자격증명 | `IPluginCredentialService` — `~/AppData/.../OpenDesk/plugin-credentials/{pluginId}.json` (Base64) |
| 저장 경로 | Skill `~/.opendesk/skills/`, Plugin `~/.opendesk/plugins/{id}/` |
| Plugin 마이그레이션 | `Tools/OpenDesk/Migrate ExternalTool Skills` 메뉴 — `HasExternalTool==true` Skill SO 를 manifest.json 초안으로 변환 |
| 기존 두 서비스 | `AnthropicCliChatService`, `AnthropicApiChatService` 모두 `[Obsolete]` (가역 보존) |

**확장 패턴:** 새 AI 모델/백엔드 = `Middleware/providers/<name>.py` 추가 + Unity 측 PlayerPrefs enum 한 줄 확장. Unity 측 새 ChatService 클래스 만들지 않는다.

**Plugin 시스템 구성:**
- 도메인 모델: `OpenDesk.Core.Models.Plugins.*` (PluginDescriptor / PluginManifest / PluginCatalog / McpServerSpec / CredentialRequirement / McpConfigPayload)
- 서비스: `IPluginCatalogService` / `IAgentPluginLoadoutService` / `IPluginCredentialService` / `IMcpConfigComposer`
- UI: `Presentation/UI/Plugins/PluginsMarketView.{uxml,uss,cs}` + `PluginCredentialModal.{uxml,uss,cs}` (UI Toolkit + opendesk-tokens.uss)
- 영속: `AgentPluginLoadoutData` (`PersistedDataTable.AgentPluginLoadouts`)

## (참고) AI 백엔드 이중화 (2026-04-27) — Deprecated

위 통합 게이트웨이 도입으로 흡수. Unity 측 두 ChatService 는 `[Obsolete]` 가역 보존.

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
