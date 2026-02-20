using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 대기 상태 — 커피 마시기, 스트레칭 등 Idle 모션
    /// 일정 시간마다 랜덤 서브 모션 재생
    /// </summary>
    public class AgentIdleState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;

        // Idle 서브모션 목록 (Mixamo: Sitting Idle, Breathing, Idle)
        private static readonly string[] IdleVariants = { "Idle", "Idle_Breathing", "Idle_LookAround" };

        private float _subMotionTimer;
        private const float SubMotionInterval = 8f; // 8초마다 서브모션

        public string Name => "Idle";

        public AgentIdleState(AgentCharacterContext ctx) => _ctx = ctx;

        public void Enter()
        {
            _ctx.Animation.PlayAnimation("Idle", loop: true);
            _subMotionTimer = SubMotionInterval;
            Debug.Log($"[{_ctx.AgentName}] Idle 진입");
        }

        public void Update(float deltaTime)
        {
            _subMotionTimer -= deltaTime;
            if (_subMotionTimer <= 0f)
            {
                PlayRandomSubMotion();
                _subMotionTimer = SubMotionInterval;
            }
        }

        public void Exit()
        {
            // 다음 상태 진입 전 클린업 없음
        }

        private void PlayRandomSubMotion()
        {
            var pick = IdleVariants[Random.Range(0, IdleVariants.Length)];
            _ctx.Animation.PlayAnimation(pick, loop: false);
            // 완료 후 Idle로 복귀 큐
            _ctx.Animation.QueueAnimation("Idle", loop: true, delay: _ctx.Animation.GetAnimationDuration(pick));
        }
    }
}
