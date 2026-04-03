using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// Unity Animator 기반 IAnimationController 구현체.
    /// AgentAnimatorBuilder가 생성한 Controller의 "State" int 파라미터로 전환.
    /// Play(name) 호출 시 이름 → State int 매핑.
    /// </summary>
    public class UnityAnimatorController : IAnimationController
    {
        private readonly Animator _animator;
        private readonly string _ownerName;

        private static readonly int StateParam = Animator.StringToHash("State");

        // 애니메이션 이름 → State int 매핑
        private static readonly Dictionary<string, int> StateMap = new()
        {
            { "Idle", 0 },
            { "Typing", 1 },
            { "Walk", 2 },
            { "Cheering", 3 },
            { "Celebrate", 3 },   // alias
            { "Thinking", 0 },    // Thinking 전용 클립 없으면 Idle
            { "LookUp", 0 },      // fallback
        };

        public UnityAnimatorController(Animator animator, string ownerName)
        {
            _animator = animator;
            _ownerName = ownerName;
        }

        public void PlayAnimation(string animationName, bool loop)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            if (StateMap.TryGetValue(animationName, out int stateValue))
            {
                _animator.SetInteger(StateParam, stateValue);
            }
            else
            {
                // 매핑에 없는 이름이면 직접 Play 시도
                _animator.Play(animationName, 0, 0f);
            }
        }

        public void QueueAnimation(string animationName, bool loop, float delay)
        {
            // 딜레이 후 전환 — 간단히 Play로 대체
            PlayAnimation(animationName, loop);
        }

        public float GetAnimationDuration(string animationName)
        {
            if (_animator == null) return 1f;

            var clipInfo = _animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfo.Length > 0)
                return clipInfo[0].clip.length;

            return 1f;
        }

        public void SetAnimationTimeScale(float scale)
        {
            if (_animator == null) return;
            _animator.speed = scale;
        }
    }
}
