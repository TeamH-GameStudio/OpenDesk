using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NativeWebSocket;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// OpenClaw Gateway WebSocket 연결/이벤트 수신
    /// UniTask 기반 비동기 처리로 메인 스레드 블로킹 없음
    /// </summary>
    public class OpenClawBridgeService : IOpenClawBridgeService
    {
        private readonly IEventParserService _parser;

        private WebSocket               _socket;
        private CancellationTokenSource _loopCts;
        private readonly Subject<AgentEvent>    _eventSubject      = new();
        private readonly ReactiveProperty<bool> _connectionState   = new(false);

        public bool IsConnected => _connectionState.Value;
        public ReadOnlyReactiveProperty<bool> ConnectionState => _connectionState;
        public Observable<AgentEvent> OnEventReceived => _eventSubject;

        public OpenClawBridgeService(IEventParserService parser)
        {
            _parser = parser;
        }

        public async UniTask ConnectAsync(string gatewayUrl, CancellationToken ct = default)
        {
            if (_socket != null)
                await DisconnectAsync();

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _socket = new WebSocket(gatewayUrl);

            _socket.OnOpen    += OnOpen;
            _socket.OnMessage += OnMessage;
            _socket.OnError   += OnError;
            _socket.OnClose   += OnClose;

            await _socket.Connect();

            // 수신 루프 (백그라운드)
            DispatchLoopAsync(_loopCts.Token).Forget();
        }

        public async UniTask DisconnectAsync()
        {
            _loopCts?.Cancel();

            if (_socket != null)
            {
                await _socket.Close();
                _socket = null;
            }
        }

        public async UniTask SendMessageAsync(string sessionId, string message, CancellationToken ct = default)
        {
            if (_socket == null || !IsConnected)
            {
                Debug.LogWarning("[Bridge] 연결되지 않은 상태에서 메시지 전송 시도");
                return;
            }

            var payload = $"{{\"session_id\":\"{sessionId}\",\"message\":\"{message}\"}}";
            await _socket.SendText(payload);
        }

        // NativeWebSocket은 메인 스레드에서 DispatchMessageQueue() 호출 필요
        private async UniTaskVoid DispatchLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _socket?.DispatchMessageQueue();
                await UniTask.Delay(16, cancellationToken: ct); // ~60fps
            }
        }

        private void OnOpen()
        {
            _connectionState.Value = true;
            _eventSubject.OnNext(new AgentEvent(AgentActionType.Connected));
            Debug.Log("[Bridge] OpenClaw Gateway 연결됨");
        }

        private void OnMessage(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var e    = _parser.Parse(json);

            if (e.HasValue)
                _eventSubject.OnNext(e.Value);
        }

        private void OnError(string errorMsg)
        {
            Debug.LogError($"[Bridge] WebSocket 오류: {errorMsg}");
        }

        private void OnClose(WebSocketCloseCode code)
        {
            _connectionState.Value = false;
            _eventSubject.OnNext(new AgentEvent(AgentActionType.Disconnected));
            Debug.Log($"[Bridge] 연결 종료: {code}");
        }

        public void Dispose()
        {
            _loopCts?.Cancel();
            _eventSubject.Dispose();
            _connectionState.Dispose();
        }
    }
}
