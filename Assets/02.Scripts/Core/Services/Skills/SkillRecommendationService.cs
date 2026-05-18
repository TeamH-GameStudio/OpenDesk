using System;
using System.Collections.Generic;
using System.Linq;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// catalog.roleRecommendations 의 카테고리 키 우선 + 스킬 자체의 RecommendedRoles 보조.
    /// 동일 우선순위 내에서는 downloads / rating 으로 정렬.
    /// </summary>
    public class SkillRecommendationService : ISkillRecommendationService
    {
        private readonly ISkillCatalogService _catalog;

        public SkillRecommendationService(ISkillCatalogService catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public IReadOnlyList<SkillDescriptor> RecommendForRole(AgentRole role, int limit = 6)
        {
            if (role == AgentRole.None || limit <= 0) return Array.Empty<SkillDescriptor>();

            var all = _catalog.GetAll();
            if (all == null || all.Count == 0) return Array.Empty<SkillDescriptor>();

            var recommendedCategories = _catalog.GetRecommendedCategoriesFor(role) ?? Array.Empty<SkillCategory>();
            var categorySet = new HashSet<SkillCategory>(recommendedCategories);

            var seen = new HashSet<string>();
            var result = new List<SkillDescriptor>();

            // 1차: 스킬 자체의 RecommendedRoles 에 정확히 매치되는 스킬 (가장 강한 신호)
            foreach (var descriptor in all
                         .Where(d => d.RecommendedRoles != null && d.RecommendedRoles.Contains(role))
                         .OrderByDescending(d => d.Rating)
                         .ThenByDescending(d => d.Downloads))
            {
                if (result.Count >= limit) break;
                if (seen.Add(descriptor.Id)) result.Add(descriptor);
            }

            // 2차: catalog.roleRecommendations 카테고리에 속하는 스킬
            if (result.Count < limit && categorySet.Count > 0)
            {
                foreach (var descriptor in all
                             .Where(d => categorySet.Contains(d.Category))
                             .OrderByDescending(d => d.Downloads)
                             .ThenByDescending(d => d.Rating))
                {
                    if (result.Count >= limit) break;
                    if (seen.Add(descriptor.Id)) result.Add(descriptor);
                }
            }

            // 3차 (폴백): 다운로드 상위
            if (result.Count < limit)
            {
                foreach (var descriptor in all
                             .OrderByDescending(d => d.Downloads)
                             .ThenByDescending(d => d.Rating))
                {
                    if (result.Count >= limit) break;
                    if (seen.Add(descriptor.Id)) result.Add(descriptor);
                }
            }

            return result;
        }
    }
}
