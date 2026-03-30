using OpenDesk.Presentation.Character.Context;
using UnityEngine;
using UnityEngine.AI;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 대기 상태 — Idle 모션 + NavMesh 배회.
    /// 일정 시간 대기 → 랜덤 위치로 걸어감 → 도착 후 Idle.
    /// </summary>
    public class AgentIdleState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private float _wanderTimer;
        private bool _isWalking;

        // 배회 설정
        private const float WanderInterval = 6f;     // 대기 후 배회까지 시간
        private const float WanderRadius = 3f;        // 배회 반경
        private const int MaxSampleAttempts = 10;     // NavMesh 샘플링 시도 횟수

        public string Name => "Idle";

        public AgentIdleState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Idle", loop: true);
            _wanderTimer = WanderInterval + Random.Range(-2f, 2f);
            _isWalking = false;
        }

        public void Update(float deltaTime)
        {
            if (_isWalking)
            {
                // 도착 체크
                if (_ctx.HasReachedDestination)
                {
                    _isWalking = false;
                    _ctx.StopMoving();
                    _ctx.Animation.PlayAnimation("Idle", loop: true);
                    _wanderTimer = WanderInterval + Random.Range(-2f, 3f);
                }
                return;
            }

            _wanderTimer -= deltaTime;
            if (_wanderTimer <= 0f)
            {
                TryWander();
            }
        }

        public void Exit()
        {
            _ctx.StopMoving();
            _isWalking = false;
        }

        private void TryWander()
        {
            if (_ctx.NavAgent == null || !_ctx.NavAgent.isOnNavMesh)
            {
                _wanderTimer = WanderInterval;
                return;
            }

            // NavMesh 위 랜덤 포인트 탐색
            var origin = _ctx.Transform.position;
            for (int i = 0; i < MaxSampleAttempts; i++)
            {
                var randomDir = origin + Random.insideUnitSphere * WanderRadius;
                randomDir.y = origin.y;

                if (NavMesh.SamplePosition(randomDir, out var hit, WanderRadius, NavMesh.AllAreas))
                {
                    _ctx.MoveTo(hit.position);
                    _ctx.Animation.PlayAnimation("Walk", loop: true);
                    _isWalking = true;
                    return;
                }
            }

            // 실패 시 다시 대기
            _wanderTimer = WanderInterval;
        }
    }
}
