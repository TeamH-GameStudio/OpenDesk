namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 신규 온보딩 흐름의 화면 스텝.
    /// 레거시 OpenClaw 시절 OnboardingState(23-state)와 분리된 별도 enum.
    /// </summary>
    public enum OnboardingFlowStep
    {
        Welcome = 0,
        Plan = 1,
        Auth = 2,
        License = 3,        // OpenDesk 라이선스 활성화 (Auth 이후, User 이전)
        User = 4,
        AgentCreation = 5,
        Loading = 6,
        Office = 7,
    }
}
