using System;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Persistence;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// AgentPluginLoadoutData 위에 R3 이벤트 + 비동기 저장을 얹은 서비스.
    /// AgentSkillLoadoutService 와 동일 패턴.
    /// </summary>
    public class AgentPluginLoadoutService : IAgentPluginLoadoutService, IDisposable
    {
        private readonly IGameDataService _gameData;
        private readonly Subject<AgentPluginLoadout> _onChanged = new();

        public Observable<AgentPluginLoadout> OnLoadoutChanged => _onChanged;

        public AgentPluginLoadoutService(IGameDataService gameData)
        {
            _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        }

        public AgentPluginLoadout GetLoadout(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return AgentPluginLoadout.Empty(agentId);
            var data = _gameData.GetData<AgentPluginLoadoutData>();
            return data != null ? data.GetLoadout(agentId) : AgentPluginLoadout.Empty(agentId);
        }

        public async UniTask<bool> EquipAsync(string agentId, string pluginId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(pluginId)) return false;

            var data = _gameData.GetData<AgentPluginLoadoutData>();
            if (data == null)
            {
                Debug.LogError("[AgentPluginLoadoutService] AgentPluginLoadoutData 초기화 실패");
                return false;
            }

            var equipped = data.Equip(agentId, pluginId);
            if (!equipped) return false;

            await _gameData.SaveData<AgentPluginLoadoutData>();
            _onChanged.OnNext(data.GetLoadout(agentId));
            return true;
        }

        public async UniTask<bool> UnequipAsync(string agentId, string pluginId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(pluginId)) return false;

            var data = _gameData.GetData<AgentPluginLoadoutData>();
            if (data == null) return false;

            var removed = data.Unequip(agentId, pluginId);
            if (!removed) return false;

            await _gameData.SaveData<AgentPluginLoadoutData>();
            _onChanged.OnNext(data.GetLoadout(agentId));
            return true;
        }

        public void Dispose() => _onChanged.Dispose();
    }
}
