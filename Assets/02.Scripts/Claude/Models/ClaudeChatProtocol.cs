using System;

namespace OpenDesk.Claude.Models
{
    // ── Unity → 서버 요청 ─────────────────────────────────────

    [Serializable]
    public class ChatRequest
    {
        public string type = "chat";
        public string message;
    }

    [Serializable]
    public class ClearRequest
    {
        public string type = "clear";
    }

    [Serializable]
    public class ConfigRequest
    {
        public string type = "config";
        public string systemPrompt;
    }

    [Serializable]
    public class PingRequest
    {
        public string type = "ping";
    }

    [Serializable]
    public class ResumeRequest
    {
        public string type = "resume";
        public string conversation; // ConversationFile JSON
    }

    // ── 서버 → Unity 응답 (수신 파싱용) ────────────────────────

    [Serializable]
    public class ServerMessage
    {
        public string type;
    }

    [Serializable]
    public class ConnectedMessage
    {
        public string type;
        public string model;
    }

    [Serializable]
    public class DeltaMessage
    {
        public string type;
        public string text;
    }

    [Serializable]
    public class FinalMessage
    {
        public string type;
        public string text;
        public float cost;
    }

    [Serializable]
    public class ErrorMessage
    {
        public string type;
        public string message;
        public string code;
    }

    [Serializable]
    public class StatusMessage
    {
        public string type;
        public string text;
    }
}
