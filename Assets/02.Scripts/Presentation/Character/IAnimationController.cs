namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 캐릭터 애니메이션 추상화 인터페이스
    /// </summary>
    public interface IAnimationController
    {
        void PlayAnimation(string animationName, bool loop);
        void QueueAnimation(string animationName, bool loop, float delay);
        float GetAnimationDuration(string animationName);
        void SetAnimationTimeScale(float scale);
    }
}
