using System;
using OpenDesk.Core.Services.Credits;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Credits
{
    /// <summary>
    /// TopBar inset 으로 표시하는 잔액 배지. ICreditBalanceService 의 Balance/Held 를 R3 로 구독.
    /// 잔액 부족 임계치(LowThreshold) 미만이면 색상 변경.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CreditBalanceBadge : MonoBehaviour, IDisposable
    {
        [SerializeField] private long _lowThreshold = 100;

        private UIDocument _document;
        private ICreditBalanceService _credits;
        private VisualElement _root;
        private Label _amount;
        private Label _held;
        private IDisposable _balanceSub;
        private IDisposable _heldSub;
        private IDisposable _insufficientSub;

        [Inject]
        public void Construct(ICreditBalanceService credits)
        {
            _credits = credits;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = _document?.rootVisualElement;
            if (root == null) return;
            _root = root.Q<VisualElement>("credit-balance-badge");
            _amount = root.Q<Label>("credit-balance-badge__amount");
            _held = root.Q<Label>("credit-balance-badge__held");

            if (_credits != null)
            {
                _balanceSub = _credits.Balance.Subscribe(UpdateBalance);
                _heldSub = _credits.Held.Subscribe(UpdateHeld);
                _insufficientSub = _credits.OnInsufficient.Subscribe(_ => MarkInsufficient());
            }
        }

        private void OnDisable() => Dispose();

        private void UpdateBalance(long balance)
        {
            if (_amount != null)
            {
                _amount.text = balance.ToString("N0");
            }
            if (_root != null)
            {
                _root.EnableInClassList("credit-badge--low", balance < _lowThreshold && balance > 0);
                _root.EnableInClassList("credit-badge--insufficient", balance <= 0);
            }
        }

        private void UpdateHeld(long held)
        {
            if (_held == null) return;
            if (held <= 0)
            {
                _held.text = string.Empty;
                _held.EnableInClassList("credit-badge__held--hidden", true);
            }
            else
            {
                _held.text = $"-{held:N0} hold";
                _held.EnableInClassList("credit-badge__held--hidden", false);
            }
        }

        private void MarkInsufficient()
        {
            if (_root == null) return;
            _root.EnableInClassList("credit-badge--insufficient", true);
        }

        public void Dispose()
        {
            _balanceSub?.Dispose();
            _heldSub?.Dispose();
            _insufficientSub?.Dispose();
            _balanceSub = null;
            _heldSub = null;
            _insufficientSub = null;
        }
    }
}
