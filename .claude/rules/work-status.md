# 작업 현황 (2026-04-08 최종 갱신)

작업 정리 폴더: `C:/Users/user/Desktop/OpenDesk/작업정리/`
마일스톤 문서: `C:/Users/user/Desktop/OpenDesk/new기획정리/Unity_클라이언트_마일스톤.md`

## 방향 (04-07 확정)

- **Claude CLI 완전 폐기** — Anthropic SDK 미들웨어로 전환
- **미들웨어/SDK 개발 안 함** — 승문 담당
- **Unity = WebSocket 클라이언트만** — 새 프로토콜 대응
- 에이전트 3개: researcher, writer, analyst
- 기획서: `C:/Users/user/Desktop/OpenDesk/new기획정리/`

## Phase 진행 상황 (04-07~08)

| Phase | 내용 | 상태 |
|-------|------|------|
| 1 | DTO 교체 (Unity→MW 7종, MW→Unity 6종) | 완료 |
| 2 | WebSocket 클라이언트 교체 (송수신 + autoConnect) | 완료 |
| 3 | ChatPanel 리팩토링 (agentId 동적, StringBuilder) | 완료 |
| 4 | FSM 매핑 + ForceState 연동 | 완료 |
| 5 | 세션 관리 UI (세션목록↔채팅 전환) | 완료 |
| 6 | 버블 UI (HUD 타원형 + thinking/working 표시) | 완료 |
| 7 | 레거시 정리 (MiddlewareLauncher, IClaudeService Obsolete) | 완료 |
| 8 | 통합 테스트 (6개 시나리오) | 미착수 (승문 대기) |

## 메인 씬: AgentProtocolTestScene

- Office_35 + NavMesh 자동 베이크 + 가구 레이어 자동 설정
- Cinemachine 2 VCam (Overview ↔ AgentCam, EaseInOut 0.8초)
- 좌측: 테스트 버튼 (에이전트3, FSM7, 서버5, Mock6) — 항상 표시
- 우측: 세션 목록 → 채팅 뷰 전환 — 에이전트 클릭 시 활성화
- FSM 테스트 → 실제 3D ForceState() 연동 (의자 앉기/타이핑/Cheering 등)
- Mock 모드: 미들웨어 없이 전체 흐름 시뮬레이션

## 애니메이션 (11 State)

0=Idle(Businessman), 1=Typing, 2=Walk, 3=Cheering(Businessman), 4=Thinking(Drinking)
5=Sleeping, 6=StandToSit, 7=SitToStand, 8=SitToType, 9=TypeToSit, 10=Error(FemaleStandingPose)

## 빌드 순서 (씬 재구성 시)

1. Tools > OpenDesk > Build Agent Animator Controller
2. Tools > OpenDesk > Build Agent Prefabs
3. Tools > OpenDesk > Build Agent Protocol Test Scene

## 알려진 이슈

| 이슈 | 심각도 | 비고 |
|------|--------|------|
| 버블 UI 실제 표시 확인 | HIGH | HUD+씬 재빌드 순서 필수 |
| 앉아서 Complete 모션 없음 | LOW | 클립 추가 시 수정 |
| PlayerPrefs 세션 완전 제거 | LOW | 서버 세션 검증 후 |
| 미들웨어 통합 테스트 | HIGH | 승문 서버 준비 후 |
