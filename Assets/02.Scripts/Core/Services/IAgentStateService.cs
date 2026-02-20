using System;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    public interface IAgentStateService
    {
        // 현재 상태 조회
        AgentActionType GetState(string sessionId);

        // 상태 변경 스트림 — (sessionId, newState)
        Observable<(string SessionId, AgentActionType State)> OnStateChanged { get; }

        // 이벤트 수신 → 상태 갱신
        void ApplyEvent(AgentEvent agentEvent);

        // 테스트/디버그용 강제 설정
        void ForceState(string sessionId, AgentActionType state);
    }
}
