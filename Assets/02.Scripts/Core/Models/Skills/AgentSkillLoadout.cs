using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// 단일 에이전트의 장착 스킬 ID 목록. 슬롯 무제한.
    /// AgentSkillLoadoutService 가 PersistedDataTable.AgentSkillLoadouts 로 영속.
    /// </summary>
    public sealed record AgentSkillLoadout(
        string AgentId,
        IReadOnlyList<string> EquippedSkillIds
    )
    {
        public static AgentSkillLoadout Empty(string agentId) =>
            new(agentId ?? string.Empty, Array.Empty<string>());

        public bool IsEmpty => EquippedSkillIds == null || EquippedSkillIds.Count == 0;

        public AgentSkillLoadout WithEquip(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return this;
            var current = EquippedSkillIds ?? Array.Empty<string>();
            foreach (var existing in current)
                if (existing == skillId) return this;

            var next = new List<string>(current.Count + 1);
            next.AddRange(current);
            next.Add(skillId);
            return new AgentSkillLoadout(AgentId, next);
        }

        public AgentSkillLoadout WithUnequip(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return this;
            var current = EquippedSkillIds ?? Array.Empty<string>();
            var next = new List<string>(current.Count);
            foreach (var existing in current)
                if (existing != skillId) next.Add(existing);
            return new AgentSkillLoadout(AgentId, next);
        }
    }

    /// <summary>
    /// PersistedDataTable.AgentSkillLoadouts 의 영속 표현. JsonUtility 호환.
    /// 전체 에이전트의 장착 상태를 단일 JSON 으로 묶어 저장한다.
    /// </summary>
    [Serializable]
    public class AgentSkillLoadoutPersistedData
    {
        public List<PersistedLoadoutEntry> entries = new();
    }

    [Serializable]
    public class PersistedLoadoutEntry
    {
        public string agentId;
        public List<string> equippedSkillIds = new();
    }
}
