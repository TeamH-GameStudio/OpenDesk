using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트의 영혼(Soul) — 성격, 행동 원칙, 대화 스타일, 전문성을 구조화한 SO.
    /// AgentProfileSO에 연결되어 system prompt의 핵심 인격 레이어를 구성.
    /// Resources/Souls/ 에 역할별 프리셋 배치.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAgentSoul", menuName = "OpenDesk/Agent Soul")]
    public class AgentSoul : ScriptableObject
    {
        [Header("식별")]
        [SerializeField] private AgentRole _targetRole = AgentRole.None;
        [SerializeField] private AgentTone _targetTone = AgentTone.None;

        [Header("핵심 정체성")]
        [SerializeField, TextArea(3, 8)] private string _identity;

        [Header("성격 특성")]
        [SerializeField, TextArea(3, 10)] private string _personalityTraits;

        [Header("대화 스타일")]
        [SerializeField, TextArea(3, 10)] private string _communicationStyle;

        [Header("행동 원칙")]
        [SerializeField, TextArea(3, 10)] private string _behaviorRules;

        [Header("전문 지식")]
        [SerializeField, TextArea(3, 10)] private string _domainExpertise;

        [Header("감정 모델")]
        [SerializeField, TextArea(3, 8)] private string _emotionalModel;

        // ── 프로퍼티 ──
        public AgentRole TargetRole => _targetRole;
        public AgentTone TargetTone => _targetTone;
        public string Identity => _identity;
        public string PersonalityTraits => _personalityTraits;
        public string CommunicationStyle => _communicationStyle;
        public string BehaviorRules => _behaviorRules;
        public string DomainExpertise => _domainExpertise;
        public string EmotionalModel => _emotionalModel;

        /// <summary>Soul 전체를 XML 구조 시스템 프롬프트로 변환</summary>
        public string ToSystemPromptBlock()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<soul>");

            AppendSection(sb, "identity", _identity);
            AppendSection(sb, "personality", _personalityTraits);
            AppendSection(sb, "communication-style", _communicationStyle);
            AppendSection(sb, "behavior-rules", _behaviorRules);
            AppendSection(sb, "domain-expertise", _domainExpertise);
            AppendSection(sb, "emotional-model", _emotionalModel);

            sb.AppendLine("</soul>");
            return sb.ToString();
        }

        private static void AppendSection(
            System.Text.StringBuilder sb, string tag, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            sb.AppendLine($"  <{tag}>");
            sb.AppendLine($"  {content.Trim()}");
            sb.AppendLine($"  </{tag}>");
        }

        /// <summary>역할+톤 조합 키로 Resources에서 로드</summary>
        public static AgentSoul LoadFor(AgentRole role, AgentTone tone)
        {
            // 1순위: 역할+톤 조합
            var combined = Resources.Load<AgentSoul>($"Souls/Soul_{role}_{tone}");
            if (combined != null) return combined;

            // 2순위: 역할 기본
            var roleDefault = Resources.Load<AgentSoul>($"Souls/Soul_{role}");
            if (roleDefault != null) return roleDefault;

            // 3순위: 톤 기본
            var toneDefault = Resources.Load<AgentSoul>($"Souls/Soul_{tone}");
            if (toneDefault != null) return toneDefault;

            // 없으면 null — 기존 심플 프롬프트로 폴백
            return null;
        }
    }
}
