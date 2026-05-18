using System;
using OpenDesk.Core.Services.Credits;
using R3;
using UnityEngine.UIElements;

namespace OpenDesk.Presentation.UI.Credits
{
    /// <summary>
    /// 잔액 배지의 비-MonoBehaviour 바인더. 다른 UIDocument 안에 inline 으로
    /// 배지 요소가 들어간 경우 (TopBar / ChatPanel) 호스트 컨트롤러가 본 클래스로
    /// 라벨을 ICreditBalanceService 에 묶는다.
    ///
    /// 호스트 UXML 안에 필요한 요소:
    ///   - `{prefix}` : 루트 (low / insufficient 클래스 토글용)
    ///   - `{prefix}__amount` : 잔액 숫자 표시 라벨
    ///   - `{prefix}__held` : held 표시 라벨 (없으면 0 표시 시 자동 숨김)
    /// </summary>
    public sealed class CreditBalanceBinder : IDisposable
    {
        private const string LowClass = "credit-badge--low";
        private const string InsufficientClass = "credit-badge--insufficient";
        private const string HeldHiddenClass = "credit-badge__held--hidden";

        private readonly VisualElement _root;
        private readonly Label _amount;
        private readonly Label _held;
        private readonly long _lowThreshold;
        private readonly IDisposable _balanceSub;
        private readonly IDisposable _heldSub;
        private readonly IDisposable _insufficientSub;

        public CreditBalanceBinder(
            VisualElement root,
            Label amount,
            Label held,
            ICreditBalanceService credits,
            long lowThreshold = 100)
        {
            _root = root;
            _amount = amount;
            _held = held;
            _lowThreshold = lowThreshold;

            if (credits == null) return;
            _balanceSub = credits.Balance.Subscribe(UpdateBalance);
            _heldSub = credits.Held.Subscribe(UpdateHeld);
            _insufficientSub = credits.OnInsufficient.Subscribe(_ => MarkInsufficient());
        }

        /// <summary>parent 안에서 prefix 로 시작하는 요소 3개를 자동으로 찾아 바인딩.</summary>
        public static CreditBalanceBinder BindByPrefix(
            VisualElement parent,
            string prefix,
            ICreditBalanceService credits,
            long lowThreshold = 100)
        {
            if (parent == null || string.IsNullOrEmpty(prefix)) return null;
            var root = parent.Q<VisualElement>(prefix);
            var amount = parent.Q<Label>($"{prefix}__amount");
            var held = parent.Q<Label>($"{prefix}__held");
            if (root == null || amount == null) return null;
            return new CreditBalanceBinder(root, amount, held, credits, lowThreshold);
        }

        private void UpdateBalance(long balance)
        {
            if (_amount != null)
            {
                _amount.text = balance.ToString("N0");
            }
            if (_root != null)
            {
                _root.EnableInClassList(LowClass, balance < _lowThreshold && balance > 0);
                _root.EnableInClassList(InsufficientClass, balance <= 0);
            }
        }

        private void UpdateHeld(long held)
        {
            if (_held == null) return;
            if (held <= 0)
            {
                _held.text = string.Empty;
                _held.EnableInClassList(HeldHiddenClass, true);
            }
            else
            {
                _held.text = $"-{held:N0} hold";
                _held.EnableInClassList(HeldHiddenClass, false);
            }
        }

        private void MarkInsufficient()
        {
            if (_root != null) _root.EnableInClassList(InsufficientClass, true);
        }

        public void Dispose()
        {
            _balanceSub?.Dispose();
            _heldSub?.Dispose();
            _insufficientSub?.Dispose();
        }
    }
}
