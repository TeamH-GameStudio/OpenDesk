namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 전문 분야 (OpenClaw 역할 기준)
    /// </summary>
    public enum AgentRole
    {
        None,
        Planning,    // 기획
        Development, // 개발
        Design,      // 디자인
        Legal,       // 법률
        Marketing,   // 마케팅
        Research,    // 리서치
        Support,     // 고객지원
        Finance,     // 재무
    }
}
