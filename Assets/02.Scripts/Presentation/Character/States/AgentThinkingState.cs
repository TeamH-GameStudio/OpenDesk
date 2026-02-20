using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 사고 중 상태 — 턱 괴기, 고개 갸웃 모션
    /// LLM 응답 대기 중 (Thinking 이벤트)
    /// </summary>
    public class AgentThinkingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        public string Name => "Thinking";

        public AgentThinkingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.Animation.PlayAnimation("Thinking", loop: true);
            Debug.Log($"[{_ctx.AgentName}] Thinking 진입");
        }

        public void Update(float deltaTime) { }

        public void Exit() { }
    }
}
