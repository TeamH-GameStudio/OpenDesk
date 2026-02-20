using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 서브에이전트 목록 추적 — 위젯 패널 표시용
    /// </summary>
    public class SubAgentService : ISubAgentService, IDisposable
    {
        private readonly List<SubAgentStatus> _activeAgents = new();
        private readonly ReactiveProperty<IReadOnlyList<SubAgentStatus>> _activeAgentsProp;

        public ReadOnlyReactiveProperty<IReadOnlyList<SubAgentStatus>> ActiveAgents
            => _activeAgentsProp;

        public SubAgentService()
        {
            _activeAgentsProp = new ReactiveProperty<IReadOnlyList<SubAgentStatus>>(
                new List<SubAgentStatus>()
            );
        }

        public void OnSubAgentSpawned(AgentEvent e)
        {
            if (string.IsNullOrEmpty(e.SubAgentId)) return;

            // 중복 방지
            if (_activeAgents.Any(a => a.Id == e.SubAgentId)) return;

            _activeAgents.Add(new SubAgentStatus
            {
                Id        = e.SubAgentId,
                Label     = e.TaskName,
                ParentId  = e.SessionId,
                IsRunning = true,
                StartedAt = DateTime.UtcNow,
            });

            Publish();
        }

        public void OnSubAgentCompleted(AgentEvent e) => RemoveAgent(e.SubAgentId);
        public void OnSubAgentFailed(AgentEvent e)    => RemoveAgent(e.SubAgentId);

        public UniTask<IReadOnlyList<SubAgentStatus>> GetSnapshotAsync(CancellationToken ct = default)
        {
            IReadOnlyList<SubAgentStatus> snapshot = _activeAgents.ToList();
            return UniTask.FromResult(snapshot);
        }

        private void RemoveAgent(string subAgentId)
        {
            if (string.IsNullOrEmpty(subAgentId)) return;
            _activeAgents.RemoveAll(a => a.Id == subAgentId);
            Publish();
        }

        private void Publish()
        {
            _activeAgentsProp.Value = _activeAgents.ToList();
        }

        public void Dispose()
        {
            _activeAgentsProp.Dispose();
        }
    }
}
