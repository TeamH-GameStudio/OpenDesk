using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 원격 catalog 실패 시 사용하는 빌트인 스킬 카탈로그.
    /// v4: 스킬 = "동료의 역할/워크플로" (Marketing Manager, PM, Researcher 등) — 행동 지침만 담음.
    /// 도구(Gmail, Notion, Calendar 등) 는 BuiltInPluginFallback 으로 분리되어 PluginCatalog 에 등록된다.
    /// "스킬" 과 "플러그인" 두 단어가 절대 섞이지 않도록 데이터 분류부터 분리한다.
    /// </summary>
    internal static class BuiltInFallback
    {
        public static SkillCatalog Build()
        {
            return new SkillCatalog
            {
                // SkillCatalogService.CurrentSchemaVersion 과 일치해야 캐시 invalidation 이 의도대로 동작.
                schemaVersion = "2.0-v4",
                updatedAt = 0,
                skills = new List<SkillCatalogEntry>
                {
                    // 역할 기반 스킬 — Claude Design v4 §A 의 "역할 · 사람 느낌" 카드들.
                    Entry("marketing-mgr",  "마케팅 매니저",      "캠페인 기획부터 이메일 발송, 일정 조율까지", "scheduling", 4.7f, 8200,
                          new[] { "Marketing" },
                          required: new[] { "gmail", "google-calendar" }, optional: new[] { "notion", "slack" }),
                    Entry("pm",             "프로덕트 매니저",    "스펙 정리 · 이슈 추적 · 회의 노트",         "scheduling", 4.8f, 11200,
                          new[] { "Planning" },
                          required: new[] { "linear", "google-calendar", "notion" }, optional: new[] { "slack", "gmail" }),
                    Entry("dev-mentor",     "개발자 멘토",        "코드 리뷰 · 디버깅 · 학습 가이드",          "coding",     4.6f, 6900,
                          new[] { "Development" },
                          required: new[] { "web-search" }, optional: new[] { "file-manager" }),
                    Entry("researcher",     "리서치 어시스턴트",  "주제 조사 · 출처 정리 · 요약 노트",         "research",   4.7f, 9300,
                          new[] { "Research" },
                          required: new[] { "web-search" }, optional: new[] { "notion" }),
                    Entry("sales-mgr",      "영업 매니저",        "리드 메일 작성 · 미팅 잡기",                "email",      4.4f, 3200,
                          new[] { "Marketing" },
                          required: new[] { "gmail", "google-calendar" }, optional: new[] { "notion" }),
                    Entry("meeting-recap",  "회의 정리",          "회의록 → 액션아이템 → 채널 공유",           "research",   4.6f, 5800,
                          new[] { "Planning" },
                          required: new[] { "notion", "slack" }, optional: new[] { "linear", "google-calendar" }),
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
            float rating, int downloads, string[] roles,
            string[] required = null, string[] optional = null)
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
                // v4 의존성 선언 — SkillMarketView 설치 모달에서 "함께 설치" 흐름의 트리거.
                requiredPlugins = required != null ? new List<string>(required) : new List<string>(),
                optionalPlugins = optional != null ? new List<string>(optional) : new List<string>(),
            };
        }

        private static RoleRecommendation Recommend(AgentRole role, params string[] categories)
            => new() { role = role.ToString(), categories = new List<string>(categories) };
    }
}
