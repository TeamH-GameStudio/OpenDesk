namespace OpenDesk.Core.Models
{
    /// <summary>
    /// 스킬 카테고리. Claude Skills 형태를 차용한 10개 세분화 + 기존 호환 유지.
    /// catalog.json 의 category 필드는 소문자 문자열로 표기되며 SkillCategoryExtensions 로 매핑한다.
    /// </summary>
    public enum SkillCategory
    {
        General,        // 범용

        // 신규 — Claude Skills 형태 차용 (catalog.json 의 "category" 필드 기준)
        Scheduling,     // 일정/캘린더
        Email,          // 이메일
        Search,         // 검색/웹
        Research,       // 리서치/조사
        Coding,         // 코딩/개발 도구
        Design,         // 디자인/이미지
        Analytics,      // 분석/데이터
        Social,         // SNS/커뮤니티
        Support,        // 고객지원/CS
        Translation,    // 번역/언어

        // 레거시 호환 — SkillDiskette 자산에서 사용 중.
        // 새 코드는 위 신규 카테고리를 우선 사용할 것.
        Development,
        Document,
        Analysis,
        ExternalTool,
    }
}
