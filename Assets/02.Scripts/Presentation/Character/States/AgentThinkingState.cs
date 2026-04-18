using OpenDesk.Presentation.Character.Context;
using UnityEngine;
using UnityEngine.AI;

// WorkStation, WorkStationType 참조
using OpenDesk.Presentation.Character;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 사고 중 상태 — WorkStation으로 이동 → 자연스럽게 앉기 → 작업 방향 응시.
    ///
    /// 흐름:
    ///   1. WorkStation 탐색 (에이전트 역할 기반)
    ///   2. NavMesh로 ApproachPoint까지 이동 (Walk)
    ///   3. ApproachPoint 도착 → SitPoint 방향으로 회전 (TurningToSit)
    ///   4. StandToSit 재생 + ApproachPoint→SitPoint Lerp 이동 (SittingDown)
    ///   5. Thinking 루프 (Thinking)
    ///
    /// 이미 앉아있으면 (Completed→Thinking 재진입) 이동 없이 바로 Thinking.
    /// </summary>
    public class AgentThinkingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private enum Phase { MovingToApproach, Thinking }
        private Phase _phase;

        private const float SearchRadius = 30f;

        /// <summary>현재 앉아있는지 (외부에서 참조)</summary>
        public bool IsSeated { get; private set; }

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

            Debug.Log($"[{_ctx.AgentName}] Thinking 진입 -- WorkStation 탐색");

            var workStation = FindWorkStation();
            if (workStation != null)
            {
                workStation.IsOccupied = true;
                _ctx.CurrentWorkStation = workStation;

                // ApproachPoint로 NavMesh 이동
                var approachPos = workStation.ApproachPosition;
                if (NavMesh.SamplePosition(approachPos, out var hit, 2f, NavMesh.AllAreas))
                    approachPos = hit.position;

                if (_ctx.MoveTo(approachPos))
                {
                    _ctx.Animation.PlayAnimation("Walk", loop: true);
                    _phase = Phase.MovingToApproach;
                    Debug.Log($"[{_ctx.AgentName}] WorkStation '{workStation.name}' ApproachPoint로 이동 시작");
                    return;
                }
                else
                {
                    Debug.LogWarning($"[{_ctx.AgentName}] MoveTo 실패 -- NavAgent 상태: enabled={_ctx.NavAgent?.enabled}, onNavMesh={_ctx.NavAgent?.isOnNavMesh}");
                }
            }

            // WorkStation 없거나 이동 불가 → 서있는 상태로 Idle 유지
            Debug.LogWarning($"[{_ctx.AgentName}] WorkStation 이동 불가 -- 서있는 채로 대기");
            StartThinkingInPlace();
        }

        public void Update(float deltaTime)
        {
            switch (_phase)
            {
                case Phase.MovingToApproach:
                    if (_ctx.HasReachedDestination)
                    {
                        _ctx.StopMoving();
                        SitDown();
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
            // IsSeated, CurrentWorkStation은 리셋하지 않음 — CompletedState/ChattingState에서 참조
        }

        /// <summary>완전히 일어남 처리 (DismissAgent에서 호출)</summary>
        public void ResetSeated()
        {
            IsSeated = false;

            // WorkStation 점유 해제
            if (_ctx.CurrentWorkStation != null)
            {
                _ctx.CurrentWorkStation.IsOccupied = false;
                _ctx.CurrentWorkStation = null;
            }

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

        // ── 내부 메서드 ──────────────────────────────────────────

        /// <summary>ApproachPoint 도착 → SitPoint에 즉시 앉기</summary>
        private void SitDown()
        {
            if (_ctx.NavAgent != null)
                _ctx.NavAgent.enabled = false;

            var ws = _ctx.CurrentWorkStation;
            if (ws != null && _ctx.Transform != null)
            {
                _ctx.Transform.position = ws.SitPosition;
                _ctx.Transform.rotation = Quaternion.LookRotation(ws.SitForward, Vector3.up);
            }

            IsSeated = true;
            _ctx.Animation.PlayAnimation("Thinking", loop: true);
            _phase = Phase.Thinking;
            Debug.Log($"[{_ctx.AgentName}] 앉기 완료 -- Thinking 루프");
        }

        private void StartThinkingInPlace()
        {
            _ctx.StopMoving();
            // 서있는 상태이므로 Idle 유지 (Thinking은 앉은 포즈라 어색함)
            _ctx.Animation.PlayAnimation("Idle", loop: true);
            _phase = Phase.Thinking;
        }

        /// <summary>에이전트 역할에 맞는 WorkStation 탐색</summary>
        private WorkStation FindWorkStation()
        {
            var agentPos = _ctx.Transform != null ? _ctx.Transform.position : Vector3.zero;
            var preferredType = GetPreferredType();

            WorkStation bestMatch = null;
            WorkStation fallback = null;
            float bestDist = float.MaxValue;
            float fallbackDist = float.MaxValue;

            var stations = Object.FindObjectsByType<WorkStation>(FindObjectsSortMode.None);
            foreach (var ws in stations)
            {
                if (ws.IsOccupied) continue;

                float dist = Vector3.Distance(agentPos, ws.SitPosition);
                if (dist > SearchRadius) continue;

                if (ws.Type == preferredType && dist < bestDist)
                {
                    bestDist = dist;
                    bestMatch = ws;
                }
                else if (ws.Type != WorkStationType.Resting && dist < fallbackDist)
                {
                    fallbackDist = dist;
                    fallback = ws;
                }
            }

            var result = bestMatch ?? fallback;
            if (result != null)
                Debug.Log($"[{_ctx.AgentName}] WorkStation 발견: {result.name} (type={result.Type}, dist={Vector3.Distance(agentPos, result.SitPosition):F1}m)");
            else
                Debug.LogWarning($"[{_ctx.AgentName}] WorkStation 없음 -- 제자리 Thinking");

            return result;
        }

        private WorkStationType GetPreferredType()
        {
            return _ctx.AgentId switch
            {
                "researcher" => WorkStationType.Main,
                "writer" => WorkStationType.Sub,
                "analyst" => WorkStationType.Sub,
                _ => WorkStationType.Main
            };
        }
    }
}
