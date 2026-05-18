namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 영속 데이터 카테고리 식별자.
    /// ProjectH의 TableName 패턴을 OpenDesk 도메인에 맞게 이식.
    /// 사용처가 늘어날 때마다 항목을 추가하고 GameDataService.InitializeDataRegistrations에 등록한다.
    /// </summary>
    public enum PersistedDataTable
    {
        /// <summary>에이전트별 옷장(아웃핏) 데이터 — WardrobeOutfitData.</summary>
        WardrobeOutfits,

        /// <summary>온보딩 §4에서 수집한 사용자 기본 프로필 — UserProfileData.</summary>
        UserProfile,

        /// <summary>온보딩 §2에서 선택한 플랜 — PlanSelectionData.</summary>
        PlanSelection,

        /// <summary>에이전트별 장착 스킬 ID 목록 — AgentSkillLoadoutData.</summary>
        AgentSkillLoadouts,

        /// <summary>에이전트별 장착 플러그인 ID 목록 — AgentPluginLoadoutData.</summary>
        AgentPluginLoadouts,

        // 향후 도메인 추가 위치 (예시)
        // AgentProfiles,
        // Sessions,
        // ChatHistory,
    }
}
