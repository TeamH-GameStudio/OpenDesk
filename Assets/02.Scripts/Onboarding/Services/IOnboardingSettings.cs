namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 온보딩 완료 여부 및 저장된 설정 관리
    /// PlayerPrefs 추상화 — 테스트에서 Mock 가능
    /// </summary>
    public interface IOnboardingSettings
    {
        bool   IsFirstRun      { get; }
        string SavedGatewayUrl { get; }
        string SavedLocalPath  { get; }
        int    AppVersion      { get; }  // 버전 마이그레이션용

        void MarkOnboardingComplete(string gatewayUrl, string localPath);
        void ClearAll();  // 설정 초기화 (재온보딩용)
    }
}
