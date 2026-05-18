using System;
using OpenDesk.Core.Services.Credits;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Credits
{
    /// <summary>
    /// 라우터가 잔액 부족(credit.insufficient) 을 푸시하면 모달을 보여준다.
    /// 충전은 Phase 2 — 현재는 안내만, 닫기로 dismiss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class InsufficientCreditsModal : MonoBehaviour, IDisposable
    {
        private UIDocument _document;
        private ICreditBalanceService _credits;
        private VisualElement _root;
        private Label _required;
        private Label _balance;
        private Button _dismissBtn;
        private Button _topupBtn;
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

            _root = root.Q<VisualElement>("insufficient-credits-modal");
            _required = root.Q<Label>("insufficient-credits-modal__required");
            _balance = root.Q<Label>("insufficient-credits-modal__balance");
            _dismissBtn = root.Q<Button>("insufficient-credits-modal__dismiss");
            _topupBtn = root.Q<Button>("insufficient-credits-modal__topup");

            if (_dismissBtn != null) _dismissBtn.clicked += Hide;
            if (_topupBtn != null)
            {
                _topupBtn.SetEnabled(false); // Phase 2 까지 비활성
                _topupBtn.clicked += Hide;
            }

            if (_credits != null)
            {
                _insufficientSub = _credits.OnInsufficient.Subscribe(Show);
            }

            Hide();
        }

        private void OnDisable() => Dispose();

        private void Show(CreditInsufficient evt)
        {
            if (_required != null) _required.text = evt.Required.ToString("N0");
            if (_balance != null) _balance.text = evt.Balance.ToString("N0");
            if (_root != null) _root.EnableInClassList("ic-modal--hidden", false);
        }

        private void Hide()
        {
            if (_root != null) _root.EnableInClassList("ic-modal--hidden", true);
        }

        public void Dispose()
        {
            _insufficientSub?.Dispose();
            _insufficientSub = null;
            if (_dismissBtn != null) _dismissBtn.clicked -= Hide;
            if (_topupBtn != null) _topupBtn.clicked -= Hide;
        }
    }
}
