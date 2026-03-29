using System;
using System.Collections.Generic;
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
    }

    // ── 저장소 ──────────────────────────────────────────────

    /// <summary>
    /// PlayerPrefs 기반 세션별 채팅 메시지 저장.
    /// JSON 배열로 직렬화하여 키 하나에 보관.
    /// </summary>
    public static class ChatMessageStore
    {
        private const string KeyPrefix = "OpenDesk_Chat_";
        private const int MaxMessages = 200;

        // ================================================================
        //  로드
        // ================================================================

        public static List<ChatMessage> Load(string sessionId)
        {
            var key = KeyPrefix + sessionId;
            var json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return new List<ChatMessage>();

            var wrapper = JsonUtility.FromJson<MessageListWrapper>(json);
            return wrapper?.Messages ?? new List<ChatMessage>();
        }

        // ================================================================
        //  추가
        // ================================================================

        public static void Append(string sessionId, ChatSender sender, string text)
        {
            var list = Load(sessionId);

            list.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                Timestamp = DateTime.UtcNow.ToString("o"),
            });

            // 최대 메시지 수 제한
            while (list.Count > MaxMessages)
                list.RemoveAt(0);

            Save(sessionId, list);

            // 세션 마지막 메시지도 업데이트
            UpdateSessionPreview(sessionId, sender, text);
        }

        // ================================================================
        //  삭제
        // ================================================================

        public static void Clear(string sessionId)
        {
            PlayerPrefs.DeleteKey(KeyPrefix + sessionId);
            PlayerPrefs.Save();
        }

        // ================================================================
        //  내부
        // ================================================================

        private static void Save(string sessionId, List<ChatMessage> messages)
        {
            var wrapper = new MessageListWrapper { Messages = messages };
            var json = JsonUtility.ToJson(wrapper);
            PlayerPrefs.SetString(KeyPrefix + sessionId, json);
            PlayerPrefs.Save();
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

                    // 첫 메시지면 세션 제목도 업데이트
                    if (s.Title == "새 대화" && sender == ChatSender.User)
                    {
                        var title = text.Length > 25 ? text[..22] + "..." : text;
                        AgentSessionStore.UpdateTitle(i, title);
                    }
                    break;
                }
            }
        }

        // ── JSON 래퍼 (JsonUtility는 리스트 직접 직렬화 불가) ──

        [Serializable]
        private class MessageListWrapper
        {
            public List<ChatMessage> Messages = new();
        }
    }
}
