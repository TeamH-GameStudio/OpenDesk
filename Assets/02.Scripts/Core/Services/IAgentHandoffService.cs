namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 씬 전환 사이에 살아남는 가벼운 핸드오프 채널.<br/>
    /// AgentCreationScene에서 생성된 에이전트 이름을 OnboardingScene이 §6 로딩 화면에서 표시할 때 사용한다.<br/>
    /// CoreInstaller(DontDestroyOnLoad)에 싱글톤 등록되어 씬 로드를 가로질러 유지된다.
    /// </summary>
    public interface IAgentHandoffService
    {
        /// <summary>
        /// 직전에 생성된 에이전트의 표시 이름. 사용 후 <see cref="Consume"/>으로 비운다.
        /// </summary>
        string LastCreatedAgentName { get; set; }

        /// <summary>
        /// 값을 읽고 비운다. 동일 데이터로 두 번 진입하는 것을 방지.
        /// </summary>
        string Consume();
    }
}
