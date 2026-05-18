using System.Collections.Generic;
using R3;

namespace OpenDesk.AgentCreation.Persistence
{
    // Single source of truth for agent draft records.
    //
    // OnChanged emits after Save/Delete commits to disk, so subscribers can
    // recompose anything derived from a record (system prompt, HUD label, etc.)
    // without each having to know how persistence works.
    public interface IAgentRepository
    {
        AgentDraftRecord Get(string agentId);
        IReadOnlyList<AgentDraftRecord> GetAll();
        string Save(AgentDraftRecord record);
        bool Delete(string agentId);
        Observable<AgentRepositoryChange> OnChanged { get; }
    }
}
