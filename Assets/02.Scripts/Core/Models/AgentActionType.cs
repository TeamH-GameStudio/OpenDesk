namespace OpenDesk.Core.Models
{
    public enum AgentActionType
    {
        Idle,
        TaskStarted,
        TaskCompleted,
        TaskFailed,
        Thinking,
        SubAgentSpawned,
        SubAgentCompleted,
        SubAgentFailed,
        Connected,
        Disconnected
    }
}
