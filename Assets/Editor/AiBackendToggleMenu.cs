using UnityEditor;
using UnityEngine;
using VContainer;

namespace OpenDesk.Editor
{
    /// <summary>
    /// AI 채팅 백엔드 토글 메뉴.
    /// MiddlewareChatService 가 PlayerPrefs `OpenDesk_ChatBackend` 키를 읽어 백엔드 결정.
    /// 변경 후 다음 Play 또는 SetProvider 호출 시 적용됨.
    ///
    /// Play 중에 전환하려면 "Set Live" 메뉴를 사용 — 활성 MiddlewareChatService 인스턴스를
    /// 찾아 SetProvider 호출.
    /// </summary>
    public static class AiBackendToggleMenu
    {
        private const string Key = "OpenDesk_ChatBackend";
        private const string MenuRoot = "OpenDesk/AI Backend";

        // ── 다음 Play 시 적용 (PlayerPrefs 만 변경) ─────────────

        [MenuItem(MenuRoot + "/Set Next Play: anthropic_cli (deprecated)")]
        public static void NextCli() => SetNext("anthropic_cli");

        [MenuItem(MenuRoot + "/Set Next Play: anthropic_api (BYOK)")]
        public static void NextApi() => SetNext("anthropic_api");

        [MenuItem(MenuRoot + "/Set Next Play: opendesk_routed (hosted credit)")]
        public static void NextRouted() => SetNext("opendesk_routed");

        private static void SetNext(string provider)
        {
            PlayerPrefs.SetString(Key, provider);
            PlayerPrefs.Save();
            Debug.Log($"[AiBackend] PlayerPrefs 갱신 — 다음 Play 부터 '{provider}' 적용");
        }

        // ── Play 중 즉시 적용 (live SetProvider) ─────────────────

        [MenuItem(MenuRoot + "/Set Live: anthropic_api (BYOK)", false, 30)]
        public static void LiveApi() => SetLive("anthropic_api");

        [MenuItem(MenuRoot + "/Set Live: opendesk_routed (hosted credit)", false, 31)]
        public static void LiveRouted() => SetLive("opendesk_routed");

        private static void SetLive(string provider)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[AiBackend] Set Live 는 Play 중에만 동작. PlayerPrefs 만 갱신합니다.");
                SetNext(provider);
                return;
            }

            // 1) PlayerPrefs 갱신 (다음 Play 도 유지)
            PlayerPrefs.SetString(Key, provider);
            PlayerPrefs.Save();

            // 2) MiddlewareChatService.SetProvider 를 VContainer 로 찾아 호출.
            //    이게 핵심 — _activeProvider 필드를 갱신해야 WS 재연결 시 도로 anthropic_api
            //    로 돌아가지 않는다 (HandleConnectionChanged 가 _activeProvider 를 재송신).
            var scopes = UnityEngine.Object.FindObjectsByType<VContainer.Unity.LifetimeScope>(FindObjectsSortMode.None);
            OpenDesk.Core.Implementations.MiddlewareChatService chatService = null;
            foreach (var scope in scopes)
            {
                if (scope.Container == null) continue;
                try
                {
                    var resolved = scope.Container.Resolve<OpenDesk.Core.Services.IAiChatService>();
                    if (resolved is OpenDesk.Core.Implementations.MiddlewareChatService mws)
                    {
                        chatService = mws;
                        break;
                    }
                }
                catch { /* 이 scope 에 미등록 — 다음 scope 시도 */ }
            }

            if (chatService == null)
            {
                Debug.LogWarning("[AiBackend] MiddlewareChatService 미발견. AgentOfficeScene 진입 후 다시 시도하세요.");
                return;
            }

            chatService.SetProvider(provider);
            Debug.Log($"[AiBackend] Live 전환 — MiddlewareChatService.SetProvider('{provider}') 완료. WS 재연결되어도 유지됩니다.");
        }

        // ── 진단 ─────────────────────────────────────────────────

        [MenuItem(MenuRoot + "/Show current backend", false, 100)]
        public static void ShowCurrent()
        {
            var current = PlayerPrefs.GetString(Key, "(default)");
            Debug.Log($"[AiBackend] PlayerPrefs 현재 설정: {current}");
        }

        [MenuItem(MenuRoot + "/Clear license JWT", false, 101)]
        public static void ClearJwt()
        {
            PlayerPrefs.DeleteKey("OpenDesk_License_Jwt");
            PlayerPrefs.DeleteKey("OpenDesk_License_RefreshToken");
            PlayerPrefs.DeleteKey("OpenDesk_License_UserId");
            PlayerPrefs.DeleteKey("OpenDesk_License_PlanTier");
            PlayerPrefs.Save();
            Debug.Log("[AiBackend] 라이선스 JWT 캐시 삭제 — 다음 Play 시 OnboardingScene 의 License step 재진입");
        }
    }
}
