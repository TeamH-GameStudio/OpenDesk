using System;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Services.Credits;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations.Credits
{
    /// <summary>
    /// ClaudeWebSocketClient 의 credit.* 이벤트를 구독하여 R3 reactive 상태로 발행.
    /// </summary>
    public sealed class CreditBalanceService : ICreditBalanceService, IDisposable
    {
        public const string PrefsKeyBalanceCache = "OpenDesk_Credits_BalanceCache";

        private readonly ClaudeWebSocketClient _ws;
        private readonly ReactiveProperty<long> _balance;
        private readonly ReactiveProperty<long> _held = new(0);
        private readonly ReactiveProperty<string> _lastTier = new(string.Empty);
        private readonly ReactiveProperty<string> _lastModel = new(string.Empty);
        private readonly Subject<CreditInsufficient> _onInsufficient = new();

        public ReadOnlyReactiveProperty<long> Balance { get; }
        public ReadOnlyReactiveProperty<long> Held { get; }
        public ReadOnlyReactiveProperty<string> LastTier { get; }
        public ReadOnlyReactiveProperty<string> LastModel { get; }
        public Observable<CreditInsufficient> OnInsufficient => _onInsufficient;

        [Inject]
        public CreditBalanceService(ClaudeWebSocketClient ws = null)
        {
            _ws = ws;

            var cached = PlayerPrefs.GetInt(PrefsKeyBalanceCache, 0);
            _balance = new ReactiveProperty<long>(cached);

            Balance = _balance.ToReadOnlyReactiveProperty();
            Held = _held.ToReadOnlyReactiveProperty();
            LastTier = _lastTier.ToReadOnlyReactiveProperty();
            LastModel = _lastModel.ToReadOnlyReactiveProperty();

            if (_ws != null)
            {
                _ws.OnCreditBalance += HandleBalance;
                _ws.OnCreditRouting += HandleRouting;
                _ws.OnCreditSettled += HandleSettled;
                _ws.OnCreditInsufficient += HandleInsufficient;
                _ws.OnLicenseActivated += HandleActivated;
            }
        }

        private void HandleBalance(CreditBalanceMessage msg)
        {
            if (msg == null) return;
            _balance.Value = msg.balance;
            _held.Value = msg.held;
            PlayerPrefs.SetInt(PrefsKeyBalanceCache, (int)Math.Min(msg.balance, int.MaxValue));
            PlayerPrefs.Save();
        }

        private void HandleRouting(CreditRoutingMessage msg)
        {
            if (msg == null) return;
            _lastTier.Value = msg.tier ?? string.Empty;
            _lastModel.Value = msg.model ?? string.Empty;
        }

        private void HandleSettled(CreditSettledMessage msg)
        {
            if (msg == null) return;
            _balance.Value = msg.balance;
            // held 는 credit.balance 에서 같이 들어오지만 settle 후 즉시 보정.
            _held.Value = 0;
            PlayerPrefs.SetInt(PrefsKeyBalanceCache, (int)Math.Min(msg.balance, int.MaxValue));
            PlayerPrefs.Save();
        }

        private void HandleInsufficient(CreditInsufficientMessage msg)
        {
            if (msg == null) return;
            _onInsufficient.OnNext(new CreditInsufficient(
                Code: msg.code ?? string.Empty,
                Required: msg.required,
                Balance: msg.balance));
        }

        private void HandleActivated(LicenseActivatedMessage msg)
        {
            if (msg == null) return;
            // 활성화 직후 서버가 초기 잔액을 알려준다 — 캐시 즉시 갱신.
            _balance.Value = msg.balance;
            PlayerPrefs.SetInt(PrefsKeyBalanceCache, (int)Math.Min(msg.balance, int.MaxValue));
            PlayerPrefs.Save();
        }

        public void Dispose()
        {
            if (_ws != null)
            {
                _ws.OnCreditBalance -= HandleBalance;
                _ws.OnCreditRouting -= HandleRouting;
                _ws.OnCreditSettled -= HandleSettled;
                _ws.OnCreditInsufficient -= HandleInsufficient;
                _ws.OnLicenseActivated -= HandleActivated;
            }
            _balance?.Dispose();
            _held?.Dispose();
            _lastTier?.Dispose();
            _lastModel?.Dispose();
            _onInsufficient?.Dispose();
        }
    }
}
