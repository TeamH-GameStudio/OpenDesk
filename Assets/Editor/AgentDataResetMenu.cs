using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenDesk.EditorTools
{
    /// <summary>
    /// 에이전트 관련 영속 데이터 초기화 유틸.
    /// 메뉴: Tools > OpenDesk > Reset Agent Data.
    ///
    /// 삭제 대상:
    ///   - Application.persistentDataPath/agents/                  (AgentDraftJsonStore — 위저드 저장 에이전트)
    ///   - Application.persistentDataPath/OpenDesk/Conversations/  (ChatMessageStore   — 세션별 채팅 로그)
    ///   - PlayerPrefs: OpenDesk_Session_*, OpenDesk_SessionCount, OpenDesk_ActiveSession (AgentSessionStore)
    ///   - PlayerPrefs: OpenDesk_AgentDraft_* / OpenDesk_AgentRoster (구 위저드 호환 키)
    ///
    /// 보존:
    ///   - 와드로브 카탈로그 / 스킬 카탈로그 캐시 / API 키 / Backend 토글
    /// </summary>
    public static class AgentDataResetMenu
    {
        private const string MenuPath = "Tools/OpenDesk/Reset Agent Data";

        [MenuItem(MenuPath)]
        public static void ResetAgentData()
        {
            var summary = BuildSummary();
            var ok = EditorUtility.DisplayDialog(
                "에이전트 데이터 초기화",
                "다음 데이터를 모두 삭제합니다 (되돌릴 수 없음):\n\n" + summary,
                "초기화", "취소");
            if (!ok)
            {
                Debug.Log("[ResetAgentData] 사용자 취소");
                return;
            }

            int deletedFiles = 0;
            int deletedKeys  = 0;

            // 1) 에이전트 JSON 파일들 삭제
            var agentsDir = Path.Combine(Application.persistentDataPath, "agents");
            deletedFiles += DeleteDirectoryContents(agentsDir);

            // 2) 채팅 대화 파일들 삭제
            var conversationsDir = Path.Combine(Application.persistentDataPath, "OpenDesk", "Conversations");
            deletedFiles += DeleteDirectoryContents(conversationsDir);

            // 3) PlayerPrefs 세션 키들 삭제
            int sessionCount = PlayerPrefs.GetInt("OpenDesk_SessionCount", 0);
            for (int i = 0; i < sessionCount; i++)
            {
                var p = $"OpenDesk_Session_{i}_";
                foreach (var suffix in new[]
                {
                    "SessionId", "AgentIndex", "AgentName", "Role",
                    "Title", "LastMessage", "CreatedAt", "LastActivity",
                })
                {
                    var key = p + suffix;
                    if (PlayerPrefs.HasKey(key))
                    {
                        PlayerPrefs.DeleteKey(key);
                        deletedKeys++;
                    }
                }
            }

            foreach (var key in new[] { "OpenDesk_SessionCount", "OpenDesk_ActiveSession" })
            {
                if (PlayerPrefs.HasKey(key))
                {
                    PlayerPrefs.DeleteKey(key);
                    deletedKeys++;
                }
            }

            // 4) 구 위저드 호환 키 (있다면 정리)
            foreach (var legacyKey in new[]
            {
                "OpenDesk_AgentRoster",
                "OpenDesk_AgentDraft",
                "OpenDesk_LastAgentId",
            })
            {
                if (PlayerPrefs.HasKey(legacyKey))
                {
                    PlayerPrefs.DeleteKey(legacyKey);
                    deletedKeys++;
                }
            }

            PlayerPrefs.Save();

            Debug.Log($"[ResetAgentData] 완료 — 파일 {deletedFiles}개, PlayerPrefs 키 {deletedKeys}개 삭제");
            EditorUtility.DisplayDialog(
                "에이전트 데이터 초기화 완료",
                $"파일 {deletedFiles}개\nPlayerPrefs 키 {deletedKeys}개\n삭제했습니다.",
                "확인");
        }

        [MenuItem("Tools/OpenDesk/Reveal Agent Data Folder")]
        public static void RevealFolder()
        {
            var root = Application.persistentDataPath;
            if (!Directory.Exists(root))
            {
                Debug.LogWarning($"[ResetAgentData] persistentDataPath 미존재: {root}");
                return;
            }
            EditorUtility.RevealInFinder(root);
        }

        // ────────────────────────────────────────────────────────────

        private static string BuildSummary()
        {
            var agentsDir = Path.Combine(Application.persistentDataPath, "agents");
            var convDir   = Path.Combine(Application.persistentDataPath, "OpenDesk", "Conversations");
            int agentFiles = CountJsonFiles(agentsDir);
            int convFiles  = CountJsonFiles(convDir);
            int sessionCount = PlayerPrefs.GetInt("OpenDesk_SessionCount", 0);

            return
                $"• 저장된 에이전트: {agentFiles}개\n  {agentsDir}\n\n" +
                $"• 채팅 세션 파일: {convFiles}개\n  {convDir}\n\n" +
                $"• PlayerPrefs 세션 슬롯: {sessionCount}개";
        }

        private static int CountJsonFiles(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetFiles(dir, "*.json").Length;
        }

        private static int DeleteDirectoryContents(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            int count = 0;
            try
            {
                foreach (var path in Directory.GetFiles(dir, "*.json"))
                {
                    File.Delete(path);
                    count++;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ResetAgentData] {dir} 삭제 중 오류: {ex.Message}");
            }
            return count;
        }
    }
}
