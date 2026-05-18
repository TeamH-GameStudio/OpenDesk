using System;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using VContainer.Unity;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 미들웨어 sub_agent_* WS 이벤트 → ISubAgentService 어댑터.
    /// Office scope 에 등록 — IAiChatService (Office) 와 ISubAgentService (Core, parent) 양쪽을 본다.
    /// </summary>
    public sealed class SubAgentEventBridge : IStartable, IDisposable
    {
        private readonly IAiChatService _chat;
        private readonly ISubAgentService _subAgent;
        private bool _bound;

        public SubAgentEventBridge(IAiChatService chat, ISubAgentService subAgent)
        {
            _chat = chat;
            _subAgent = subAgent;
        }

        public void Start()
        {
            if (_chat == null || _subAgent == null) return;
            _chat.OnSubAgentSpawned   += HandleSpawned;
            _chat.OnSubAgentCompleted += HandleCompleted;
            _chat.OnSubAgentFailed    += HandleFailed;
            _bound = true;
        }

        public void Dispose()
        {
            if (!_bound || _chat == null) return;
            _chat.OnSubAgentSpawned   -= HandleSpawned;
            _chat.OnSubAgentCompleted -= HandleCompleted;
            _chat.OnSubAgentFailed    -= HandleFailed;
            _bound = false;
        }

        private void HandleSpawned(SubAgentSpawnedMessage m)
        {
            if (m == null) return;
            _subAgent.OnSubAgentSpawned(new AgentEvent(
                actionType: AgentActionType.SubAgentSpawned,
                sessionId:  m.agent_id ?? string.Empty,
                taskName:   m.task_name ?? string.Empty,
                subAgentId: m.sub_agent_id ?? string.Empty));
        }

        private void HandleCompleted(SubAgentCompletedMessage m)
        {
            if (m == null) return;
            _subAgent.OnSubAgentCompleted(new AgentEvent(
                actionType: AgentActionType.SubAgentCompleted,
                sessionId:  m.agent_id ?? string.Empty,
                taskName:   m.task_name ?? string.Empty,
                subAgentId: m.sub_agent_id ?? string.Empty));
        }

        private void HandleFailed(SubAgentFailedMessage m)
        {
            if (m == null) return;
            _subAgent.OnSubAgentFailed(new AgentEvent(
                actionType: AgentActionType.SubAgentFailed,
                sessionId:  m.agent_id ?? string.Empty,
                subAgentId: m.sub_agent_id ?? string.Empty,
                message:    m.error ?? string.Empty));
        }
    }
}
