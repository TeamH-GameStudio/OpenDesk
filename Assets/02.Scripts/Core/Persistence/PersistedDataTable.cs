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

        // 향후 도메인 추가 위치 (예시)
        // AgentProfiles,
        // Sessions,
        // ChatHistory,
        // UserPreferences,
    }
}
