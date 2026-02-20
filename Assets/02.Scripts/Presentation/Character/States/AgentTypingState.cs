using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 타이핑 상태 — 작업 중 모션
    /// Thinking → Typing 순서로 전환 (일 받으면 먼저 고개 듦)
    /// </summary>
    public class AgentTypingState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        private const float LookUpDuration = 0.6f;   // 고개 드는 짧은 모션
        private float _lookUpTimer;
        private bool _isTyping;

        public string Name => "Typing";

        public AgentTypingState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            Debug.Log($"[{_ctx.AgentName}] Typing 진입 — 태스크 수신");

            // 1. 고개 드는 모션 먼저
            _ctx.Animation.PlayAnimation("LookUp", loop: false);
            _lookUpTimer = LookUpDuration;
            _isTyping    = false;
        }

        public void Update(float deltaTime)
        {
            if (_isTyping) return;

            _lookUpTimer -= deltaTime;
            if (_lookUpTimer <= 0f)
            {
                // 2. 타이핑 모션으로 전환
                _ctx.Animation.PlayAnimation("Typing", loop: true);
                _isTyping = true;
                Debug.Log($"[{_ctx.AgentName}] Typing 시작");
            }
        }

        public void Exit()
        {
            _isTyping = false;
        }
    }
}
