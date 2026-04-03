using System.Collections.Generic;
using UnityEngine;
using OpenDesk.Core.Models;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 스킬 디스켓 ScriptableObject.
    /// 에이전트에 장착하여 system prompt에 주입되는 스킬 데이터.
    /// 프리셋(에디터 생성) 또는 런타임 크래프팅으로 생성 가능.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillDiskette", menuName = "OpenDesk/Skill Diskette")]
    public class SkillDiskette : ScriptableObject
    {
        [Header("기본 정보")]
        [SerializeField] private string _skillId;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(3, 10)] private string _description;
        [SerializeField] private SkillCategory _category;

        [Header("프롬프트")]
        [SerializeField, TextArea(5, 20)] private string _promptContent;

        [Header("외형")]
        [SerializeField] private Sprite _icon;
        [SerializeField] private Color _color = new Color(0.5f, 0.9f, 1.0f);

        [Header("크래프팅 정보")]
        [SerializeField] private bool _isCustomCrafted;
        [SerializeField, TextArea(2, 5)] private string _craftPrompt;

        [Header("외부 도구 (선택)")]
        [SerializeField] private string _mcpServerCommand;
        [SerializeField] private List<string> _requiredTokens = new();

        // ── 프로퍼티 ──────────────────────────────────────

        public string SkillId => _skillId;
        public string DisplayName => _displayName;
        public string Description => _description;
        public SkillCategory Category => _category;
        public string PromptContent => _promptContent;
        public Sprite Icon => _icon;
        public Color Color => _color;
        public bool IsCustomCrafted => _isCustomCrafted;
        public string CraftPrompt => _craftPrompt;
        public string McpServerCommand => _mcpServerCommand;
        public List<string> RequiredTokens => _requiredTokens;
        public bool HasExternalTool => !string.IsNullOrEmpty(_mcpServerCommand);

        // ── 런타임 생성 ───────────────────────────────────

        /// <summary>
        /// 크래프팅 결과로 런타임 SkillDiskette 생성
        /// </summary>
        public static SkillDiskette CreateRuntime(
            string skillId,
            string displayName,
            string description,
            SkillCategory category,
            string promptContent,
            bool isCustomCrafted = false,
            string craftPrompt = null)
        {
            var so = CreateInstance<SkillDiskette>();
            so._skillId = skillId;
            so._displayName = displayName;
            so._description = description;
            so._category = category;
            so._promptContent = promptContent;
            so._isCustomCrafted = isCustomCrafted;
            so._craftPrompt = craftPrompt;
            so._color = GetCategoryColor(category);
            so.name = displayName;
            return so;
        }

        private static Color GetCategoryColor(SkillCategory cat) => cat switch
        {
            SkillCategory.Development  => new Color(0.2f, 0.8f, 0.4f),
            SkillCategory.Document     => new Color(0.3f, 0.5f, 1.0f),
            SkillCategory.Analysis     => new Color(1.0f, 0.7f, 0.2f),
            SkillCategory.ExternalTool => new Color(0.9f, 0.3f, 0.9f),
            _                          => new Color(0.5f, 0.9f, 1.0f),
        };
    }
}
