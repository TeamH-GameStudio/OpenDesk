using System;
using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character.States
{
    /// <summary>
    /// 완료 상태 — 응답 완료 후 앉은 채로 대기.
    /// 추가 프롬프트가 오면 Thinking으로 재진입 (앉은 채).
    /// "작업 완료" 시 90도 회전 → SitToStand → Idle.
    /// </summary>
    public class AgentCompletedState : IAgentState
    {
        private readonly AgentCharacterContext _ctx;
        private AgentThinkingState _thinkingState;

        public event Action OnCompletionAnimDone;

        private enum Phase { TypeToSit, Waiting, Rotating, StandingUp, Cheering }
        private Phase _phase;
        private float _timer;

        private const float TypeToSitDuration = 1.0f;
        private const float RotateDuration = 0.5f;
        private const float SlideDuration = 0.4f;
        private const float SlideDistance = 1.0f; // 의자 밖으로 충분히 이동
        private const float SitToStandDuration = 1.5f;
        private const float CheerDuration = 3f;

        // 회전용
        private Quaternion _rotateStart;
        private Quaternion _rotateTarget;

        // 슬라이드용
        private Vector3 _slideStart;
        private Vector3 _slideTarget;

        public string Name => "Completed";

        public AgentCompletedState(AgentCharacterContext ctx) => _ctx = ctx;

        /// <summary>ThinkingState 참조 설정 (BuildFSM에서 호출)</summary>
        public void SetThinkingState(AgentThinkingState thinkingState)
        {
            _thinkingState = thinkingState;
        }

        public void Enter()
        {
            _ctx.StopMoving();
            _ctx.Expression?.SetExpression("Happy");

            _ctx.Animation.PlayAnimation("TypeToSit", loop: false);
            _timer = TypeToSitDuration;
            _phase = Phase.TypeToSit;
            Debug.Log($"[{_ctx.AgentName}] Completed -- 앉은 채로 대기");
        }

        public void Update(float deltaTime)
        {
            _timer -= deltaTime;
            if (_timer > 0f)
            {
                if (_ctx.Transform != null)
                {
                    float t;
                    // 회전 보간
                    if (_phase == Phase.Rotating)
                    {
                        t = Mathf.Clamp01(1f - (_timer / RotateDuration));
                        _ctx.Transform.rotation = Quaternion.Slerp(_rotateStart, _rotateTarget, t);
                    }
                    // 슬라이드 보간 (StandingUp 중 이동 + 높이 내려가기 동시)
                    else if (_phase == Phase.StandingUp)
                    {
                        t = Mathf.Clamp01(1f - (_timer / SitToStandDuration));
                        _ctx.Transform.position = Vector3.Lerp(_slideStart, _slideTarget, t);
                    }
                }
                return;
            }

            switch (_phase)
            {
                case Phase.TypeToSit:
                    _ctx.Animation.PlayAnimation("Thinking", loop: true);
                    _phase = Phase.Waiting;
                    _timer = float.MaxValue;
                    break;

                case Phase.Waiting:
                    break;

                case Phase.Rotating:
                    // 회전 완료 → 슬라이드 + SitToStand 동시 시작
                    if (_ctx.Transform != null)
                    {
                        _slideStart = _ctx.Transform.position;
                        // 슬라이드 목표: forward 방향 + NavMesh 높이로 내려가기
                        var groundTarget = _slideStart + _ctx.Transform.forward * SlideDistance;
                        if (UnityEngine.AI.NavMesh.SamplePosition(groundTarget, out var navHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                            groundTarget = navHit.position;
                        _slideTarget = groundTarget;
                    }
                    _ctx.Animation.PlayAnimation("SitToStand", loop: false);
                    _timer = SitToStandDuration; // 슬라이드와 애니메이션이 같은 시간 동안
                    _phase = Phase.StandingUp;
                    break;

                case Phase.StandingUp:
                    // SitToStand + 슬라이드 완료 → 높이 복원 확정 → Cheering
                    _thinkingState?.ResetSeated();
                    _ctx.Animation.PlayAnimation("Cheering", loop: false);
                    _timer = CheerDuration;
                    _phase = Phase.Cheering;
                    break;

                case Phase.Cheering:
                    OnCompletionAnimDone?.Invoke();
                    _timer = float.MaxValue;
                    break;
            }
        }

        /// <summary>"작업 완료" — 90도 회전 후 일어나기</summary>
        public void DismissAgent()
        {
            if (_phase != Phase.Waiting) return;

            _ctx.Expression?.SetExpression("Happy");

            // 90도 회전 (의자에서 옆으로 돌아서 내려오는 느낌)
            if (_ctx.Transform != null)
            {
                _rotateStart = _ctx.Transform.rotation;
                _rotateTarget = _rotateStart * Quaternion.Euler(0, 90f, 0);
            }

            _timer = RotateDuration;
            _phase = Phase.Rotating;
            Debug.Log($"[{_ctx.AgentName}] Dismiss -- 90도 회전 후 일어나기");
        }

        public void Exit()
        {
            _ctx.Expression?.SetExpression("Neutral");
        }
    }
}
