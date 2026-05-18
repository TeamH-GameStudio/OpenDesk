using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 에이전트 역할 기반 스킬 추천. 카탈로그의 roleRecommendations 매핑 우선 + 폴백 휴리스틱.
    /// </summary>
    public interface ISkillRecommendationService
    {
        IReadOnlyList<SkillDescriptor> RecommendForRole(AgentRole role, int limit = 6);
    }
}
