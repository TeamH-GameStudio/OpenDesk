using System;
using UnityEngine;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// SkillCategory ↔ catalog.json 문자열 / 표시 이름 / 색상 매핑.
    /// catalog.json 의 "category" 필드는 소문자 케밥/스네이크 표기를 사용한다.
    /// </summary>
    public static class SkillCategoryExtensions
    {
        public static string ToSerializedKey(this SkillCategory category) => category switch
        {
            SkillCategory.Scheduling   => "scheduling",
            SkillCategory.Email        => "email",
            SkillCategory.Search       => "search",
            SkillCategory.Research     => "research",
            SkillCategory.Coding       => "coding",
            SkillCategory.Design       => "design",
            SkillCategory.Analytics    => "analytics",
            SkillCategory.Social       => "social",
            SkillCategory.Support      => "support",
            SkillCategory.Translation  => "translation",
            SkillCategory.Development  => "development",
            SkillCategory.Document     => "document",
            SkillCategory.Analysis     => "analysis",
            SkillCategory.ExternalTool => "external-tool",
            _                          => "general",
        };

        public static SkillCategory ParseCategory(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return SkillCategory.General;
            var key = raw.Trim().ToLowerInvariant();
            return key switch
            {
                "scheduling"     => SkillCategory.Scheduling,
                "email"          => SkillCategory.Email,
                "search"         => SkillCategory.Search,
                "research"       => SkillCategory.Research,
                "coding"         => SkillCategory.Coding,
                "design"         => SkillCategory.Design,
                "analytics"      => SkillCategory.Analytics,
                "social"         => SkillCategory.Social,
                "support"        => SkillCategory.Support,
                "translation"    => SkillCategory.Translation,
                "development"    => SkillCategory.Development,
                "document"       => SkillCategory.Document,
                "analysis"       => SkillCategory.Analysis,
                "external-tool"  => SkillCategory.ExternalTool,
                "externaltool"   => SkillCategory.ExternalTool,
                _                => SkillCategory.General,
            };
        }

        public static string DisplayName(this SkillCategory category) => category switch
        {
            SkillCategory.Scheduling   => "일정",
            SkillCategory.Email        => "이메일",
            SkillCategory.Search       => "검색",
            SkillCategory.Research     => "리서치",
            SkillCategory.Coding       => "코딩",
            SkillCategory.Design       => "디자인",
            SkillCategory.Analytics    => "분석",
            SkillCategory.Social       => "소셜",
            SkillCategory.Support      => "고객지원",
            SkillCategory.Translation  => "번역",
            SkillCategory.Development  => "개발",
            SkillCategory.Document     => "문서",
            SkillCategory.Analysis     => "데이터분석",
            SkillCategory.ExternalTool => "외부도구",
            _                          => "범용",
        };

        public static Color DisplayColor(this SkillCategory category) => category switch
        {
            SkillCategory.Scheduling   => new Color(0.95f, 0.55f, 0.25f),
            SkillCategory.Email        => new Color(0.45f, 0.70f, 1.00f),
            SkillCategory.Search       => new Color(0.40f, 0.85f, 0.65f),
            SkillCategory.Research     => new Color(0.65f, 0.55f, 0.95f),
            SkillCategory.Coding       => new Color(0.20f, 0.80f, 0.40f),
            SkillCategory.Design       => new Color(0.95f, 0.45f, 0.75f),
            SkillCategory.Analytics    => new Color(1.00f, 0.75f, 0.20f),
            SkillCategory.Social       => new Color(0.30f, 0.65f, 1.00f),
            SkillCategory.Support      => new Color(0.50f, 0.90f, 0.85f),
            SkillCategory.Translation  => new Color(0.85f, 0.65f, 0.40f),
            SkillCategory.Development  => new Color(0.25f, 0.80f, 0.50f),
            SkillCategory.Document     => new Color(0.30f, 0.50f, 1.00f),
            SkillCategory.Analysis     => new Color(1.00f, 0.70f, 0.20f),
            SkillCategory.ExternalTool => new Color(0.90f, 0.30f, 0.90f),
            _                          => new Color(0.55f, 0.85f, 1.00f),
        };
    }
}
