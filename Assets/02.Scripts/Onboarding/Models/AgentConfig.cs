namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// OpenClaw .yaml에서 파싱된 에이전트 설정
    /// </summary>
    public class AgentConfig
    {
        public string SessionId { get; set; } = "";
        public string Name      { get; set; } = "";
        public string Role      { get; set; } = "main";   // main / dev / planner / life
        public string Model     { get; set; } = "";
        public bool   HasApiKey { get; set; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(SessionId) &&
            !string.IsNullOrWhiteSpace(Name);
    }
}
