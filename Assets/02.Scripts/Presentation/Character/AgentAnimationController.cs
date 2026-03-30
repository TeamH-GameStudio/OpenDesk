using OpenDesk.Core.Models;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 에이전트 상태에 따른 애니메이션 전환 컨트롤러.
    /// Animator 파라미터 "State" (int)로 상태 전환.
    ///
    /// 애니메이션 클립 매핑:
    ///   0 = Idle       (Idle.fbx)
    ///   1 = Typing     (Typing.fbx)
    ///   2 = Walk       (Standard Walk.fbx)
    ///   3 = Cheering   (Cheering.fbx)
    ///
    /// Animator Controller에서 위 State 값으로 Transition 설정 필요.
    /// </summary>
    public class AgentAnimationController : MonoBehaviour
    {
        private Animator _animator;
        private AgentAnimState _currentState = AgentAnimState.Idle;

        private static readonly int StateParam = Animator.StringToHash("State");

        public enum AgentAnimState
        {
            Idle = 0,
            Typing = 1,
            Walk = 2,
            Cheering = 3,
        }

        public void Initialize(Animator animator)
        {
            _animator = animator;
            if (_animator != null && _animator.runtimeAnimatorController != null)
                SetState(AgentAnimState.Idle);
        }

        public AgentAnimState CurrentState => _currentState;

        /// <summary>직접 애니메이션 상태 설정</summary>
        public void SetState(AgentAnimState state)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            _currentState = state;
            _animator.SetInteger(StateParam, (int)state);
        }

        /// <summary>AgentActionType → 애니메이션 상태 자동 매핑</summary>
        public void ApplyActionType(AgentActionType action)
        {
            var animState = MapActionToAnim(action);
            SetState(animState);
        }

        /// <summary>
        /// 액션 타입 → 애니메이션 매핑.
        /// 추후 사용자 지정에 따라 수정 예정.
        /// </summary>
        private static AgentAnimState MapActionToAnim(AgentActionType action)
        {
            return action switch
            {
                AgentActionType.Idle => AgentAnimState.Idle,

                // AI가 응답 중 → Typing
                AgentActionType.Thinking => AgentAnimState.Typing,
                AgentActionType.Planning => AgentAnimState.Typing,
                AgentActionType.Executing => AgentAnimState.Typing,
                AgentActionType.Reviewing => AgentAnimState.Typing,
                AgentActionType.ChatDelta => AgentAnimState.Typing,
                AgentActionType.ToolUsing => AgentAnimState.Typing,
                AgentActionType.ToolResult => AgentAnimState.Typing,

                // 완료 → Cheering
                AgentActionType.ChatFinal => AgentAnimState.Cheering,
                AgentActionType.TaskCompleted => AgentAnimState.Cheering,

                // 시작/연결 → Walk
                AgentActionType.TaskStarted => AgentAnimState.Walk,
                AgentActionType.Connected => AgentAnimState.Idle,

                // 에러/연결끊김 → Idle
                AgentActionType.TaskFailed => AgentAnimState.Idle,
                AgentActionType.Disconnected => AgentAnimState.Idle,

                _ => AgentAnimState.Idle,
            };
        }
    }
}
