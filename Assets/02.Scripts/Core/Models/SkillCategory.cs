namespace OpenDesk.Core.Models
{
    /// <summary>
    /// 스킬 디스켓 카테고리
    /// </summary>
    public enum SkillCategory
    {
        General,        // 범용 (번역, 요약 등)
        Development,    // 개발 (코드 리뷰, 디버깅)
        Document,       // 문서 (보고서, 기술문서)
        Analysis,       // 분석 (데이터, CSV)
        ExternalTool    // 외부 도구 (Notion, GitHub 등)
    }
}
