using System;
using System.Collections.Generic;
using System.Linq;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 에이전트별 상태를 관리
    /// - AgentEvent를 받아 상태 전환 규칙 적용
    /// - 에이전틱 루프 세부 단계(Planning/Executing/Reviewing) 지원
    /// - 글로벌 상태 조회 (IsAnyAgentBusy, BusyAgentCount)
    /// </summary>
    public class AgentStateService : IAgentStateService, IDisposable
    {
        private readonly Dictionary<string, AgentActionType> _states = new();
        private readonly Subject<(string SessionId, AgentActionType State)> _stateChanged = new();

        public Observable<(string SessionId, AgentActionType State)> OnStateChanged
            => _stateChanged;

        /// <summary>어느 에이전트라도 작업 중인지 조회</summary>
        public bool IsAnyAgentBusy => _states.Values.Any(IsWorkingState);

        /// <summary>현재 작업 중인 에이전트 수</summary>
        public int BusyAgentCount => _states.Values.Count(IsWorkingState);

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
                AgentActionType.Planning         => AgentActionType.Planning,
                AgentActionType.Executing        => AgentActionType.Executing,
                AgentActionType.Reviewing        => AgentActionType.Reviewing,
                AgentActionType.ToolUsing        => AgentActionType.Executing,   // 도구 사용 = 실행 중
                AgentActionType.ToolResult       => AgentActionType.Reviewing,   // 도구 결과 = 검토 중
                AgentActionType.TaskCompleted    => AgentActionType.TaskCompleted,
                AgentActionType.TaskFailed       => AgentActionType.TaskFailed,
                AgentActionType.Disconnected     => AgentActionType.Disconnected,
                AgentActionType.Connected        => AgentActionType.Idle,
                _                                => AgentActionType.Idle,
            };
        }

        private static bool IsWorkingState(AgentActionType state)
        {
            return state switch
            {
                AgentActionType.TaskStarted => true,
                AgentActionType.Thinking    => true,
                AgentActionType.Planning    => true,
                AgentActionType.Executing   => true,
                AgentActionType.Reviewing   => true,
                _                           => false,
            };
        }

        public void Dispose()
        {
            _stateChanged.Dispose();
        }
    }
}
