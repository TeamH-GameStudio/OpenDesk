using System;

namespace OpenDesk.Claude.Models
{
    // ── Unity → 미들웨어 요청 (7종) ──────────────────────────────

    [Serializable]
    public class ChatMessageRequest
    {
        public string type = "chat_message";
        public string agent_id;
        public string message;
    }

    [Serializable]
    public class ChatClearRequest
    {
        public string type = "chat_clear";
        public string agent_id;
    }

    [Serializable]
    public class SessionListRequest
    {
        public string type = "session_list";
        public string agent_id;
    }

    [Serializable]
    public class SessionSwitchRequest
    {
        public string type = "session_switch";
        public string agent_id;
        public string session_id;
    }

    [Serializable]
    public class SessionNewRequest
    {
        public string type = "session_new";
        public string agent_id;
    }

    [Serializable]
    public class SessionDeleteRequest
    {
        public string type = "session_delete";
        public string agent_id;
        public string session_id;
    }

    [Serializable]
    public class StatusRequest
    {
        public string type = "status_request";
    }

    // ── 미들웨어 → Unity 응답 (6종) ──────────────────────────────

    [Serializable]
    public class ServerMessage
    {
        public string type;
    }

    [Serializable]
    public class AgentStateMessage
    {
        public string type;       // "agent_state"
        public string agent_id;
        public string role;
        public string session_id;
        public string state;      // "idle", "thinking", "working", "complete", "error"
        public string tool;       // working 시 도구 이름
        public string tool_input; // working 시 도구 입력 미리보기
        public string error;      // error 시 에러 코드
        public string message;    // error 시 에러 메시지
        public double timestamp;
    }

    [Serializable]
    public class AgentDeltaMessage
    {
        public string type;       // "agent_delta"
        public string agent_id;
        public string role;
        public string session_id;
        public string text;       // 실시간 텍스트 청크 (raw 마크다운, TMP 미적용)
        public double timestamp;
    }

    [Serializable]
    public class AgentMessageMessage
    {
        public string type;       // "agent_message"
        public string agent_id;
        public string role;
        public string session_id;
        public string message;    // 최종 응답 (TMP 포매팅 적용)
        public double timestamp;
    }

    [Serializable]
    public class AgentThinkingMessage
    {
        public string type;       // "agent_thinking"
        public string agent_id;
        public string role;
        public string session_id;
        public string thinking;   // 추론 과정 텍스트
        public double timestamp;
    }

    [Serializable]
    public class SessionListResponse
    {
        public string type;       // "session_list_response"
        public string agent_id;
        public string current_session_id;
        public SessionInfo[] sessions;
    }

    [Serializable]
    public class SessionInfo
    {
        public string session_id;
        public string title;
        public double updated_at;
        public int message_count;
    }

    [Serializable]
    public class SessionSwitchedMessage
    {
        public string type;       // "session_switched"
        public string agent_id;
        public string session_id;
        public ChatHistoryEntry[] chat_history;
    }

    [Serializable]
    public class ChatHistoryEntry
    {
        public string role;       // "user" 또는 "assistant"
        public string text;
    }
}
