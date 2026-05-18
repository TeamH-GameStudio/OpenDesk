using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// 원격 catalog.json 의 단일 스킬 엔트리. zip 매니페스트와 별개로 마켓 목록/추천에 사용.
    /// promptContent 는 zip 내부에 있으며 카탈로그에는 메타데이터만 포함.
    /// </summary>
    [Serializable]
    public class SkillCatalogEntry
    {
        public string id;
        public string displayName;
        public string description;
        public string category;          // 소문자 키
        public string version;
        public string author;
        public string iconUrl;
        public string downloadUrl;
        public string checksum;          // "sha256:..." 또는 "sha256-..."
        public long publishedAt;         // Unix epoch (sec)
        public int downloads;
        public float rating;
        public List<string> recommendedRoles = new();   // AgentRole enum 이름

        public SkillDescriptor ToDescriptor(SkillManifest manifestOrNull, bool isInstalled, string installPath)
        {
            var resolvedCategory = SkillCategoryExtensions.ParseCategory(category);
            var roles = ParseRoles(recommendedRoles);

            var promptContent = manifestOrNull?.promptContent ?? string.Empty;
            var mcpServerCommand = manifestOrNull?.mcpServerCommand ?? string.Empty;
            var tokens = manifestOrNull?.requiredTokens != null
                ? new List<string>(manifestOrNull.requiredTokens)
                : new List<string>();

            return new SkillDescriptor(
                Id: id ?? string.Empty,
                DisplayName: string.IsNullOrEmpty(displayName) ? id : displayName,
                Description: description ?? string.Empty,
                Category: resolvedCategory,
                Version: version ?? "0.0.0",
                Author: author ?? string.Empty,
                IconUrl: iconUrl ?? string.Empty,
                PromptContent: promptContent,
                McpServerCommand: mcpServerCommand,
                RequiredTokens: tokens,
                RecommendedRoles: roles,
                DownloadUrl: downloadUrl ?? string.Empty,
                Checksum: checksum ?? string.Empty,
                PublishedAt: FromUnixSeconds(publishedAt),
                Downloads: downloads,
                Rating: rating,
                IsInstalled: isInstalled,
                InstallPath: installPath ?? string.Empty
            );
        }

        private static DateTime FromUnixSeconds(long sec)
        {
            if (sec <= 0) return DateTime.MinValue;
            try { return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime; }
            catch { return DateTime.MinValue; }
        }

        private static IReadOnlyList<AgentRole> ParseRoles(List<string> rawRoles)
        {
            if (rawRoles == null || rawRoles.Count == 0)
                return Array.Empty<AgentRole>();

            var result = new List<AgentRole>(rawRoles.Count);
            foreach (var raw in rawRoles)
            {
                if (Enum.TryParse<AgentRole>(raw, ignoreCase: true, out var role) &&
                    role != AgentRole.None)
                {
                    result.Add(role);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 역할별 카테고리 추천 키 매핑. JsonUtility 가 Dictionary 를 지원하지 않으므로
    /// List 페어 형태로 직렬화한 뒤 런타임에서 사전으로 인덱싱한다.
    /// </summary>
    [Serializable]
    public class RoleRecommendation
    {
        public string role;              // AgentRole enum 이름
        public List<string> categories = new();   // 카테고리 키 (소문자)
    }

    /// <summary>
    /// 원격 catalog.json 최상위 객체. RemoteSkillRegistry 가 HTTP 로 가져와 캐시한다.
    /// updatedAt + ETag 로 변경 감지.
    /// </summary>
    [Serializable]
    public class SkillCatalog
    {
        public string schemaVersion = "1.0";
        public long updatedAt;
        public List<SkillCatalogEntry> skills = new();
        public List<RoleRecommendation> roleRecommendations = new();

        public List<string> GetRecommendedCategoriesFor(AgentRole role)
        {
            if (role == AgentRole.None || roleRecommendations == null)
                return new List<string>();

            var key = role.ToString();
            foreach (var pair in roleRecommendations)
            {
                if (string.Equals(pair.role, key, StringComparison.OrdinalIgnoreCase))
                    return pair.categories != null
                        ? new List<string>(pair.categories)
                        : new List<string>();
            }
            return new List<string>();
        }
    }
}
