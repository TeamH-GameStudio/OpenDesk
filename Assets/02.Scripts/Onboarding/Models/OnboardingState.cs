namespace OpenDesk.Onboarding.Models
{
    public enum OnboardingState
    {
        // ── 초기 ──────────────────────────────
        Init,
        CheckingFirstRun,

        // ── OpenClaw 감지 ─────────────────────
        DetectingOpenClaw,
        OpenClawNotFound,       // UI: "설치 필요" 화면
        InstallingOpenClaw,     // UI: 설치 진행 바
        InstallFailed,          // UI: 실패 + 재시도 버튼

        // ── Gateway 연결 ──────────────────────
        ConnectingGateway,
        GatewayFailed,          // UI: 재시도 or 수동 URL 입력
        WaitingForManualUrl,    // UI: URL 입력 폼

        // ── 에이전트 파싱 ─────────────────────
        ParsingAgents,
        NoAgentsFound,          // UI: 기본 에이전트 안내

        // ── 워크스페이스 설정 ─────────────────
        WorkspaceSetup,         // UI: 폴더 선택 (스킵 가능)

        // ── 완료 ──────────────────────────────
        ReadyToEnter,
        Completed,

        // ── 에러 ──────────────────────────────
        FatalError,
    }
}
