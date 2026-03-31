# 작업 현황 (2026-04-01 최종 갱신)

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
