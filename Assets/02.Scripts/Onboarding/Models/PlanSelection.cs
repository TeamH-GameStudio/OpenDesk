namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 사용자가 선택한 플랜 (immutable). 영속 컨테이너는 <see cref="PlanSelectionData"/>.
    /// </summary>
    public sealed class PlanSelection
    {
        public PlanTier Tier { get; }

        public PlanSelection(PlanTier tier)
        {
            Tier = tier;
        }
    }
}
