using System;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 제작 위저드에서 수집하는 설정 데이터.
    /// 각 Step에서 값을 채운 뒤 최종 확인 시 사용.
    /// </summary>
    [Serializable]
    public class AgentCreationData
    {
        // Step 1
        public string AgentName = "";

        // Step 2
        public AgentRole Role = AgentRole.None;

        // Step 3
        public AgentAIModel AIModel = AgentAIModel.None;

        // Step 4
        public AgentTone Tone = AgentTone.None;

        // Step 5
        public string AvatarPrefabName = "";

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(AgentName) &&
            Role != AgentRole.None &&
            AIModel != AgentAIModel.None &&
            Tone != AgentTone.None &&
            !string.IsNullOrWhiteSpace(AvatarPrefabName);

        public void Reset()
        {
            AgentName = "";
            Role = AgentRole.None;
            AIModel = AgentAIModel.None;
            Tone = AgentTone.None;
            AvatarPrefabName = "";
        }
    }
}
