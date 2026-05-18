using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 플랜 선택 조회 + 영속화.
    /// </summary>
    public interface IPlanService
    {
        PlanTier? Selected { get; }

        UniTask<PlanTier?> LoadAsync(CancellationToken ct = default);

        UniTask<bool> SaveAsync(PlanTier tier, CancellationToken ct = default);
    }
}
