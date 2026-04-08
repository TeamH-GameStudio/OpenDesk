# OpenDesk — AI Agent Desktop for Non-developers

Unity 기반 AI 에이전트 관리 데스크톱 앱. 비전공자가 에이전트를 생성하고 대화하는 올인원 환경.

## 기술 스택

- **Unity 2022.3+** / C# / .NET Standard 2.1
- **DI**: VContainer (LifetimeScope 계층: CoreInstaller > OnboardingInstaller / OfficeInstaller)
- **비동기**: UniTask (async/await). Task/async void 사용 금지
- **WebSocket**: NativeWebSocket (Origin 헤더 지원)
- **AI 미들웨어**: Python (websockets) — Claude CLI subprocess + NDJSON 파싱
- **폰트**: NotoSansKR (특수문자/이모지 사용 금지 — [OK][X][!] 등 ASCII 대체)

## 프로젝트 경로

| 항목 | 경로 |
|------|------|
| Unity 프로젝트 | `C:/Users/user/Documents/GitHub/OpenDesk` |
| Python 미들웨어 | `Middleware/` (server.py, claude_bridge.py, formatter.py) |
| 작업 정리 | `C:/Users/user/Desktop/OpenDesk/작업정리/` (날짜별 폴더) |
| 스킬 시스템 기획 | `C:/Users/user/Desktop/Claude_Code_Skills/OpenDesk_스킬시스템_기획.md` |

## 씬 구조

| 씬 | 역할 |
|----|------|
| AgentProtocolTestScene | 미들웨어 통합 테스트 (멀티에이전트 프로토콜) |
| OfficeScene | 에이전트 3D 시각화 + 세션/채팅 + 대시보드 |
| AgentCreationScene | 에이전트 제작 6단계 위저드 |
| TestChattingScene | (레거시) 단일 에이전트 채팅 테스트용 |

## 완료된 기능 (2026-03-29 기준)

1. **온보딩 자동화** — Node.js/WSL2/OpenClaw 자동설치, UAC 처리, 재부팅 복귀
2. **Gateway 연결** — 핸드셰이크(connect RPC) + 토큰 인증 + 하트비트(30s)
3. **채팅 파이프라인 2종**
   - Gateway 직접: chat.send RPC → 스트리밍 delta/final → 말풍선 UI
   - Python 미들웨어: Claude CLI subprocess → WebSocket → 말풍선 UI + 상태표시
4. **에이전트 제작 위저드** — 6단계 (이름/역할/모델/말투/아바타/확인)
5. **3D 시각화** — 큐브 소환 + World Space HUD (상태바 13종 + Billboard)
6. **세션 관리** — PlayerPrefs CRUD, 세션 리스트, 에이전트 클릭 → 세션 오픈
7. **대시보드** — 콘솔 로그, 비용 HUD, 에이전틱 루프 시각화
8. **보안 감사** — 4도메인 점검 서비스

## 다음 작업

- 스킬 시스템: ISkillManagerService → 에이전트 스킬 슬롯 + 마켓 UI
- system prompt 합성 (기본 프로필 + 장착 스킬 → API system prompt)
- Inspector 바인딩 잔여분 (Office 씬 일부 패널)
- Ed25519 디바이스 서명 (Gateway 보안 설정 교체)
- 3D 캐릭터 모델/애니메이션 교체 (현재 큐브 플레이스홀더)

## 코딩 규칙

- VContainer DI 필수 — new로 서비스 생성 금지, LifetimeScope에 등록
- UniTask + CancellationToken 전달 필수 — destroyCancellationToken 활용
- null 안전성 — ?. / ?? 적극 사용, SerializeField는 null 체크
- GC 최소화 — SetText() 사용, string concat 대신 StringBuilder
- 유니코드 특수문자 금지 — NotoSansKR 미지원 글리프 방지
- 에디터 스크립트 — Inspector 자동 바인딩 패턴 유지 (SerializedObject + FindProperty)
- 프로덕션 품질 — 엣지케이스, 에러 처리, 재시도 로직 항상 고려

## 작업 스타일

- 작업이 오래 걸릴 경우 (2분 이상), 2분 주기로 현재 진행 현황을 간결하게 공유할 것
- 현황 공유 시 완료된 항목, 현재 작업 중인 항목, 남은 항목을 명시

## 작업 정리 프로세스

작업 완료 후 정리 요청 시 `C:/Users/user/Desktop/OpenDesk/작업정리/YYYY-MM-DD/` 에 MD 파일로 정리.
새 세션 시작 시 가장 최근 폴더부터 읽고 맥락 숙지 후 작업 시작.
