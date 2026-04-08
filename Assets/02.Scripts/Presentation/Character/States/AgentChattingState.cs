using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 채팅 응답 중 상태 — Claude가 스트리밍 응답을 보내는 동안.
    /// Thinking에서 이미 앉아있으면 SitToType → Typing 시퀀스 재생.
    /// </summary>
    public class AgentChattingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private enum Phase { TransitionToType, Typing }
        private Phase _phase;
        private float _transitionTimer;

        private const float SitToTypeDuration = 1.0f;

        public string Name => "Chatting";

        public AgentChattingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Expression?.SetExpression("Focused");

            // SitToType 전환 애니메이션 재생
            _ctx.Animation.PlayAnimation("SitToType", loop: false);
            _transitionTimer = SitToTypeDuration;
            _phase = Phase.TransitionToType;
            Debug.Log($"[{_ctx.AgentName}] Chatting 진입 -- SitToType → Typing");
        }

        public void Update(float deltaTime)
        {
            if (_phase == Phase.TransitionToType)
            {
                _transitionTimer -= deltaTime;
                if (_transitionTimer <= 0f)
                {
                    _ctx.Animation.PlayAnimation("Typing", loop: true);
                    _phase = Phase.Typing;
                }
            }
        }

        public void Exit()
        {
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
