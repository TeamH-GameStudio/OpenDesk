# 작업 현황 (2026-03-31 기준)

작업 정리 폴더: `C:/Users/user/Desktop/OpenDesk/작업정리/`

## 마일스톤 완성도

| 마일스톤 | 완성도 | 핵심 내용 |
|----------|--------|-----------|
| M0 기반 | 100% | Gateway 포트 통일, Bridge 재연결, 하트비트, 이벤트 파서 |
| M1 환경설치 | 95% | Node.js/WSL2/OpenClaw 자동설치, UAC, 재부팅 복귀, 포트 충돌 감지 |
| M2 키관리 | 90% | 14개 제공업체, 라우팅 모드, Base64 암호화 |
| M3 대시보드 | 90% | 콘솔 로그, 비용 HUD, 에이전틱 루프 그래프 |
| M4 채널/스킬 | 85% | 5채널 모델, 10개 내장 스킬 카탈로그 |
| M5 보안 | 85% | 4도메인 감사, CLI 연동 |
| 온보딩 | 100% | 실제 Windows PC 테스트 완료 (03-24) |
| Gateway 연결 | 100% | 핸드셰이크 + 토큰 인증 + 스코프 (03-25) |
| 채팅 (Gateway) | 100% | chat.send RPC + 스트리밍 delta/final (03-27~28) |
| 채팅 (미들웨어) | 100% | Python + Claude CLI + 상태표시 (03-28) |
| 에이전트 위저드 | 100% | 6단계 플로우 + PlayerPrefs 저장 (03-29) |
| 3D 시각화 | 100% | 큐브 소환 + HUD 13종 + Billboard (03-29) |
| 세션 관리 | 100% | CRUD + 리스트 + 에이전트 클릭 연동 (03-29) |

## 브랜치 현황

| 브랜치 | 내용 | 커밋 수 |
|--------|------|---------|
| `origin/JS/TerminalConnectionSetting` | 온보딩~채팅 (03-23~28) | 30+ |
| `origin/JS/3DModel&Claude` | 에이전트 위저드~세션 (03-29) | 4 |

## 남은 작업 (우선순위순)

### HIGH
- [ ] 스킬 시스템 구현 — ISkillManagerService, 스킬 슬롯 UI, 마켓 팝업
- [ ] system prompt 합성 — 기본 프로필 + 장착 스킬 SKILL.md → API system prompt
- [ ] Office 씬 Inspector 바인딩 잔여분

### MEDIUM
- [ ] Ed25519 디바이스 서명 (dangerouslyDisable* 설정 교체)
- [ ] API 키 저장소 DPAPI/Keychain 업그레이드
- [ ] 테스트 커버리지 40% → 70%
- [ ] Ollama 최신 버전 업데이트 + 로컬 모델 테스트

### LOW
- [ ] 3D 캐릭터 모델/애니메이션 (현재 큐브)
- [ ] 다크 테마 색상 팔레트 확정
- [ ] 다국어 지원
- [ ] 커뮤니티 스킬 마켓 (Phase 3)

## 알려진 이슈

| 이슈 | 상태 |
|------|------|
| Ollama 0.1.32 구버전 | 미해결 — ollama.com 재설치 필요 |
| Groq rate limit (분당 30) | 일시적 — 무료 한도 |
| Gateway dangerouslyDisable* 보안 | 개발용 — V1에서 교체 |
| NativeWebSocket Connect() 블로킹 | 해결됨 — fire-and-forget 패턴 |
