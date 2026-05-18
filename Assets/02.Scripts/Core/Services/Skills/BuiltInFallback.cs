using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 원격 catalog 실패 시 사용하는 빌트인 카탈로그.
    /// SkillMarketService.BuiltInCatalog 10개를 신규 SkillCatalog 형태로 마이그레이션.
    /// </summary>
    internal static class BuiltInFallback
    {
        public static SkillCatalog Build()
        {
            return new SkillCatalog
            {
                schemaVersion = "1.0",
                updatedAt = 0,
                skills = new List<SkillCatalogEntry>
                {
                    Entry("google-calendar", "Google Calendar", "일정 조회/생성/수정", "scheduling", 4.5f, 12000, new[] { "Planning", "Marketing" }),
                    Entry("web-search",      "Web Search",      "웹 검색 및 요약",       "search",     4.7f, 25000, new[] { "Research", "Development" }),
                    Entry("browser-control", "Browser Control", "브라우저 자동화",       "search",     4.3f, 8000,  new[] { "Research", "Marketing" }),
                    Entry("file-manager",    "File Manager",    "파일 읽기/쓰기/검색",   "research",   4.6f, 15000, new[] { "Development", "Research" }),
                    Entry("code-executor",   "Code Executor",   "Python/Node 코드 실행", "coding",     4.4f, 10000, new[] { "Development" }),
                    Entry("email-assistant", "Email Assistant", "이메일 읽기/작성",      "email",      4.1f, 5000,  new[] { "Planning", "Support", "Marketing" }),
                    Entry("notion-sync",     "Notion Sync",     "Notion 페이지 편집",    "research",   4.0f, 3000,  new[] { "Planning", "Research" }),
                    Entry("zapier-mcp",      "Zapier MCP",      "5000+ 앱 연동",         "scheduling", 4.2f, 7000,  new[] { "Planning" }),
                    Entry("image-gen",       "Image Generator", "이미지 생성",           "design",     3.9f, 6000,  new[] { "Design", "Marketing" }),
                    Entry("db-query",        "Database Query",  "SQL 쿼리/수정",         "analytics",  4.3f, 4000,  new[] { "Development", "Finance" }),
                },
                roleRecommendations = new List<RoleRecommendation>
                {
                    Recommend(AgentRole.Planning,    "scheduling", "email", "research"),
                    Recommend(AgentRole.Development, "coding", "search", "analytics"),
                    Recommend(AgentRole.Design,      "design", "social", "research"),
                    Recommend(AgentRole.Marketing,   "email", "social", "research"),
                    Recommend(AgentRole.Research,    "research", "search", "analytics"),
                    Recommend(AgentRole.Support,     "support", "email", "translation"),
                    Recommend(AgentRole.Legal,       "research", "translation"),
                    Recommend(AgentRole.Finance,     "analytics", "research"),
                },
            };
        }

        private static SkillCatalogEntry Entry(
            string id, string name, string desc, string category,
            float rating, int downloads, string[] roles)
        {
            return new SkillCatalogEntry
            {
                id = id,
                displayName = name,
                description = desc,
                category = category,
                version = "1.0.0",
                author = "opendesk",
                iconUrl = string.Empty,
                downloadUrl = string.Empty,
                checksum = string.Empty,
                publishedAt = 0,
                downloads = downloads,
                rating = rating,
                recommendedRoles = new List<string>(roles),
            };
        }

        private static RoleRecommendation Recommend(AgentRole role, params string[] categories)
            => new() { role = role.ToString(), categories = new List<string>(categories) };
    }
}
