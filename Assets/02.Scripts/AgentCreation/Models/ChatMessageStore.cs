using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    // ── 메시지 모델 ─────────────────────────────────────────

    public enum ChatSender { User, Agent, System }

    [Serializable]
    public class ChatMessage
    {
        public ChatSender Sender;
        public string Text;
        public string Timestamp; // ISO 8601

        public DateTime Time =>
            DateTime.TryParse(Timestamp, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : DateTime.UtcNow;

        /// <summary>Claude CLI 호환 role 문자열</summary>
        public string Role => Sender switch
        {
            ChatSender.User => "user",
            ChatSender.Agent => "assistant",
            ChatSender.System => "system",
            _ => "user"
        };
    }

    // ── 세션 파일 모델 (JSON 직렬화) ───────────────────────

    [Serializable]
    public class ConversationFile
    {
        public string SessionId;
        public string AgentName;
        public string CreatedAt;
        public List<ChatMessage> Messages = new();
    }

    // ── 저장소 ──────────────────────────────────────────────

    /// <summary>
    /// 로컬 JSON 파일 기반 세션별 채팅 메시지 저장.
    /// 경로: {persistentDataPath}/OpenDesk/Conversations/{sessionId}.json
    /// Claude CLI가 직접 읽어 대화를 이어나갈 수 있는 포맷.
    /// </summary>
    public static class ChatMessageStore
    {
        private const int MaxMessages = 500;

        private static string _conversationsDir;

        /// <summary>대화 저장 폴더 경로</summary>
        public static string ConversationsDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_conversationsDir))
                {
                    _conversationsDir = Path.Combine(
                        Application.persistentDataPath, "OpenDesk", "Conversations");
                    if (!Directory.Exists(_conversationsDir))
                        Directory.CreateDirectory(_conversationsDir);
                }
                return _conversationsDir;
            }
        }

        /// <summary>세션 JSON 파일 경로</summary>
        public static string GetFilePath(string sessionId)
            => Path.Combine(ConversationsDirectory, $"{sessionId}.json");

        // ================================================================
        //  로드
        // ================================================================

        public static List<ChatMessage> Load(string sessionId)
        {
            var path = GetFilePath(sessionId);
            if (!File.Exists(path)) return new List<ChatMessage>();

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonUtility.FromJson<ConversationFile>(json);
                return file?.Messages ?? new List<ChatMessage>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatMessageStore] 로드 실패: {sessionId} — {ex.Message}");
                return new List<ChatMessage>();
            }
        }

        /// <summary>전체 ConversationFile 로드 (세션 메타 포함)</summary>
        public static ConversationFile LoadConversationFile(string sessionId)
        {
            var path = GetFilePath(sessionId);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<ConversationFile>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatMessageStore] 파일 로드 실패: {sessionId} — {ex.Message}");
                return null;
            }
        }

        // ================================================================
        //  추가
        // ================================================================

        public static void Append(string sessionId, ChatSender sender, string text,
            string agentName = "")
        {
            var path = GetFilePath(sessionId);
            ConversationFile file;

            if (File.Exists(path))
            {
                try
                {
                    var existingJson = File.ReadAllText(path);
                    file = JsonUtility.FromJson<ConversationFile>(existingJson)
                           ?? CreateNewFile(sessionId, agentName);
                }
                catch
                {
                    file = CreateNewFile(sessionId, agentName);
                }
            }
            else
            {
                file = CreateNewFile(sessionId, agentName);
            }

            file.Messages.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                Timestamp = DateTime.UtcNow.ToString("o"),
            });

            // 최대 메시지 수 제한
            while (file.Messages.Count > MaxMessages)
                file.Messages.RemoveAt(0);

            SaveFile(path, file);

            // 세션 미리보기 업데이트
            UpdateSessionPreview(sessionId, sender, text);
        }

        // ================================================================
        //  삭제
        // ================================================================

        public static void Clear(string sessionId)
        {
            var path = GetFilePath(sessionId);
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ChatMessageStore] 삭제 실패: {ex.Message}");
                }
            }
        }

        // ================================================================
        //  내부
        // ================================================================

        private static ConversationFile CreateNewFile(string sessionId, string agentName)
        {
            return new ConversationFile
            {
                SessionId = sessionId,
                AgentName = agentName,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                Messages = new List<ChatMessage>(),
            };
        }

        private static void SaveFile(string path, ConversationFile file)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonUtility.ToJson(file, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatMessageStore] 저장 실패: {ex.Message}");
            }
        }

        private static void UpdateSessionPreview(string sessionId, ChatSender sender, string text)
        {
            int count = AgentSessionStore.Count;
            for (int i = 0; i < count; i++)
            {
                var s = AgentSessionStore.Load(i);
                if (s != null && s.SessionId == sessionId)
                {
                    var preview = sender == ChatSender.User ? text : $"AI: {text}";
                    if (preview.Length > 50) preview = preview[..47] + "...";
                    AgentSessionStore.UpdateLastMessage(i, preview);

                    if (s.Title == "새 대화" && sender == ChatSender.User)
                    {
                        var title = text.Length > 25 ? text[..22] + "..." : text;
                        AgentSessionStore.UpdateTitle(i, title);
                    }
                    break;
                }
            }
        }
    }
}
