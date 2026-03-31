using OpenDesk.Core.Models;

namespace OpenDesk.SkillDiskette.Models
{
    /// <summary>
    /// Claude API 크래프팅 응답을 파싱한 결과.
    /// JSON 역직렬화 대상이므로 필드명은 camelCase 유지.
    /// </summary>
    [System.Serializable]
    public class CraftResult
    {
        public string skillName;
        public string description;
        public string promptContent;
        public string category;

        public SkillCategory ParseCategory()
        {
            if (string.IsNullOrEmpty(category))
                return SkillCategory.General;

            return System.Enum.TryParse<SkillCategory>(category, true, out var result)
                ? result
                : SkillCategory.General;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(skillName) &&
            !string.IsNullOrEmpty(promptContent);
    }
}
