using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 채팅 응답 중 상태 — Claude가 스트리밍 응답을 보내는 동안.
    /// TypingState와 유사하지만 의자 이동 없이 즉시 Typing 시작.
    /// (이미 의자에 앉아있는 경우가 대부분 — ChatDelta는 Typing 후에 옴)
    /// </summary>
    public class AgentChattingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        public string Name => "Chatting";

        public AgentChattingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            // 이동 중이면 멈추고 바로 타이핑
            _ctx.StopMoving();
            _ctx.Animation.PlayAnimation("Typing", loop: true);
            _ctx.Expression?.SetExpression("Focused");
            Debug.Log($"[{_ctx.AgentName}] Chatting 진입 -- AI 응답 중");
        }

        public void Update(float deltaTime)
        {
            // 스트리밍 중 — 추후 응답 길이에 따라 표정/속도 변화 가능
        }

        public void Exit()
        {
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
