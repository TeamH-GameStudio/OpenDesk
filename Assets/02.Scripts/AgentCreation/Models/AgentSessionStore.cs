using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// PlayerPrefs 기반 세션 저장/로드.
    /// 에이전트별로 복수 세션 관리, 활성 세션 추적.
    /// </summary>
    public static class AgentSessionStore
    {
        private const string KeyPrefix = "OpenDesk_Session_";
        private const string KeyCount  = "OpenDesk_SessionCount";
        private const string KeyActive = "OpenDesk_ActiveSession";

        // ================================================================
        //  생성
        // ================================================================

        /// <summary>새 세션 생성 + 저장, 인덱스 반환</summary>
        public static int CreateSession(int agentIndex, string agentName, AgentRole role)
        {
            int idx = PlayerPrefs.GetInt(KeyCount, 0);
            var now = DateTime.UtcNow.ToString("o");
            var sessionId = $"session_{idx}_{Guid.NewGuid():N}"[..20];

            var p = $"{KeyPrefix}{idx}_";
            PlayerPrefs.SetString(p + "SessionId",   sessionId);
            PlayerPrefs.SetInt(p    + "AgentIndex",   agentIndex);
            PlayerPrefs.SetString(p + "AgentName",    agentName);
            PlayerPrefs.SetInt(p    + "Role",         (int)role);
            PlayerPrefs.SetString(p + "Title",        "새 대화");
            PlayerPrefs.SetString(p + "LastMessage",  "");
            PlayerPrefs.SetString(p + "CreatedAt",    now);
            PlayerPrefs.SetString(p + "LastActivity", now);

            PlayerPrefs.SetInt(KeyCount, idx + 1);

            // 자동으로 활성 세션 설정
            SetActiveSession(idx);

            PlayerPrefs.Save();
            Debug.Log($"[SessionStore] 세션 생성: [{idx}] {agentName} - {sessionId}");
            return idx;
        }

        // ================================================================
        //  로드
        // ================================================================

        public static int Count => PlayerPrefs.GetInt(KeyCount, 0);

        public static AgentSession Load(int index)
        {
            var p = $"{KeyPrefix}{index}_";
            var sessionId = PlayerPrefs.GetString(p + "SessionId", "");
            if (string.IsNullOrEmpty(sessionId)) return null;

            var activeIdx = GetActiveSessionIndex();

            return new AgentSession
            {
                SessionId    = sessionId,
                AgentIndex   = PlayerPrefs.GetInt(p + "AgentIndex", 0),
                AgentName    = PlayerPrefs.GetString(p + "AgentName", ""),
                Role         = (AgentRole)PlayerPrefs.GetInt(p + "Role", 0),
                Title        = PlayerPrefs.GetString(p + "Title", "새 대화"),
                LastMessage  = PlayerPrefs.GetString(p + "LastMessage", ""),
                CreatedAt    = ParseDate(PlayerPrefs.GetString(p + "CreatedAt", "")),
                LastActivity = ParseDate(PlayerPrefs.GetString(p + "LastActivity", "")),
                IsActive     = index == activeIdx,
            };
        }

        public static List<AgentSession> LoadAll()
        {
            var list = new List<AgentSession>();
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                var s = Load(i);
                if (s != null) list.Add(s);
            }
            return list;
        }

        /// <summary>특정 에이전트의 세션만 로드</summary>
        public static List<AgentSession> LoadByAgent(int agentIndex)
        {
            var list = new List<AgentSession>();
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                var s = Load(i);
                if (s != null && s.AgentIndex == agentIndex)
                    list.Add(s);
            }
            return list;
        }

        // ================================================================
        //  업데이트
        // ================================================================

        public static void UpdateLastMessage(int index, string message)
        {
            var p = $"{KeyPrefix}{index}_";
            PlayerPrefs.SetString(p + "LastMessage", message);
            PlayerPrefs.SetString(p + "LastActivity", DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();
        }

        public static void UpdateTitle(int index, string title)
        {
            var p = $"{KeyPrefix}{index}_";
            PlayerPrefs.SetString(p + "Title", title);
            PlayerPrefs.Save();
        }

        // ================================================================
        //  활성 세션
        // ================================================================

        public static int GetActiveSessionIndex() => PlayerPrefs.GetInt(KeyActive, -1);

        public static void SetActiveSession(int index)
        {
            PlayerPrefs.SetInt(KeyActive, index);
            PlayerPrefs.Save();
        }

        public static AgentSession GetActiveSession()
        {
            int idx = GetActiveSessionIndex();
            return idx >= 0 ? Load(idx) : null;
        }

        // ================================================================
        //  삭제
        // ================================================================

        public static void ClearAll()
        {
            int count = Count;
            for (int i = 0; i < count; i++)
            {
                var p = $"{KeyPrefix}{i}_";
                PlayerPrefs.DeleteKey(p + "SessionId");
                PlayerPrefs.DeleteKey(p + "AgentIndex");
                PlayerPrefs.DeleteKey(p + "AgentName");
                PlayerPrefs.DeleteKey(p + "Role");
                PlayerPrefs.DeleteKey(p + "Title");
                PlayerPrefs.DeleteKey(p + "LastMessage");
                PlayerPrefs.DeleteKey(p + "CreatedAt");
                PlayerPrefs.DeleteKey(p + "LastActivity");
            }
            PlayerPrefs.DeleteKey(KeyCount);
            PlayerPrefs.DeleteKey(KeyActive);
            PlayerPrefs.Save();
        }

        // ================================================================
        //  유틸
        // ================================================================

        private static DateTime ParseDate(string iso)
        {
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt;
            return DateTime.UtcNow;
        }
    }
}
