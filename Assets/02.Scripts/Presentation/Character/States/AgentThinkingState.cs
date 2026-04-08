using OpenDesk.Presentation.Character.Context;
using UnityEngine;
using UnityEngine.AI;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 사고 중 상태 — WorkPlaceChair로 이동 → 앉기 → WorkPlaceTable 방향으로 회전.
    /// 이미 앉아있으면 (Completed→Thinking 재진입) 이동 없이 바로 Thinking 모션.
    /// </summary>
    public class AgentThinkingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private enum Phase { MovingToChair, SittingDown, Thinking }
        private Phase _phase;
        private Vector3 _lookDirection;
        private float _transitionTimer;
        private Vector3 _seatPosition;

        private const float ChairSearchRadius = 20f;
        private const float SeatYOffset = 0.3f;
        private const float StandToSitDuration = 1.5f;

        /// <summary>현재 앉아있는지 (외부에서 참조)</summary>
        public bool IsSeated { get; private set; }

        /// <summary>앉은 위치 저장 (CompletedState에서 재사용)</summary>
        public Vector3 LastSeatPosition => _seatPosition;
        public Vector3 LastLookDirection => _lookDirection;

        public string Name => "Thinking";

        public AgentThinkingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.Expression?.SetExpression("Puzzled");

            // 이미 앉아있으면 (Completed에서 재진입) 이동 안 함
            if (IsSeated)
            {
                _ctx.Animation.PlayAnimation("Thinking", loop: true);
                _phase = Phase.Thinking;
                Debug.Log($"[{_ctx.AgentName}] Thinking 재진입 -- 이미 앉아있음");
                return;
            }

            Debug.Log($"[{_ctx.AgentName}] Thinking 진입 -- WorkPlaceChair로 이동");

            if (TryFindChair(out var chairPos) && TryFindTable(chairPos, out var tablePos))
            {
                var dir = (tablePos - chairPos);
                dir.y = 0;
                _lookDirection = dir.normalized;
                _seatPosition = chairPos + Vector3.up * SeatYOffset;

                var navTarget = chairPos;
                if (NavMesh.SamplePosition(navTarget, out var hit, 2f, NavMesh.AllAreas))
                    navTarget = hit.position;

                if (_ctx.MoveTo(navTarget))
                {
                    _ctx.Animation.PlayAnimation("Walk", loop: true);
                    _phase = Phase.MovingToChair;
                    return;
                }
            }

            StartThinkingInPlace();
        }

        public void Update(float deltaTime)
        {
            switch (_phase)
            {
                case Phase.MovingToChair:
                    if (_ctx.HasReachedDestination)
                    {
                        _ctx.StopMoving();

                        if (_ctx.NavAgent != null)
                            _ctx.NavAgent.enabled = false;

                        if (_ctx.Transform != null)
                        {
                            _ctx.Transform.position = _seatPosition;
                            if (_lookDirection != Vector3.zero)
                                _ctx.Transform.forward = _lookDirection;
                        }

                        IsSeated = true;
                        _ctx.Animation.PlayAnimation("StandToSit", loop: false);
                        _transitionTimer = StandToSitDuration;
                        _phase = Phase.SittingDown;
                        Debug.Log($"[{_ctx.AgentName}] 의자 도착 -- 앉는 중");
                    }
                    break;

                case Phase.SittingDown:
                    _transitionTimer -= deltaTime;
                    if (_transitionTimer <= 0f)
                    {
                        _ctx.Animation.PlayAnimation("Thinking", loop: true);
                        _phase = Phase.Thinking;
                    }
                    break;

                case Phase.Thinking:
                    break;
            }
        }

        public void Exit()
        {
            _ctx.StopMoving();
            _ctx.Expression?.SetExpression("Neutral");
            // IsSeated는 리셋하지 않음 — CompletedState/ChattingState에서 참조
        }

        /// <summary>완전히 일어남 처리 (DismissAgent에서 호출)</summary>
        public void ResetSeated()
        {
            IsSeated = false;

            // NavMesh 높이로 복원
            if (_ctx.Transform != null)
            {
                var pos = _ctx.Transform.position;
                if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas))
                    _ctx.Transform.position = hit.position;
            }

            if (_ctx.NavAgent != null && !_ctx.NavAgent.enabled)
                _ctx.NavAgent.enabled = true;
        }

        private void StartThinkingInPlace()
        {
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Thinking", loop: true);
            _phase = Phase.Thinking;
        }

        private bool TryFindChair(out Vector3 position)
        {
            return TryFindByLayer("WorkPlaceChair", out position);
        }

        private bool TryFindTable(Vector3 fromChair, out Vector3 position)
        {
            position = Vector3.zero;
            int layer = LayerMask.NameToLayer("WorkPlaceTable");
            if (layer < 0) return false;

            float closestDist = float.MaxValue;
            bool found = false;
            var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allObjects)
            {
                if (t.gameObject.layer != layer) continue;
                var dist = Vector3.Distance(fromChair, t.position);
                if (dist < closestDist && dist < 5f)
                {
                    closestDist = dist;
                    position = t.position;
                    found = true;
                }
            }
            return found;
        }

        private bool TryFindByLayer(string layerName, out Vector3 position)
        {
            position = Vector3.zero;
            if (_ctx.Transform == null) return false;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) return false;

            var agentPos = _ctx.Transform.position;
            GameObject closest = null;
            float closestDist = float.MaxValue;
            var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allObjects)
            {
                if (t.gameObject.layer != layer) continue;
                if (t.IsChildOf(_ctx.Transform)) continue;
                var dist = Vector3.Distance(agentPos, t.position);
                if (dist < closestDist && dist < ChairSearchRadius)
                {
                    closestDist = dist;
                    closest = t.gameObject;
                }
            }
            if (closest == null) return false;
            position = closest.transform.position;
            return true;
        }
    }
}
