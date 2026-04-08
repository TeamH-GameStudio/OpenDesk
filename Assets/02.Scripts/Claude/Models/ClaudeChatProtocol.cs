using System;

namespace OpenDesk.Claude.Models
{
    // ══════════════════════════════════════════════════════════
    //  Unity -> Middleware (요청 7종)
    // ══════════════════════════════════════════════════════════

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
    public class StatusRequest
    {
        public string type = "status_request";
    }

    [Serializable]
    public class SessionListRequest
    {
        public string type = "session_list";
        public string agent_id;
    }

    [Serializable]
    public class SessionNewRequest
    {
        public string type = "session_new";
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
    public class SessionDeleteRequest
    {
        public string type = "session_delete";
        public string agent_id;
        public string session_id;
    }

    // ══════════════════════════════════════════════════════════
    //  Middleware -> Unity (응답/이벤트 7종)
    // ══════════════════════════════════════════════════════════

    /// <summary>type 필드만 먼저 파싱하는 베이스 메시지</summary>
    [Serializable]
    public class ServerMessage
    {
        public string type;
    }

    /// <summary>
    /// agent_state: 에이전트 상태 변화
    /// state = idle | thinking | working | complete | error
    /// </summary>
    [Serializable]
    public class AgentStateMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public double timestamp;
        public string state;
        // working 상태 시 추가 필드
        public string tool;
        public string tool_input;
        // error 상태 시 추가 필드
        public string error;
        public string message;
    }

    /// <summary>
    /// agent_thinking: AI 추론 과정 (스냅샷 — 매번 전체 내용)
    /// </summary>
    [Serializable]
    public class AgentThinkingMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public double timestamp;
        public string thinking;
    }

    /// <summary>
    /// agent_delta: 응답 텍스트 스트리밍 (delta — 이어붙이기)
    /// </summary>
    [Serializable]
    public class AgentDeltaMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public double timestamp;
        public string text;
    }

    /// <summary>
    /// agent_message: 최종 응답 (TMP 리치텍스트 변환 완료)
    /// </summary>
    [Serializable]
    public class AgentMessageResponse
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public double timestamp;
        public string message;
    }

    /// <summary>
    /// agent_action: 캐릭터 액션 (idle/typing/walk/cheering/sitting/drinking/dancing)
    /// </summary>
    [Serializable]
    public class AgentActionMessage
    {
        public string type;
        public string agent_id;
        public string action;
        public double timestamp;
    }

    /// <summary>session_list_response: 세션 목록</summary>
    [Serializable]
    public class SessionListResponse
    {
        public string type;
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

    /// <summary>session_switched: 세션 전환 완료 + 대화 이력</summary>
    [Serializable]
    public class SessionSwitchedMessage
    {
        public string type;
        public string agent_id;
        public string session_id;
        public ChatHistoryEntry[] chat_history;
    }

    [Serializable]
    public class ChatHistoryEntry
    {
        public string role;
        public string text;
    }
}
