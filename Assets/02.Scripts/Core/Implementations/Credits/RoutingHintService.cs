using System;
using OpenDesk.Claude;
using OpenDesk.Core.Services.Credits;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations.Credits
{
    public sealed class RoutingHintService : IRoutingHintService, IDisposable
    {
        public const string PrefsKey = "OpenDesk_ComplexityHint";

        private readonly ClaudeWebSocketClient _ws;
        private readonly ReactiveProperty<ComplexityHint> _hint;

        public ReadOnlyReactiveProperty<ComplexityHint> Hint { get; }
        public ComplexityHint Current => _hint.Value;

        [Inject]
        public RoutingHintService(ClaudeWebSocketClient ws = null)
        {
            _ws = ws;
            var stored = PlayerPrefs.GetString(PrefsKey, "auto");
            _hint = new ReactiveProperty<ComplexityHint>(Parse(stored));
            Hint = _hint.ToReadOnlyReactiveProperty();

            // 연결 시 현재 hint 를 자동 push — 미들웨어 세션이 새로 만들어진 경우에도 동기 유지.
            if (_ws != null)
            {
                _ws.OnConnectionChanged += HandleConnectionChanged;
            }
        }

        public void SetHint(ComplexityHint hint)
        {
            if (_hint.Value == hint) return;
            _hint.Value = hint;
            PlayerPrefs.SetString(PrefsKey, Serialize(hint));
            PlayerPrefs.Save();
            PushHint(hint);
        }

        private void HandleConnectionChanged(bool connected, string _)
        {
            if (!connected) return;
            PushHint(_hint.Value);
        }

        private void PushHint(ComplexityHint hint)
        {
            if (_ws == null || !_ws.IsConnected) return;
            _ws.SendComplexityHint(Serialize(hint));
        }

        private static ComplexityHint Parse(string raw)
        {
            return (raw ?? string.Empty).ToLowerInvariant() switch
            {
                "simple" => ComplexityHint.Simple,
                "complex" => ComplexityHint.Complex,
                _ => ComplexityHint.Auto,
            };
        }

        public static string Serialize(ComplexityHint hint) => hint switch
        {
            ComplexityHint.Simple => "simple",
            ComplexityHint.Complex => "complex",
            _ => "auto",
        };

        public void Dispose()
        {
            if (_ws != null)
            {
                _ws.OnConnectionChanged -= HandleConnectionChanged;
            }
            _hint.Dispose();
        }
    }
}
