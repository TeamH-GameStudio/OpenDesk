using System;
using System.Collections.Generic;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 제작 위저드에서 수집하는 설정 데이터.
    /// SKILL.md frontmatter 구조와 대응:
    ///   AgentName       → name
    ///   Role            → description (역할 키워드)
    ///   AIModel         → model
    ///   Tone            → 본문 persona
    ///   AllowedTools    → allowed-tools
    ///   ExecutionContext → context (fork/inline)
    ///   ArgumentHint    → argument-hint
    ///   EquippedSkills  → 장착된 스킬 목록
    ///   CustomPrompt    → 추가 system prompt
    /// </summary>
    [Serializable]
    public class AgentCreationData
    {
        // ── 기본 (위저드 Step 1~5) ──────────────────────────
        // Step 1
        public string AgentName = "";

        // Step 2
        public AgentRole Role = AgentRole.None;

        // Step 3
        public AgentAIModel AIModel = AgentAIModel.None;

        // Step 4
        public AgentTone Tone = AgentTone.None;

        // Step 5
        public string AvatarPrefabName = "";

        // ── 확장 (추후 UI 추가 예정) ────────────────────────

        /// <summary>사용 가능 도구 제한 (SKILL.md allowed-tools 대응)</summary>
        public List<string> AllowedTools = new();

        /// <summary>실행 컨텍스트 — "fork"면 독립 서브에이전트 (SKILL.md context 대응)</summary>
        public string ExecutionContext = "";

        /// <summary>인자 힌트 (SKILL.md argument-hint 대응)</summary>
        public string ArgumentHint = "";

        /// <summary>장착된 스킬 ID 목록 (스킬 마켓에서 장착)</summary>
        public List<string> EquippedSkills = new();

        /// <summary>사용자 커스텀 system prompt (추가 지시사항)</summary>
        public string CustomPrompt = "";

        /// <summary>최대 스킬 슬롯 수</summary>
        public int MaxSkillSlots = 3;

        // ── 유효성 ──────────────────────────────────────────

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(AgentName) &&
            Role != AgentRole.None &&
            AIModel != AgentAIModel.None &&
            Tone != AgentTone.None &&
            !string.IsNullOrWhiteSpace(AvatarPrefabName);

        public void Reset()
        {
            AgentName = "";
            Role = AgentRole.None;
            AIModel = AgentAIModel.None;
            Tone = AgentTone.None;
            AvatarPrefabName = "";
            AllowedTools.Clear();
            ExecutionContext = "";
            ArgumentHint = "";
            EquippedSkills.Clear();
            CustomPrompt = "";
            MaxSkillSlots = 3;
        }

        // ── 디버그 출력 ─────────────────────────────────────

        public string ToDebugString()
        {
            var tools = AllowedTools.Count > 0 ? string.Join(", ", AllowedTools) : "(제한 없음)";
            var skills = EquippedSkills.Count > 0 ? string.Join(", ", EquippedSkills) : "(없음)";

            return $"=== 에이전트 프로필 ===\n" +
                   $"  이름: {AgentName}\n" +
                   $"  역할: {Role}\n" +
                   $"  AI 모델: {AIModel}\n" +
                   $"  말투: {Tone}\n" +
                   $"  아바타: {AvatarPrefabName}\n" +
                   $"  허용 도구: {tools}\n" +
                   $"  실행 컨텍스트: {(string.IsNullOrEmpty(ExecutionContext) ? "(기본)" : ExecutionContext)}\n" +
                   $"  인자 힌트: {(string.IsNullOrEmpty(ArgumentHint) ? "(없음)" : ArgumentHint)}\n" +
                   $"  장착 스킬: {skills}\n" +
                   $"  커스텀 프롬프트: {(string.IsNullOrEmpty(CustomPrompt) ? "(없음)" : CustomPrompt[..System.Math.Min(50, CustomPrompt.Length)] + "...")}\n" +
                   $"  스킬 슬롯: {EquippedSkills.Count}/{MaxSkillSlots}";
        }
    }
}
