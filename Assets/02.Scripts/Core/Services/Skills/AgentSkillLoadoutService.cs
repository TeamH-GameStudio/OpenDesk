using System;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Persistence;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// AgentSkillLoadoutData 위에 R3 이벤트 + 비동기 저장을 얹은 서비스.
    /// 메모리 변경 후 SaveData 호출하여 즉시 영속화 (작업량 적음, 슬롯 변경 빈도 낮음).
    /// </summary>
    public class AgentSkillLoadoutService : IAgentSkillLoadoutService, IDisposable
    {
        private readonly IGameDataService _gameData;
        private readonly Subject<AgentSkillLoadout> _onChanged = new();

        public Observable<AgentSkillLoadout> OnLoadoutChanged => _onChanged;

        public AgentSkillLoadoutService(IGameDataService gameData)
        {
            _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        }

        public AgentSkillLoadout GetLoadout(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return AgentSkillLoadout.Empty(agentId);
            var data = _gameData.GetData<AgentSkillLoadoutData>();
            return data != null ? data.GetLoadout(agentId) : AgentSkillLoadout.Empty(agentId);
        }

        public async UniTask<bool> EquipAsync(string agentId, string skillId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(skillId)) return false;

            var data = _gameData.GetData<AgentSkillLoadoutData>();
            if (data == null)
            {
                Debug.LogError("[AgentSkillLoadoutService] AgentSkillLoadoutData 초기화 실패");
                return false;
            }

            var equipped = data.Equip(agentId, skillId);
            if (!equipped) return false;

            await _gameData.SaveData<AgentSkillLoadoutData>();
            _onChanged.OnNext(data.GetLoadout(agentId));
            return true;
        }

        public async UniTask<bool> UnequipAsync(string agentId, string skillId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(skillId)) return false;

            var data = _gameData.GetData<AgentSkillLoadoutData>();
            if (data == null) return false;

            var removed = data.Unequip(agentId, skillId);
            if (!removed) return false;

            await _gameData.SaveData<AgentSkillLoadoutData>();
            _onChanged.OnNext(data.GetLoadout(agentId));
            return true;
        }

        public void Dispose() => _onChanged.Dispose();
    }
}
