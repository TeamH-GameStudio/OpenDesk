# 작업 현황 (2026-03-31 최종 갱신)

작업 정리 폴더: `C:/Users/user/Desktop/OpenDesk/작업정리/`

## 마일스톤 완성도

| 마일스톤 | 완성도 | 핵심 내용 |
|----------|--------|-----------|
| M0 기반 | 100% | Gateway 포트 통일, Bridge 재연결, 하트비트, 이벤트 파서 |
| M1 환경설치 | 95% | Node.js/WSL2/OpenClaw 자동설치, UAC, 재부팅 복귀 |
| M2 키관리 | 90% | 14개 제공업체, 라우팅 모드, Base64 암호화 |
| M3 대시보드 | 90% | 콘솔 로그, 비용 HUD, 에이전틱 루프 그래프 |
| M4 채널/스킬 | 85% | 5채널 모델, 10개 내장 스킬 카탈로그 |
| M5 보안 | 85% | 4도메인 감사, CLI 연동 |
| 온보딩 | 100% | 실제 Windows PC 테스트 완료 (03-24) |
| Gateway 연결 | 100% | 핸드셰이크 + 토큰 인증 + 스코프 (03-25) |
| 채팅 (Gateway) | 100% | chat.send RPC + 스트리밍 delta/final (03-27~28) |
| 채팅 (미들웨어) | 100% | Python + Claude CLI + 상태표시 (03-28) |
| 에이전트 위저드 | 100% | 6단계 + Claude 전용 + 스킬 확장 필드 (03-31) |
| 3D 시각화 | 90% | Model_Agent3D + HUD + NavMesh + FSM 6상태 (03-31) |
| 세션 관리 | 100% | JSON 파일 저장 + 단일 에이전트 + 세션 자동 정리 (03-31) |
| CLAUDE.md | 100% | 자동 컨텍스트 로드 (03-31) |

## 브랜치 현황

| 브랜치 | 내용 | 커밋 수 |
|--------|------|---------|
| `origin/JS/TerminalConnectionSetting` | 온보딩~채팅 (03-23~28) | 30+ |
| `origin/JS/3DModel&Claude` | 에이전트 위저드~3D~Claude 연동 (03-29~31) | 11 |

## 알려진 이슈 (03-31 기준)

| 이슈 | 심각도 | 상태 |
|------|--------|------|
| Claude CLI 응답 시간 초과 (120초) | HIGH | 미해결 — timeout 늘리거나 프롬프트 최적화 필요 |
| 에이전트 T-Pose (AnimatorController 씬 반영) | HIGH | 미해결 — 씬 재빌드 or Creation→Office 플로우로 테스트 |
| 의자 이동 → 타이핑 미동작 | MEDIUM | CLI 시간초과 해결 시 연쇄 해결 (코드 구현 완료) |
| Ollama 0.1.32 구버전 | LOW | 미해결 — ollama.com 재설치 필요 |
| Gateway dangerouslyDisable* 보안 | LOW | 개발용 — V1에서 교체 |

## 남은 작업 (우선순위순)

### HIGH
- [ ] Claude CLI 응답 시간 초과 해결
- [ ] T-Pose 근본 해결 (프리팹 ↔ 씬 AnimatorController 동기화)
- [ ] 의자 이동 → 타이핑 플로우 실제 검증

### MEDIUM
- [ ] 스킬 시스템 UI (ISkillManagerService, 스킬 슬롯, 마켓 팝업)
- [ ] system prompt 합성 (기본 프로필 + 장착 스킬 → API)
- [ ] 추가 애니메이션 (Sitting, LookAround 등 Mixamo 추가)
- [ ] Office 씬 Inspector 바인딩 잔여분

### LOW
- [ ] IExpressionController 구현 (표정/이펙트)
- [ ] Ed25519 디바이스 서명 (Gateway 보안)
- [ ] 3D 캐릭터 모델 교체 (커스텀)
- [ ] 커뮤니티 스킬 마켓
