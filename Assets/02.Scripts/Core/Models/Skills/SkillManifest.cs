using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// 다운로드한 스킬 zip 안의 manifest.json 매핑.
    /// JsonUtility 호환 [Serializable] class. promptContent 본문은 별도 prompt.md 로 분리 가능.
    /// </summary>
    [Serializable]
    public class SkillManifest
    {
        public string id;
        public string displayName;
        public string description;
        public string category;          // SkillCategoryExtensions.ParseCategory 로 변환
        public string version;
        public string author;
        public string iconUrl;
        public string promptContent;     // prompt.md 본문이 들어있다면 null
        public string promptFile;        // 별도 파일 경로 (manifest 상대). null 가능
        public string mcpServerCommand;
        public List<string> requiredTokens = new();
        public List<string> recommendedRoles = new();   // AgentRole enum 이름 문자열

        // v4: 스킬이 의존하는 플러그인 ID 목록. SkillMarketView 설치 모달이 함께-설치 흐름을 만든다.
        // required: 미설치면 함께 설치 체크박스(default ON), CTA 카운트 포함.
        // optional: 미설치 시 정보용으로만 노출 (default OFF).
        public List<string> requiredPlugins = new();
        public List<string> optionalPlugins = new();

        public SkillDescriptor ToDescriptor(string installPath)
        {
            var resolvedCategory = SkillCategoryExtensions.ParseCategory(category);
            var roles = ParseRoles(recommendedRoles);
            var tokens = requiredTokens != null
                ? new List<string>(requiredTokens)
                : new List<string>();
            var required = requiredPlugins != null
                ? requiredPlugins.FindAll(p => !string.IsNullOrWhiteSpace(p))
                : new List<string>();
            var optional = optionalPlugins != null
                ? optionalPlugins.FindAll(p => !string.IsNullOrWhiteSpace(p))
                : new List<string>();

            return new SkillDescriptor(
                Id: id ?? string.Empty,
                DisplayName: string.IsNullOrEmpty(displayName) ? id : displayName,
                Description: description ?? string.Empty,
                Category: resolvedCategory,
                Version: version ?? "0.0.0",
                Author: author ?? string.Empty,
                IconUrl: iconUrl ?? string.Empty,
                PromptContent: promptContent ?? string.Empty,
                McpServerCommand: mcpServerCommand ?? string.Empty,
                RequiredTokens: tokens,
                RecommendedRoles: roles,
                DownloadUrl: string.Empty,
                Checksum: string.Empty,
                PublishedAt: DateTime.MinValue,
                Downloads: 0,
                Rating: 0f,
                IsInstalled: !string.IsNullOrEmpty(installPath),
                InstallPath: installPath ?? string.Empty,
                RequiredPlugins: required,
                OptionalPlugins: optional
            );
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
}
