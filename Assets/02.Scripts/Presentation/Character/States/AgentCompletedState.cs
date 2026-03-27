using System;
using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 완료 상태 — 의자에 기대거나 세레머니 모션
    /// 일정 시간 후 Idle로 자동 복귀
    /// </summary>
    public class AgentCompletedState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;
        public event Action OnCompletionAnimDone;

        private float _timer;
        private const float CompleteDuration = 3f;   // 3초 후 Idle 복귀 신호

        public string Name => "Completed";

        public AgentCompletedState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.Animation.PlayAnimation("Celebrate", loop: false);
            _timer = CompleteDuration;
            Debug.Log($"[{_ctx.AgentName}] Completed 진입 ");
        }

        public void Update(float deltaTime)
        {
            _timer -= deltaTime;
            if (_timer <= 0f)
            {
                OnCompletionAnimDone?.Invoke();
                _timer = float.MaxValue; // 중복 호출 방지
            }
        }

        public void Exit() { }
    }
}
