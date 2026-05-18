using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Common;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §2 플랜 선택 ViewModel.
    /// </summary>
    public sealed class OnbPlanViewModel : ObservableObject
    {
        private readonly IPlanService _planService;

        private PlanTier? _selectedTier;
        public PlanTier? SelectedTier
        {
            get => _selectedTier;
            private set
            {
                if (SetField(ref _selectedTier, value))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public bool CanAdvance => _selectedTier.HasValue;

        public event Action<PlanTier> PlanSelected;
        public event Action BackRequested;

        public OnbPlanViewModel(IPlanService planService)
        {
            _planService = planService;

            // 진입 시 기존 선택 복원 (앱 재시작 후 재진입 케이스).
            _selectedTier = planService?.Selected;
        }

        public void Select(PlanTier tier) => SelectedTier = tier;

        public void Back() => BackRequested?.Invoke();

        public async UniTask AdvanceAsync(CancellationToken ct = default)
        {
            if (!CanAdvance || _planService == null) return;
            var tier = _selectedTier!.Value;
            await _planService.SaveAsync(tier, ct);
            PlanSelected?.Invoke(tier);
        }
    }
}
