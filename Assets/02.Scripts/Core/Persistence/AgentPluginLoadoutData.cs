using System;
using System.Collections.Generic;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 에이전트별 장착 플러그인 ID 목록 영속 데이터.
    /// agentId → List&lt;pluginId&gt; 매핑. 슬롯 무제한.
    /// AgentSkillLoadoutData 와 동일 패턴.
    /// </summary>
    [TableName(PersistedDataTable.AgentPluginLoadouts)]
    public sealed class AgentPluginLoadoutData : IGameData
    {
        private const int CURRENT_VERSION = 1;

        private readonly Dictionary<string, List<string>> _loadouts = new();
        private bool _isDirty;

        public bool IsDirty => _isDirty;
        public void MarkAsDirty() => _isDirty = true;
        public void ResetDirty() => _isDirty = false;

        public void InitializeDefault()
        {
            _loadouts.Clear();
            _isDirty = false;
        }

        public void ResetAllData()
        {
            _loadouts.Clear();
            _isDirty = false;
        }

        public string ToJson()
        {
            var snap = new SerializedSnapshot { version = CURRENT_VERSION };
            foreach (var kvp in _loadouts)
            {
                snap.entries.Add(new SerializedEntry
                {
                    agentId = kvp.Key,
                    pluginIds = kvp.Value != null ? new List<string>(kvp.Value) : new List<string>(),
                });
            }
            return JsonUtility.ToJson(snap);
        }

        public void FromJson(string json)
        {
            _loadouts.Clear();
            if (string.IsNullOrEmpty(json)) return;

            SerializedSnapshot snap;
            try { snap = JsonUtility.FromJson<SerializedSnapshot>(json); }
            catch (Exception e)
            {
                Debug.LogError($"[AgentPluginLoadoutData] JSON 파싱 실패: {e.Message}");
                return;
            }

            if (snap?.entries == null) return;
            foreach (var entry in snap.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.agentId)) continue;
                _loadouts[entry.agentId] = entry.pluginIds != null
                    ? new List<string>(entry.pluginIds)
                    : new List<string>();
            }
        }

        // ── Public API ──

        public AgentPluginLoadout GetLoadout(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return AgentPluginLoadout.Empty(agentId);
            return _loadouts.TryGetValue(agentId, out var ids)
                ? new AgentPluginLoadout(agentId, new List<string>(ids))
                : AgentPluginLoadout.Empty(agentId);
        }

        public bool Equip(string agentId, string pluginId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(pluginId)) return false;
            if (!_loadouts.TryGetValue(agentId, out var ids))
            {
                ids = new List<string>();
                _loadouts[agentId] = ids;
            }
            if (ids.Contains(pluginId)) return false;
            ids.Add(pluginId);
            _isDirty = true;
            return true;
        }

        public bool Unequip(string agentId, string pluginId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(pluginId)) return false;
            if (!_loadouts.TryGetValue(agentId, out var ids)) return false;
            var removed = ids.Remove(pluginId);
            if (removed) _isDirty = true;
            return removed;
        }

        public IReadOnlyDictionary<string, List<string>> All => _loadouts;

        // ── DTO ──

        [Serializable]
        private sealed class SerializedSnapshot
        {
            public int version;
            public List<SerializedEntry> entries = new();
        }

        [Serializable]
        private sealed class SerializedEntry
        {
            public string agentId;
            public List<string> pluginIds = new();
        }
    }
}
