namespace OpenDesk.Presentation.Character.Context
{
    /// <summary>
    /// 캐릭터 상태 머신이 접근하는 컨텍스트
    /// 각 State는 이것만 통해 외부 시스템 접근
    /// </summary>
    public class AgentCharacterContext
    {
        // 애니메이션 — ProjectH IAnimationController 재사용
        public IAnimationController Animation { get; }

        // 에이전트 식별
        public string SessionId { get; }
        public string AgentName { get; }

        public AgentCharacterContext(
            IAnimationController animation,
            string sessionId,
            string agentName)
        {
            Animation = animation;
            SessionId = sessionId;
            AgentName = agentName;
        }
    }
}
