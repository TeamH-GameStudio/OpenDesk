using System;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Services;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    public sealed class AgentCreationBridgeService : IAgentCreationBridge
    {
        public event Action<AgentDraftRecord, string> AgentSaved;
        public event Action OfficeSetupCompleted;

        public void RaiseAgentSaved(AgentDraftRecord record, string savedPath)
        {
            if (record == null)
            {
                Debug.LogWarning("[AgentCreationBridge] RaiseAgentSaved: record is null");
                return;
            }
            AgentSaved?.Invoke(record, savedPath);
        }

        public void RaiseOfficeSetupCompleted()
        {
            OfficeSetupCompleted?.Invoke();
        }
    }
}
