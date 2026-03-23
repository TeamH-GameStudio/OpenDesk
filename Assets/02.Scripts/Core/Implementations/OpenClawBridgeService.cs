using System;
using System.Collections.Generic;
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
    /// - 자동 재연결 (지수 백오프: 1s → 2s → 4s … 최대 60s)
    /// - 하트비트 ping (55초 간격 — 캐시 TTL 1시간 대응)
    /// - 연결 끊김 시 메시지 버퍼링
    /// </summary>
    public class OpenClawBridgeService : IOpenClawBridgeService
    {
        private readonly IEventParserService _parser;

        private WebSocket               _socket;
        private CancellationTokenSource  _loopCts;
        private readonly Subject<AgentEvent>    _eventSubject      = new();
        private readonly ReactiveProperty<bool> _connectionState   = new(false);

        // 재연결 설정
        private const int MaxReconnectAttempts = 10;
        private const int MaxBackoffMs         = 60_000;
        private const int HeartbeatIntervalMs  = 55_000; // 55초 (캐시 TTL 1시간 대응)

        private string _lastGatewayUrl;
        private bool   _intentionalDisconnect;

        // 연결 끊김 시 메시지 버퍼 (재연결 후 전송)
        private readonly Queue<(string sessionId, string message)> _pendingMessages = new();
        private const int MaxPendingMessages = 50;

        public bool IsConnected => _connectionState.Value;
        public ReadOnlyReactiveProperty<bool> ConnectionState => _connectionState;
        public Observable<AgentEvent> OnEventReceived => _eventSubject;

        public OpenClawBridgeService(IEventParserService parser)
        {
            _parser = parser;
        }

        public async UniTask ConnectAsync(string gatewayUrl, CancellationToken ct = default)
        {
            // Mock 모드에서는 실제 연결하지 않음
            if (PlayerPrefs.GetInt("OpenDesk_MockMode", 0) == 1)
            {
                Debug.Log("[Bridge] ★ Mock 모드 — WebSocket 연결 건너뜀");
                _lastGatewayUrl = gatewayUrl;
                return;
            }

            if (_socket != null)
                await DisconnectInternalAsync(intentional: true);

            _lastGatewayUrl = gatewayUrl;
            _intentionalDisconnect = false;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _socket = new WebSocket(gatewayUrl);

            _socket.OnOpen    += OnOpen;
            _socket.OnMessage += OnMessage;
            _socket.OnError   += OnError;
            _socket.OnClose   += OnClose;

            await _socket.Connect();

            // 수신 디스패치 루프 (NativeWebSocket 요구사항)
            DispatchLoopAsync(_loopCts.Token).Forget();

            // 하트비트 루프
            HeartbeatLoopAsync(_loopCts.Token).Forget();
        }

        public async UniTask DisconnectAsync()
        {
            await DisconnectInternalAsync(intentional: true);
        }

        public async UniTask SendMessageAsync(string sessionId, string message, CancellationToken ct = default)
        {
            if (_socket == null || !IsConnected)
            {
                // 연결 끊김 시 버퍼에 저장
                if (_pendingMessages.Count < MaxPendingMessages)
                    _pendingMessages.Enqueue((sessionId, message));
                else
                    Debug.LogWarning("[Bridge] 메시지 버퍼 초과 — 메시지 드롭");
                return;
            }

            var payload = $"{{\"session_id\":\"{sessionId}\",\"message\":\"{message}\"}}";
            await _socket.SendText(payload);
        }

        // ── 내부 연결/해제 ──────────────────────────────────────────────

        private async UniTask DisconnectInternalAsync(bool intentional)
        {
            _intentionalDisconnect = intentional;
            _loopCts?.Cancel();

            if (_socket != null)
            {
                try { await _socket.Close(); }
                catch (Exception ex) { Debug.LogWarning($"[Bridge] 소켓 종료 중 오류: {ex.Message}"); }
                _socket = null;
            }
        }

        // ── NativeWebSocket 디스패치 루프 (~60fps) ──────────────────────

        private async UniTaskVoid DispatchLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _socket?.DispatchMessageQueue();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Bridge] 디스패치 오류: {ex.Message}");
                }
                await UniTask.Delay(16, cancellationToken: ct); // ~60fps
            }
        }

        // ── 하트비트 ping ───────────────────────────────────────────────

        private async UniTaskVoid HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await UniTask.Delay(HeartbeatIntervalMs, cancellationToken: ct);

                if (_socket != null && IsConnected)
                {
                    try
                    {
                        await _socket.SendText("{\"type\":\"ping\"}");
                    }
                    catch
                    {
                        // ping 실패는 무시 — OnClose에서 재연결 처리
                    }
                }
            }
        }

        // ── 자동 재연결 (지수 백오프) ────────────────────────────────────

        private async UniTaskVoid ReconnectLoopAsync()
        {
            if (_intentionalDisconnect || string.IsNullOrEmpty(_lastGatewayUrl))
                return;

            // Mock 모드에서는 재연결 시도하지 않음
            if (PlayerPrefs.GetInt("OpenDesk_MockMode", 0) == 1)
            {
                Debug.Log("[Bridge] ★ Mock 모드 — 재연결 건너뜀");
                return;
            }

            for (int attempt = 0; attempt < MaxReconnectAttempts; attempt++)
            {
                var delayMs = Math.Min(1000 * (int)Math.Pow(2, attempt), MaxBackoffMs);
                Debug.Log($"[Bridge] 재연결 시도 {attempt + 1}/{MaxReconnectAttempts} ({delayMs}ms 후)");

                await UniTask.Delay(delayMs);

                // 재연결 중에 의도적 해제가 발생했으면 중단
                if (_intentionalDisconnect) return;

                try
                {
                    _loopCts = new CancellationTokenSource();

                    _socket = new WebSocket(_lastGatewayUrl);
                    _socket.OnOpen    += OnOpen;
                    _socket.OnMessage += OnMessage;
                    _socket.OnError   += OnError;
                    _socket.OnClose   += OnClose;

                    await _socket.Connect();

                    DispatchLoopAsync(_loopCts.Token).Forget();
                    HeartbeatLoopAsync(_loopCts.Token).Forget();

                    Debug.Log("[Bridge] 재연결 성공");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Bridge] 재연결 실패: {ex.Message}");
                    _socket = null;
                }
            }

            Debug.LogError($"[Bridge] {MaxReconnectAttempts}회 재연결 실패 — 포기");
            _eventSubject.OnNext(new AgentEvent(AgentActionType.Disconnected));
        }

        // ── 버퍼된 메시지 전송 ──────────────────────────────────────────

        private async UniTask FlushPendingMessagesAsync()
        {
            while (_pendingMessages.Count > 0 && IsConnected)
            {
                var (sessionId, message) = _pendingMessages.Dequeue();
                try
                {
                    await SendMessageAsync(sessionId, message);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Bridge] 버퍼 메시지 전송 실패: {ex.Message}");
                    break;
                }
            }
        }

        // ── WebSocket 콜백 ──────────────────────────────────────────────

        private void OnOpen()
        {
            _connectionState.Value = true;
            _eventSubject.OnNext(new AgentEvent(AgentActionType.Connected));
            Debug.Log("[Bridge] OpenClaw Gateway 연결됨");

            // 버퍼된 메시지 전송
            if (_pendingMessages.Count > 0)
                FlushPendingMessagesAsync().Forget();
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

            if (!_intentionalDisconnect)
            {
                Debug.Log($"[Bridge] 연결 끊김 (코드: {code}) — 자동 재연결 시작");
                _eventSubject.OnNext(new AgentEvent(AgentActionType.Disconnected));
                ReconnectLoopAsync().Forget();
            }
            else
            {
                Debug.Log($"[Bridge] 연결 종료 (의도적): {code}");
            }
        }

        // ── 정리 ────────────────────────────────────────────────────────

        public void Dispose()
        {
            _intentionalDisconnect = true;
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _eventSubject.Dispose();
            _connectionState.Dispose();
            _pendingMessages.Clear();
        }
    }
}
