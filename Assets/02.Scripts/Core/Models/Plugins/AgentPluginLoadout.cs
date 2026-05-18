using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// 단일 에이전트의 장착 플러그인 ID 목록. 슬롯 무제한.
    /// AgentPluginLoadoutService 가 영속한다.
    /// </summary>
    public sealed record AgentPluginLoadout(
        string AgentId,
        IReadOnlyList<string> EquippedPluginIds
    )
    {
        public static AgentPluginLoadout Empty(string agentId) =>
            new(agentId ?? string.Empty, Array.Empty<string>());

        public bool IsEmpty => EquippedPluginIds == null || EquippedPluginIds.Count == 0;

        public AgentPluginLoadout WithEquip(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return this;
            var current = EquippedPluginIds ?? Array.Empty<string>();
            foreach (var existing in current)
                if (existing == pluginId) return this;

            var next = new List<string>(current.Count + 1);
            next.AddRange(current);
            next.Add(pluginId);
            return new AgentPluginLoadout(AgentId, next);
        }

        public AgentPluginLoadout WithUnequip(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return this;
            var current = EquippedPluginIds ?? Array.Empty<string>();
            var next = new List<string>(current.Count);
            foreach (var existing in current)
                if (existing != pluginId) next.Add(existing);
            return new AgentPluginLoadout(AgentId, next);
        }
    }

    /// <summary>
    /// 전체 에이전트의 플러그인 장착 상태 영속 표현. JsonUtility 호환.
    /// PersistedDataTable 또는 PlayerPrefs 한 키에 단일 JSON 으로 저장.
    /// </summary>
    [Serializable]
    public class AgentPluginLoadoutPersistedData
    {
        public List<PersistedPluginLoadoutEntry> entries = new();
    }

    [Serializable]
    public class PersistedPluginLoadoutEntry
    {
        public string agentId;
        public List<string> equippedPluginIds = new();
    }
}
