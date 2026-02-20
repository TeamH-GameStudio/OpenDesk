using System;

namespace OpenDesk.Core.Models
{
    public readonly struct AgentEvent
    {
        public AgentActionType ActionType  { get; }
        public string          SessionId   { get; }   // 어느 에이전트인지
        public string          TaskName    { get; }
        public string          SubAgentId  { get; }
        public string          RawPayload  { get; }
        public DateTime        Timestamp   { get; }

        public AgentEvent(
            AgentActionType actionType,
            string sessionId   = "",
            string taskName    = "",
            string subAgentId  = "",
            string rawPayload  = "",
            DateTime? timestamp = null)
        {
            ActionType = actionType;
            SessionId  = sessionId  ?? "";
            TaskName   = taskName   ?? "";
            SubAgentId = subAgentId ?? "";
            RawPayload = rawPayload ?? "";
            Timestamp  = timestamp  ?? DateTime.UtcNow;
        }

        public override string ToString() =>
            $"[{Timestamp:HH:mm:ss}] {SessionId} → {ActionType} | {TaskName}";
    }
}
