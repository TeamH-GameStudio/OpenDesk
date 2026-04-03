using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// PlayerPrefs 기반 에이전트 설정 저장/로드.
    /// 서버 없이 로컬 영속화. 복수 에이전트 지원 (인덱스 기반).
    /// </summary>
    public static class AgentDataStore
    {
        private const string KeyPrefix = "OpenDesk_Agent_";
        private const string KeyCount  = "OpenDesk_AgentCount";

        // ================================================================
        //  저장
        // ================================================================

        /// <summary>에이전트 데이터를 다음 슬롯에 저장하고 인덱스를 반환</summary>
        public static int Save(AgentCreationData data, string modelPrefabName)
        {
            int index = PlayerPrefs.GetInt(KeyCount, 0);

            var prefix = $"{KeyPrefix}{index}_";
            PlayerPrefs.SetString(prefix + "Name",        data.AgentName);
            PlayerPrefs.SetInt(prefix    + "Role",        (int)data.Role);
            PlayerPrefs.SetInt(prefix    + "AIModel",     (int)data.AIModel);
            PlayerPrefs.SetInt(prefix    + "Tone",        (int)data.Tone);
            PlayerPrefs.SetString(prefix + "ModelPrefab", modelPrefabName);
            PlayerPrefs.SetString(prefix + "SessionId",   $"agent_{index}_{System.Guid.NewGuid():N}"[..16]);

            // 확장 필드 저장
            PlayerPrefs.SetString(prefix + "AllowedTools",    string.Join("|", data.AllowedTools));
            PlayerPrefs.SetString(prefix + "ExecContext",     data.ExecutionContext);
            PlayerPrefs.SetString(prefix + "ArgHint",         data.ArgumentHint);
            PlayerPrefs.SetString(prefix + "EquippedSkills",  string.Join("|", data.EquippedSkills));
            PlayerPrefs.SetString(prefix + "CustomPrompt",    data.CustomPrompt);
            PlayerPrefs.SetInt(prefix    + "MaxSkillSlots",   data.MaxSkillSlots);

            PlayerPrefs.SetInt(KeyCount, index + 1);
            PlayerPrefs.Save();

            Debug.Log($"[AgentDataStore] 저장 완료: [{index}] {data.AgentName}");
            return index;
        }

        // ================================================================
        //  로드
        // ================================================================

        /// <summary>저장된 에이전트 수</summary>
        public static int Count => PlayerPrefs.GetInt(KeyCount, 0);

        /// <summary>인덱스로 에이전트 데이터 로드. 없으면 null.</summary>
        public static SavedAgentData Load(int index)
        {
            var prefix = $"{KeyPrefix}{index}_";
            var name = PlayerPrefs.GetString(prefix + "Name", "");
            if (string.IsNullOrEmpty(name)) return null;

            var toolsRaw = PlayerPrefs.GetString(prefix + "AllowedTools", "");
            var skillsRaw = PlayerPrefs.GetString(prefix + "EquippedSkills", "");

            return new SavedAgentData
            {
                AgentName       = name,
                Role            = (AgentRole)PlayerPrefs.GetInt(prefix + "Role", 0),
                AIModel         = (AgentAIModel)PlayerPrefs.GetInt(prefix + "AIModel", 0),
                Tone            = (AgentTone)PlayerPrefs.GetInt(prefix + "Tone", 0),
                ModelPrefabName = PlayerPrefs.GetString(prefix + "ModelPrefab", ""),
                SessionId       = PlayerPrefs.GetString(prefix + "SessionId", ""),
                AllowedTools    = string.IsNullOrEmpty(toolsRaw) ? new() : new(toolsRaw.Split('|')),
                ExecutionContext = PlayerPrefs.GetString(prefix + "ExecContext", ""),
                ArgumentHint    = PlayerPrefs.GetString(prefix + "ArgHint", ""),
                EquippedSkills  = string.IsNullOrEmpty(skillsRaw) ? new() : new(skillsRaw.Split('|')),
                CustomPrompt    = PlayerPrefs.GetString(prefix + "CustomPrompt", ""),
                MaxSkillSlots   = PlayerPrefs.GetInt(prefix + "MaxSkillSlots", 3),
            };
        }

        /// <summary>모든 저장된 에이전트 로드</summary>
        public static SavedAgentData[] LoadAll()
        {
            int count = Count;
            var result = new SavedAgentData[count];
            for (int i = 0; i < count; i++)
                result[i] = Load(i);
            return result;
        }

        // ================================================================
        //  삭제
        // ================================================================

        /// <summary>모든 에이전트 데이터 초기화</summary>
        public static void ClearAll()
        {
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                var prefix = $"{KeyPrefix}{i}_";
                PlayerPrefs.DeleteKey(prefix + "Name");
                PlayerPrefs.DeleteKey(prefix + "Role");
                PlayerPrefs.DeleteKey(prefix + "AIModel");
                PlayerPrefs.DeleteKey(prefix + "Tone");
                PlayerPrefs.DeleteKey(prefix + "ModelPrefab");
                PlayerPrefs.DeleteKey(prefix + "SessionId");
                PlayerPrefs.DeleteKey(prefix + "AllowedTools");
                PlayerPrefs.DeleteKey(prefix + "ExecContext");
                PlayerPrefs.DeleteKey(prefix + "ArgHint");
                PlayerPrefs.DeleteKey(prefix + "EquippedSkills");
                PlayerPrefs.DeleteKey(prefix + "CustomPrompt");
                PlayerPrefs.DeleteKey(prefix + "MaxSkillSlots");
            }
            PlayerPrefs.DeleteKey(KeyCount);
            PlayerPrefs.Save();
            Debug.Log("[AgentDataStore] 전체 초기화 완료");
        }
    }

    /// <summary>PlayerPrefs에서 로드된 에이전트 데이터</summary>
    public class SavedAgentData
    {
        public string AgentName;
        public AgentRole Role;
        public AgentAIModel AIModel;
        public AgentTone Tone;
        public string ModelPrefabName;
        public string SessionId;

        // 확장 필드
        public System.Collections.Generic.List<string> AllowedTools = new();
        public string ExecutionContext = "";
        public string ArgumentHint = "";
        public System.Collections.Generic.List<string> EquippedSkills = new();
        public string CustomPrompt = "";
        public int MaxSkillSlots = 3;

        /// <summary>AgentCreationData로 변환</summary>
        public AgentCreationData ToCreationData()
        {
            return new AgentCreationData
            {
                AgentName = AgentName,
                Role = Role,
                AIModel = AIModel,
                Tone = Tone,
                AllowedTools = new(AllowedTools),
                ExecutionContext = ExecutionContext,
                ArgumentHint = ArgumentHint,
                EquippedSkills = new(EquippedSkills),
                CustomPrompt = CustomPrompt,
                MaxSkillSlots = MaxSkillSlots,
            };
        }

        /// <summary>디버그 출력용</summary>
        public string ToDebugString()
        {
            var data = ToCreationData();
            data.AvatarPrefabName = ModelPrefabName;
            return data.ToDebugString() + $"\n  SessionId: {SessionId}";
        }
    }
}
