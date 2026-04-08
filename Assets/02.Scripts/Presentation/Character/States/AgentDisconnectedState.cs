using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 오프라인 상태 — 캐릭터 흐릿하게 + 정적 포즈
    /// Gateway 연결 끊겼을 때
    /// </summary>
    public class AgentDisconnectedState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        public string Name => "Disconnected";

        public AgentDisconnectedState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.Animation.SetAnimationTimeScale(0f);
            _ctx.Expression?.SetExpression("Sad");
            Debug.Log($"[{_ctx.AgentName}] Disconnected — 오프라인");
        }

        public void Update(float deltaTime) { }

        public void Exit()
        {
            _ctx.Animation.SetAnimationTimeScale(1f);
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
