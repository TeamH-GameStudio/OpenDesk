using OpenDesk.Presentation.Character.Context;
using UnityEngine;
using UnityEngine.AI;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 사고 중 상태 — 의자(컴퓨터 앞)로 이동 → 앉아서 생각 모션.
    /// 채팅 흐름: 메시지 전송 → Thinking(여기) → delta 수신 → Chatting/Typing
    /// </summary>
    public class AgentThinkingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private enum Phase { MovingToDesk, Thinking }
        private Phase _phase;
        private Vector3 _deskForward;

        private const float ChairSearchRadius = 15f;
        private const float ChairOffset = 0.35f;

        public string Name => "Thinking";

        public AgentThinkingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            Debug.Log($"[{_ctx.AgentName}] Thinking 진입 -- 컴퓨터 앞으로 이동");
            _ctx.Expression?.SetExpression("Puzzled");

            if (TryFindNearestChair(out var chairPos, out var chairFwd))
            {
                _deskForward = chairFwd;
                var seatTarget = chairPos - chairFwd * ChairOffset;

                if (_ctx.MoveTo(seatTarget))
                {
                    _ctx.Animation.PlayAnimation("Walk", loop: true);
                    _phase = Phase.MovingToDesk;
                    return;
                }
            }

            // 의자 못 찾으면 제자리에서 생각
            StartThinkingInPlace();
        }

        public void Update(float deltaTime)
        {
            if (_phase == Phase.MovingToDesk && _ctx.HasReachedDestination)
            {
                _ctx.StopMoving();
                if (_ctx.Transform != null)
                    _ctx.Transform.forward = _deskForward;

                StartThinkingInPlace();
            }
        }

        public void Exit()
        {
            _ctx.StopMoving();
            _ctx.Expression?.SetExpression("Neutral");
        }

        private void StartThinkingInPlace()
        {
            _ctx.StopMoving();
            // Thinking 전용 클립이 없으면 Idle 사용 (추후 교체 가능)
            _ctx.Animation.PlayAnimation("Idle", loop: true);
            _phase = Phase.Thinking;
            Debug.Log($"[{_ctx.AgentName}] 컴퓨터 앞에서 생각 중...");
        }

        private bool TryFindNearestChair(out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.forward;

            if (_ctx.Transform == null) return false;

            var agentPos = _ctx.Transform.position;
            GameObject closest = null;
            float closestDist = float.MaxValue;

            var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allObjects)
            {
                if (!t.name.Contains("Chair")) continue;
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

            // 테이블 방향으로 forward 설정
            if (TryFindNearestTable(position, out var tablePos))
            {
                forward = (tablePos - position).normalized;
                forward.y = 0;
            }
            else
            {
                forward = closest.transform.forward;
            }

            if (NavMesh.SamplePosition(position, out var hit, 2f, NavMesh.AllAreas))
                position = hit.position;

            return true;
        }

        private static bool TryFindNearestTable(Vector3 from, out Vector3 tablePos)
        {
            tablePos = Vector3.zero;
            float closest = float.MaxValue;
            bool found = false;

            var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allObjects)
            {
                if (!t.name.Contains("Table")) continue;
                var dist = Vector3.Distance(from, t.position);
                if (dist < closest && dist < 5f)
                {
                    closest = dist;
                    tablePos = t.position;
                    found = true;
                }
            }
            return found;
        }
    }
}
