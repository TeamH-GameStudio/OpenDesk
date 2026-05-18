using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using R3;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 에이전트별 장착 플러그인 ID 목록의 영속 진입점.
    /// 변경 시 OnLoadoutChanged 로 통보 → UI/IMcpConfigComposer 가 반응.
    /// IAgentSkillLoadoutService 와 동일 패턴.
    /// </summary>
    public interface IAgentPluginLoadoutService
    {
        Observable<AgentPluginLoadout> OnLoadoutChanged { get; }

        AgentPluginLoadout GetLoadout(string agentId);

        UniTask<bool> EquipAsync(string agentId, string pluginId);

        UniTask<bool> UnequipAsync(string agentId, string pluginId);
    }
}
