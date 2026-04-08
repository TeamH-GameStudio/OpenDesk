using System;
using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 에러 상태 — 앉아있던 상태에서 일어나 에러 표정 표시.
    ///
    /// 시퀀스:
    ///   TypeToSit(원샷) → SitToStand(원샷) → Idle(face_error) → 3초 후 자동 복귀
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
            if (_timer > 0f) return;

            switch (_phase)
            {
                case Phase.TypeToSit:
                    _ctx.Animation.PlayAnimation("SitToStand", loop: false);
                    _timer = SitToStandDuration;
                    _phase = Phase.SitToStand;
                    break;

                case Phase.SitToStand:
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
