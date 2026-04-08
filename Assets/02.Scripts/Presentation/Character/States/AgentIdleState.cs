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
        private float _idleElapsed;       // IDLE 상태 누적 시간
        private bool _isSleeping;         // sleeping 서브상태 여부

        // 배회 설정
        private const float WanderInterval = 6f;     // 대기 후 배회까지 시간
        private const float WanderRadius = 3f;        // 배회 반경
        private const int MaxSampleAttempts = 10;     // NavMesh 샘플링 시도 횟수
        private const float SleepThreshold = 30f;     // 30초 후 sleeping 전환

        public string Name => "Idle";

        public AgentIdleState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Idle", loop: true);
            _ctx.Expression?.SetExpression("Neutral");
            _ctx.OnHUDStatusChanged?.Invoke("대기 중");
            _wanderTimer = WanderInterval + Random.Range(-2f, 2f);
            _isWalking = false;
            _idleElapsed = 0f;
            _isSleeping = false;
        }

        public void Update(float deltaTime)
        {
            // sleeping 상태면 더 이상 배회하지 않음
            if (_isSleeping) return;

            // 카메라 포커스 중이면 가만히 Idle 유지
            if (_ctx.IsFocused)
            {
                // 걷고 있었으면 멈춤
                if (_isWalking)
                {
                    _isWalking = false;
                    _ctx.StopMoving();
                    _ctx.Animation.PlayAnimation("Idle", loop: true);
                }
                _idleElapsed = 0f; // sleeping 타이머 리셋
                return;
            }

            _idleElapsed += deltaTime;

            // 30초 경과 시 sleeping 전환
            if (!_isWalking && _idleElapsed >= SleepThreshold)
            {
                EnterSleeping();
                return;
            }

            if (_isWalking)
            {
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
            if (_isSleeping)
            {
                _ctx.Expression?.SetExpression("Neutral");
                _isSleeping = false;
            }
        }

        private void EnterSleeping()
        {
            _isSleeping = true;
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Sleeping", loop: true);
            _ctx.Expression?.SetExpression("Sleeping");
            _ctx.OnHUDStatusChanged?.Invoke("수면 중...");
            Debug.Log($"[{_ctx.AgentName}] Idle -> Sleeping (30초 경과)");
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
