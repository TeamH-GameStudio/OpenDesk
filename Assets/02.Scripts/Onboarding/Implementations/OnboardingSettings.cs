using OpenDesk.Onboarding.Services;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// PlayerPrefs 기반 온보딩 설정 저장/복원
    /// IOnboardingSettings 추상화로 테스트에서 Mock 가능
    /// </summary>
    public class OnboardingSettings : IOnboardingSettings
    {
        private const string Key_IsFirstRun   = "OpenDesk_IsFirstRun";
        private const string Key_GatewayUrl   = "OpenDesk_GatewayUrl";
        private const string Key_LocalPath    = "OpenDesk_LocalPath";
        private const string Key_AppVersion   = "OpenDesk_AppVersion";

        private const int CurrentAppVersion   = 1;

        public bool   IsFirstRun      => PlayerPrefs.GetInt(Key_IsFirstRun, 1) == 1;
        public string SavedGatewayUrl => PlayerPrefs.GetString(Key_GatewayUrl, "ws://localhost:18789/events");
        public string SavedLocalPath  => PlayerPrefs.GetString(Key_LocalPath, "");
        public int    AppVersion      => PlayerPrefs.GetInt(Key_AppVersion, 0);

        public void MarkOnboardingComplete(string gatewayUrl, string localPath)
        {
            PlayerPrefs.SetInt(Key_IsFirstRun,   0);
            PlayerPrefs.SetString(Key_GatewayUrl, gatewayUrl ?? "");
            PlayerPrefs.SetString(Key_LocalPath,  localPath  ?? "");
            PlayerPrefs.SetInt(Key_AppVersion,    CurrentAppVersion);
            PlayerPrefs.Save();
        }

        public void ClearAll()
        {
            PlayerPrefs.DeleteKey(Key_IsFirstRun);
            PlayerPrefs.DeleteKey(Key_GatewayUrl);
            PlayerPrefs.DeleteKey(Key_LocalPath);
            PlayerPrefs.DeleteKey(Key_AppVersion);
            PlayerPrefs.Save();
        }
    }
}
