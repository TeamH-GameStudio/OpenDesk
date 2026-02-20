using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    public interface ISubAgentService
    {
        // 현재 실행 중인 서브에이전트 목록 (위젯 표시용)
        ReadOnlyReactiveProperty<IReadOnlyList<SubAgentStatus>> ActiveAgents { get; }

        // 이벤트 처리
        void OnSubAgentSpawned(AgentEvent e);
        void OnSubAgentCompleted(AgentEvent e);
        void OnSubAgentFailed(AgentEvent e);

        // 스냅샷 조회
        UniTask<IReadOnlyList<SubAgentStatus>> GetSnapshotAsync(CancellationToken ct = default);
    }
}
