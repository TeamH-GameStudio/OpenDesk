namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 각 온보딩 스텝의 실행 결과
    /// </summary>
    public class StepResult
    {
        public bool   IsSuccess    { get; private set; }
        public string Message      { get; private set; } = "";
        public OnboardingState NextState { get; private set; }

        public static StepResult Success(OnboardingState next, string message = "") =>
            new() { IsSuccess = true,  NextState = next,  Message = message };

        public static StepResult Fail(OnboardingState next, string message = "") =>
            new() { IsSuccess = false, NextState = next,  Message = message };
    }
}
