using System;
using System.Collections.Generic;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 에이전트별 상태(Idle/Typing/Done)를 관리
    /// AgentEvent를 받아 상태 전환 규칙을 적용
    /// </summary>
    public class AgentStateService : IAgentStateService, IDisposable
    {
        private readonly Dictionary<string, AgentActionType> _states = new();
        private readonly Subject<(string SessionId, AgentActionType State)> _stateChanged = new();

        public Observable<(string SessionId, AgentActionType State)> OnStateChanged
            => _stateChanged;

        public AgentActionType GetState(string sessionId)
        {
            return _states.TryGetValue(sessionId, out var state)
                ? state
                : AgentActionType.Idle;
        }

        public void ApplyEvent(AgentEvent e)
        {
            var newState = ResolveState(e.ActionType);
            var sessionId = string.IsNullOrEmpty(e.SessionId) ? "main" : e.SessionId;

            // 상태가 실제로 바뀔 때만 발행
            if (_states.TryGetValue(sessionId, out var current) && current == newState)
                return;

            _states[sessionId] = newState;
            _stateChanged.OnNext((sessionId, newState));
        }

        public void ForceState(string sessionId, AgentActionType state)
        {
            _states[sessionId] = state;
            _stateChanged.OnNext((sessionId, state));
        }

        private static AgentActionType ResolveState(AgentActionType actionType)
        {
            return actionType switch
            {
                AgentActionType.TaskStarted      => AgentActionType.TaskStarted,
                AgentActionType.Thinking         => AgentActionType.Thinking,
                AgentActionType.TaskCompleted    => AgentActionType.TaskCompleted,
                AgentActionType.TaskFailed       => AgentActionType.TaskFailed,
                AgentActionType.Disconnected     => AgentActionType.Disconnected,
                AgentActionType.Connected        => AgentActionType.Idle,
                _                                => AgentActionType.Idle,
            };
        }

        public void Dispose()
        {
            _stateChanged.Dispose();
        }
    }
}
