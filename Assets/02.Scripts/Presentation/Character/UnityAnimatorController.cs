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
        // 0=Idle, 1=Typing, 2=Walk, 3=Cheering, 4=Thinking(Drinking), 5=Sleeping
        // 6=StandToSit, 7=SitToStand, 8=SitToType, 9=TypeToSit, 10=Error
        private static readonly Dictionary<string, int> StateMap = new()
        {
            { "Idle", 0 },
            { "Typing", 1 },
            { "Walk", 2 },
            { "Cheering", 3 },
            { "Celebrate", 3 },     // alias
            { "Thinking", 4 },      // Drinking 모션
            { "Drinking", 4 },      // alias
            { "Sleeping", 5 },
            { "StandToSit", 6 },
            { "SitToStand", 7 },
            { "SitToType", 8 },
            { "TypeToSit", 9 },
            { "Error", 10 },        // Female Standing Pose
            { "LookUp", 0 },        // fallback
        };

        public UnityAnimatorController(Animator animator, string ownerName)
        {
            _animator = animator;
            _ownerName = ownerName;
        }

        public void PlayAnimation(string animationName, bool loop)
        {
            if (_animator == null)
            {
                Debug.LogWarning($"[AnimCtrl:{_ownerName}] Animator is NULL -- {animationName} 무시됨");
                return;
            }
            if (_animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[AnimCtrl:{_ownerName}] RuntimeAnimatorController is NULL -- {animationName} 무시됨");
                return;
            }

            if (StateMap.TryGetValue(animationName, out int stateValue))
            {
                _animator.SetInteger(StateParam, stateValue);
                Debug.Log($"[AnimCtrl:{_ownerName}] PlayAnimation({animationName}) -> State={stateValue}");
            }
            else
            {
                _animator.Play(animationName, 0, 0f);
                Debug.Log($"[AnimCtrl:{_ownerName}] PlayAnimation({animationName}) -> Direct Play");
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
