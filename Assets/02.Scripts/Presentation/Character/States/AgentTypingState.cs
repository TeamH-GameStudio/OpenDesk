using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 타이핑 상태 — 이미 컴퓨터 앞에 앉아있는 상태에서 타이핑.
    /// Thinking에서 의자 이동이 완료된 후 전환됨.
    /// </summary>
    public class AgentTypingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        public string Name => "Typing";

        public AgentTypingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Typing", loop: true);
            _ctx.Expression?.SetExpression("Focused");
            Debug.Log($"[{_ctx.AgentName}] Typing 진입 -- 타이핑 시작");
        }

        public void Update(float deltaTime)
        {
            // 타이핑 루프 중 — 추후 타이핑 속도 변화 등 추가 가능
        }

        public void Exit()
        {
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
