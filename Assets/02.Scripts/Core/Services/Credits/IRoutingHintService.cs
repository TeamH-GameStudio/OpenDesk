using R3;

namespace OpenDesk.Core.Services.Credits
{
    /// <summary>
    /// 라우터에게 넘길 user_complexity_hint 를 PlayerPrefs 와 동기화.
    /// ChatPanel 의 토글 UI 와 미들웨어 config 메시지 사이 어댑터.
    /// </summary>
    public interface IRoutingHintService
    {
        ReadOnlyReactiveProperty<ComplexityHint> Hint { get; }

        ComplexityHint Current { get; }

        void SetHint(ComplexityHint hint);
    }

    public enum ComplexityHint
    {
        Auto,
        Simple,
        Complex,
    }
}
