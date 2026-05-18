using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;
using R3;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 에이전트별 장착 스킬 ID 목록의 영속 진입점.
    /// 변경 시 OnLoadoutChanged 로 통보 → UI/AgentEquipmentManager 가 반응.
    /// </summary>
    public interface IAgentSkillLoadoutService
    {
        Observable<AgentSkillLoadout> OnLoadoutChanged { get; }

        AgentSkillLoadout GetLoadout(string agentId);

        UniTask<bool> EquipAsync(string agentId, string skillId);

        UniTask<bool> UnequipAsync(string agentId, string skillId);
    }
}
