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

            return new SavedAgentData
            {
                AgentName      = name,
                Role           = (AgentRole)PlayerPrefs.GetInt(prefix + "Role", 0),
                AIModel        = (AgentAIModel)PlayerPrefs.GetInt(prefix + "AIModel", 0),
                Tone           = (AgentTone)PlayerPrefs.GetInt(prefix + "Tone", 0),
                ModelPrefabName = PlayerPrefs.GetString(prefix + "ModelPrefab", ""),
                SessionId      = PlayerPrefs.GetString(prefix + "SessionId", ""),
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

        /// <summary>AgentCreationData로 변환</summary>
        public AgentCreationData ToCreationData()
        {
            return new AgentCreationData
            {
                AgentName = AgentName,
                Role = Role,
                AIModel = AIModel,
                Tone = Tone,
            };
        }
    }
}
