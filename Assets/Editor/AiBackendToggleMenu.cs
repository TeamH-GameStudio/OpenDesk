using UnityEditor;
using UnityEngine;

namespace OpenDesk.Editor
{
    /// <summary>
    /// AI 채팅 백엔드 토글 메뉴.
    /// AgentOfficeInstaller가 PlayerPrefs `OpenDesk_ChatBackend` 키를 읽어 백엔드 결정.
    /// 변경 후 다음 Play 시 적용됨.
    /// </summary>
    public static class AiBackendToggleMenu
    {
        private const string Key = "OpenDesk_ChatBackend";
        private const string MenuRoot = "OpenDesk/AI Backend";

        [MenuItem(MenuRoot + "/Use CLI (Python middleware + Claude CLI)")]
        public static void UseCli()
        {
            PlayerPrefs.SetString(Key, "cli");
            PlayerPrefs.Save();
            Debug.Log("[AiBackend] CLI 백엔드 활성 — 다음 Play에서 Python 미들웨어 + Claude CLI 사용");
        }

        [MenuItem(MenuRoot + "/Use API (Anthropic HTTP direct)")]
        public static void UseApi()
        {
            PlayerPrefs.SetString(Key, "api");
            PlayerPrefs.Save();
            Debug.Log("[AiBackend] API 백엔드 활성 — 다음 Play에서 Anthropic Messages API 직접 호출");
            Debug.Log("[AiBackend] 키 설정 필요: ApiKeyVault에 'anthropic' 키 저장하거나 ApiKeyVaultService.SaveKeyAsync(\"anthropic\", \"sk-ant-...\")");
        }

        [MenuItem(MenuRoot + "/Show current backend")]
        public static void ShowCurrent()
        {
            var current = PlayerPrefs.GetString(Key, "cli");
            Debug.Log($"[AiBackend] 현재 설정: {current}");
        }
    }
}
