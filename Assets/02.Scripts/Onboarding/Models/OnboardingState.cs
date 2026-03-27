namespace OpenDesk.Onboarding.Models
{
    public enum OnboardingState
    {
        // ── 초기 ──────────────────────────────
        Init,
        CheckingFirstRun,

        // ── M1: 환경 스캔 ────────────────────
        ScanningEnvironment,    // UI: 시스템 사전 진단 체크리스트
        NodeInstallChoice,      // UI: Node.js 미설치 → NVM 권장 / 직접 설치 선택
        NodeUpgradeChoice,      // UI: Node.js 버전 부족 → 기존 사용처 안내 + 선택지
        InstallingNodeJs,       // UI: Node.js 설치 진행 바
        NodeJsFailed,           // UI: Node.js 설치 실패 안내
        CheckingWsl2,           // UI: WSL2 확인 중 (Windows)
        InstallingWsl2,         // UI: WSL2 설치 진행 바
        Wsl2NeedsReboot,        // UI: 재부팅 안내

        // ── OpenClaw 감지/설치 ───────────────
        DetectingOpenClaw,
        OpenClawNotFound,       // UI: "설치 필요" 화면
        InstallingOpenClaw,     // UI: 설치 진행 바
        InstallFailed,          // UI: 실패 + 재시도 버튼

        // ── Gateway 연결 ─────────────────────
        ConnectingGateway,
        GatewayFailed,          // UI: 재시도 or 수동 URL 입력
        WaitingForManualUrl,    // UI: URL 입력 폼

        // ── 에이전트 파싱 ────────────────────
        ParsingAgents,
        NoAgentsFound,          // UI: 기본 에이전트 안내

        // ── 워크스페이스 설정 ────────────────
        WorkspaceSetup,         // UI: 폴더 선택 (스킵 가능)

        // ── 완료 ─────────────────────────────
        ReadyToEnter,
        Completed,

        // ── 에러 ─────────────────────────────
        FatalError,
    }
}
