using System;
using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 에러 상태 — 앉아있던 상태에서 일어나 에러 표정 표시.
    ///
    /// 시퀀스:
    ///   TypeToSit(원샷) → SitToStand(원샷, ApproachPoint로 슬라이드) → Error(3초) → 자동 복귀
    /// </summary>
    public class AgentErrorState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;
        public event Action OnErrorAnimDone;

        private enum Phase { TypeToSit, SitToStand, Error }
        private Phase _phase;
        private float _timer;

        private const float TypeToSitDuration = 1.0f;
        private const float SitToStandDuration = 1.5f;
        private const float ErrorDuration = 3f;
        private const float SlideDistance = 1.0f;

        // 슬라이드용
        private Vector3 _slideStart;
        private Vector3 _slideTarget;

        public string Name => "Error";

        public AgentErrorState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Expression?.SetExpression("Error");

            _ctx.Animation.PlayAnimation("TypeToSit", loop: false);
            _timer = TypeToSitDuration;
            _phase = Phase.TypeToSit;
            Debug.Log($"[{_ctx.AgentName}] Error 진입 -- 일어나는 시퀀스");
        }

        public void Update(float deltaTime)
        {
            _timer -= deltaTime;

            // SitToStand 중 슬라이드 보간
            if (_phase == Phase.SitToStand && _timer > 0f && _ctx.Transform != null)
            {
                float t = Mathf.Clamp01(1f - (_timer / SitToStandDuration));
                _ctx.Transform.position = Vector3.Lerp(_slideStart, _slideTarget, t);
                return;
            }

            if (_timer > 0f) return;

            switch (_phase)
            {
                case Phase.TypeToSit:
                    // 슬라이드 목표 계산
                    if (_ctx.Transform != null)
                    {
                        _slideStart = _ctx.Transform.position;

                        Vector3 groundTarget;
                        if (_ctx.CurrentWorkStation != null)
                            groundTarget = _ctx.CurrentWorkStation.ApproachPosition;
                        else
                            groundTarget = _slideStart + _ctx.Transform.forward * SlideDistance;

                        if (UnityEngine.AI.NavMesh.SamplePosition(groundTarget, out var navHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                            groundTarget = navHit.position;
                        _slideTarget = groundTarget;
                    }

                    _ctx.Animation.PlayAnimation("SitToStand", loop: false);
                    _timer = SitToStandDuration;
                    _phase = Phase.SitToStand;
                    break;

                case Phase.SitToStand:
                    // 최종 위치 확정
                    if (_ctx.Transform != null)
                        _ctx.Transform.position = _slideTarget;

                    _ctx.Animation.PlayAnimation("Error", loop: true);
                    _timer = ErrorDuration;
                    _phase = Phase.Error;
                    break;

                case Phase.Error:
                    OnErrorAnimDone?.Invoke();
                    _timer = float.MaxValue;
                    break;
            }
        }

        public void Exit()
        {
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
