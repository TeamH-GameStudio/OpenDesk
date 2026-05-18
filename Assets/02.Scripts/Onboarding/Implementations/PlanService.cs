using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Persistence;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    public sealed class PlanService : IPlanService
    {
        private readonly IGameDataService _gameDataService;

        public PlanService(IGameDataService gameDataService)
        {
            _gameDataService = gameDataService;
        }

        public PlanTier? Selected
        {
            get
            {
                var data = _gameDataService.GetData<PlanSelectionData>();
                var snap = data?.Snapshot();
                return snap?.Tier;
            }
        }

        public async UniTask<PlanTier?> LoadAsync(CancellationToken ct = default)
        {
            var data = _gameDataService.GetData<PlanSelectionData>();
            if (data == null)
            {
                Debug.LogWarning("[PlanService] PlanSelectionData가 캐시에 없습니다 — InitializeAllData가 선행되어야 합니다.");
                return null;
            }

            await _gameDataService.FetchData<PlanSelectionData>().AttachExternalCancellation(ct);
            return data.Snapshot()?.Tier;
        }

        public async UniTask<bool> SaveAsync(PlanTier tier, CancellationToken ct = default)
        {
            var data = _gameDataService.GetData<PlanSelectionData>() ?? new PlanSelectionData();
            data.Apply(new PlanSelection(tier));
            return await _gameDataService.SaveData(data).AttachExternalCancellation(ct);
        }
    }
}
