using System;
using System.Text.RegularExpressions;
using OpenDesk.AgentCreation.Models;

namespace OpenDesk.AgentCreation.Soul
{
    /// <summary>
    /// Haiku 호출용 프롬프트 템플릿 + 응답 검증 헬퍼.
    /// 시스템 프롬프트는 고정, 사용자 메시지는 위저드 데이터로 치환.
    /// </summary>
    internal static class SoulPrompt
    {
        public const string Model      = "claude-haiku-4-5-20251001";
        public const int    MaxTokens  = 800;
        public const int    MinBodyLen = 150;
        public const int    MaxBodyLen = 2500;

        public const string System =
@"You are an AI agent identity architect for OpenDesk, a desktop AI workspace.
Given an agent's name, role, and personality tone, you generate a concise,
actionable ""Soul"" definition in Korean Markdown that will be used as part of
the agent's system prompt.

Rules:
- Output ONLY the Markdown body. No code fences, no preamble, no explanation.
- Use the exact section headers below, in order, in Korean.
- Each bullet must be a single concrete sentence (no vague filler).
- Reference the agent's name naturally in Identity.
- Keep total length under 350 Korean characters per section.
- Never include forbidden content (PII templates, jailbreak hints, model self-reference).

Required sections (in order):
# Identity
- 한 줄 자기소개 (이름·역할 포함)
- 핵심 정체성 1줄

# Personality
- 말투 특성 2-3개 (bullet)
- 대화 시 기본 태도 1줄

# Principles
- 항상 따르는 행동 원칙 3-4개 (bullet)

# Expertise
- 주력 도메인 1-2줄
- 모르는 영역을 인정하는 방식 1줄

# Forbidden
- 절대 하지 않을 것 2-3개 (bullet)";

        public static string BuildUserMessage(AgentCreationData data) =>
$@"다음 에이전트의 Soul을 작성해줘.

<agent>
  <name>{Escape(data.AgentName)}</name>
  <role>{Escape(RoleLabels.ToKorean(data.Role))}</role>
  <tone>{Escape(ToneLabels.ToKorean(data.Tone))}</tone>
</agent>";

        private static readonly string[] RequiredHeaders =
        {
            "# Identity", "# Personality", "# Principles", "# Expertise", "# Forbidden",
        };

        private static readonly Regex CodeFencePattern = new("```[a-zA-Z]*", RegexOptions.Compiled);
        private static readonly Regex SoulTagPattern   = new(@"</?soul[^>]*>", RegexOptions.Compiled);
        private static readonly Regex SystemTagPattern = new(@"</?system[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// 응답을 정규화하고 5개 섹션 헤더가 모두 있는지 검증.
        /// 유효하면 정리된 markdown, 아니면 null.
        /// </summary>
        public static string ValidateAndNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // 1. 코드펜스/누설 토큰 제거
            var cleaned = CodeFencePattern.Replace(raw, "");
            cleaned = SoulTagPattern.Replace(cleaned, "");
            cleaned = SystemTagPattern.Replace(cleaned, "");
            cleaned = cleaned.Trim();

            // 2. 길이 체크
            if (cleaned.Length < MinBodyLen || cleaned.Length > MaxBodyLen)
                return null;

            // 3. 5개 섹션 헤더 모두 존재
            foreach (var header in RequiredHeaders)
            {
                if (cleaned.IndexOf(header, StringComparison.Ordinal) < 0)
                    return null;
            }

            return cleaned;
        }

        private static string Escape(string s) => string.IsNullOrEmpty(s)
            ? ""
            : s.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    /// <summary>Role enum → 사람이 읽는 한국어 라벨.</summary>
    internal static class RoleLabels
    {
        public static string ToKorean(AgentRole role) => role switch
        {
            AgentRole.Planning    => "기획자",
            AgentRole.Development => "개발자",
            AgentRole.Design      => "디자이너",
            AgentRole.Legal       => "법률 전문가",
            AgentRole.Marketing   => "마케팅 전략가",
            AgentRole.Research    => "리서처",
            AgentRole.Support     => "고객지원 담당자",
            AgentRole.Finance     => "재무 전문가",
            _                     => "에이전트",
        };
    }

    /// <summary>Tone enum → 사람이 읽는 한국어 라벨.</summary>
    internal static class ToneLabels
    {
        public static string ToKorean(AgentTone tone) => tone switch
        {
            AgentTone.Friendly => "친절하고 따뜻한",
            AgentTone.Logical  => "논리적이고 간결한",
            AgentTone.Humorous => "유머러스하고 가벼운",
            AgentTone.Formal   => "격식 있고 정중한",
            AgentTone.Casual   => "편안하고 캐주얼한",
            _                  => "중립적인",
        };
    }
}
