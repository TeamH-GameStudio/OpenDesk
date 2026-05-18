using OpenDesk.Core.Services;

namespace OpenDesk.Core.Implementations
{
    public sealed class AgentHandoffService : IAgentHandoffService
    {
        public string LastCreatedAgentName { get; set; }

        public string Consume()
        {
            var value = LastCreatedAgentName;
            LastCreatedAgentName = null;
            return value;
        }
    }
}
