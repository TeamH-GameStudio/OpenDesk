namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 온보딩 플랜 선택지.
    /// </summary>
    public enum PlanTier
    {
        /// <summary>Dev Plan — 본인 API 사용, 무제한 메시지, 개발자 친화.</summary>
        Dev = 0,

        /// <summary>Free Plan — 가볍게 시작, 제한된 메시지, 동료 1명.</summary>
        Free = 1,

        /// <summary>Pro Plan — 곧 공개 예정, 다중 동료 + 모든 모델.</summary>
        Pro = 2,
    }
}
