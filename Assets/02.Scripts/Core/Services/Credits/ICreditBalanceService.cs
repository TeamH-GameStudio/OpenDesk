using R3;

namespace OpenDesk.Core.Services.Credits
{
    /// <summary>
    /// 미들웨어가 푸시하는 credit.* 이벤트를 R3 reactive 상태로 노출.
    /// UI 위젯이 직접 ClaudeWebSocketClient 를 구독하지 않고 본 서비스만 구독하면 된다.
    ///
    /// 서버가 source of truth — 본 서비스는 캐시일 뿐, 신뢰는 settle 시점에 갱신된 balance.
    /// PlayerPrefs `OpenDesk_Credits_BalanceCache` 로 오프라인 표시용 마지막 잔액 보존.
    /// </summary>
    public interface ICreditBalanceService
    {
        /// <summary>가장 최근에 서버로부터 푸시된 잔액.</summary>
        ReadOnlyReactiveProperty<long> Balance { get; }

        /// <summary>현재 hold (진행 중 task 가 차감해둔 양).</summary>
        ReadOnlyReactiveProperty<long> Held { get; }

        /// <summary>마지막 라우팅 결정 tier — UI 가 작은 배지로 표시.</summary>
        ReadOnlyReactiveProperty<string> LastTier { get; }

        /// <summary>마지막 라우팅된 model.</summary>
        ReadOnlyReactiveProperty<string> LastModel { get; }

        /// <summary>잔액 부족 발생 이벤트 (required, balance).</summary>
        Observable<CreditInsufficient> OnInsufficient { get; }
    }

    public sealed record CreditInsufficient(string Code, long Required, long Balance);
}
