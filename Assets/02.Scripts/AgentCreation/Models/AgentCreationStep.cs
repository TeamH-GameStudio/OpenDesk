namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 제작 위저드 단계
    /// </summary>
    public enum AgentCreationStep
    {
        Hidden,         // 위저드 비표시
        NameInput,      // Step 1: 이름 정하기
        RoleSelect,     // Step 2: 역할 부여
        ModelSelect,    // Step 3: AI 모델 선택
        ToneSelect,     // Step 4: 말투 설정
        AvatarSelect,   // Step 5: 3D 모델(아바타) 선택
        Confirm,        // Step 6: 최종 확인 및 생성
    }
}
