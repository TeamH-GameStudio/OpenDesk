using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// Unity Animator 기반 IAnimationController 구현체
    /// </summary>
    public class UnityAnimatorController : IAnimationController
    {
        private readonly Animator _animator;
        private readonly string _ownerName;

        public UnityAnimatorController(Animator animator, string ownerName)
        {
            _animator  = animator;
            _ownerName = ownerName;
        }

        public void PlayAnimation(string animationName, bool loop)
        {
            if (_animator == null) return;
            _animator.Play(animationName);
        }

        public void QueueAnimation(string animationName, bool loop, float delay)
        {
            if (_animator == null) return;
            // CrossFadeInFixedTime으로 지연 전환
            _animator.CrossFadeInFixedTime(animationName, 0.2f, 0, delay);
        }

        public float GetAnimationDuration(string animationName)
        {
            if (_animator == null) return 0f;

            var clipInfo = _animator.GetCurrentAnimatorClipInfo(0);
            if (clipInfo.Length > 0)
                return clipInfo[0].clip.length;

            return 1f; // fallback
        }

        public void SetAnimationTimeScale(float scale)
        {
            if (_animator == null) return;
            _animator.speed = scale;
        }
    }
}
