namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 사용자 연령대 선택지. 스펙: 10대 / 20대 / 30대 / 40대 / 50대+ / 비공개.
    /// </summary>
    public enum AgeBucket
    {
        Teens = 0,
        Twenties = 1,
        Thirties = 2,
        Forties = 3,
        FiftiesPlus = 4,
        Undisclosed = 5,
    }
}
