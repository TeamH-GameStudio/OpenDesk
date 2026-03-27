using System.Collections.Generic;

namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 온보딩 전 과정에서 누적되는 공유 컨텍스트
    /// 각 스텝이 읽고 쓴다 — 스텝 간 직접 의존 없이 Context를 통해 소통
    /// </summary>
    public class OnboardingContext
    {
        // OpenClaw
        public bool   IsOpenClawInstalled { get; set; }
        public string OpenClawVersion     { get; set; } = "";
        public string OpenClawConfigPath  { get; set; } = "";

        // Gateway
        public string GatewayUrl          { get; set; } = "ws://127.0.0.1:18789";
        public bool   IsGatewayConnected  { get; set; }
        public int    GatewayRetryCount   { get; set; }

        // 에이전트
        public List<AgentConfig> DetectedAgents { get; set; } = new();
        public bool IsOfflineMode               { get; set; }

        // 워크스페이스
        public string LocalWorkspacePath  { get; set; } = "";
        public bool   WorkspaceSkipped    { get; set; }

        // Node.js 버전 충돌
        public string ExistingNodeVersion        { get; set; } = "";
        public List<string> NodeProjectPaths     { get; set; } = new();
        public bool   NodeUpgradeSkipped         { get; set; }

        // Node.js 신규 설치 선택
        public bool   NodeInstallSkipped         { get; set; }

        // 에러
        public string LastErrorMessage    { get; set; } = "";
    }
}
