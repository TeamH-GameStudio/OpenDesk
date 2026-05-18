namespace OpenDesk.AgentCreation.Persistence
{
    public enum AgentRepositoryChangeKind
    {
        Saved,
        Deleted,
    }

    // Carries the agent id always; Record is null for Deleted events so subscribers
    // do not need to refetch just to know which agent disappeared.
    public readonly struct AgentRepositoryChange
    {
        public readonly AgentRepositoryChangeKind Kind;
        public readonly string AgentId;
        public readonly AgentDraftRecord Record;

        public AgentRepositoryChange(AgentRepositoryChangeKind kind, string agentId, AgentDraftRecord record)
        {
            Kind = kind;
            AgentId = agentId;
            Record = record;
        }

        public static AgentRepositoryChange Saved(AgentDraftRecord record) =>
            new(AgentRepositoryChangeKind.Saved, record?.id, record);

        public static AgentRepositoryChange Deleted(string agentId) =>
            new(AgentRepositoryChangeKind.Deleted, agentId, null);
    }
}
