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
        private const int HeartbeatIntervalMs  = 30_000; // 30초 — 핸드셰이크 후에는 idle timeout 없음

        private string _lastGatewayUrl;
        private string _lastToken;
        private string _pendingToken;
        private bool   _intentionalDisconnect;
        private bool   _disposed;
        private float  _connectedAtTime;  // 연결된 시각 (Time.realtimeSinceStartup)

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

        public void SetGatewayToken(string token)
        {
            _pendingToken = token;
            Debug.Log($"[Bridge] Gateway 토큰 설정됨 ({(string.IsNullOrEmpty(token) ? "없음" : $"{token.Length}자")})");
        }

        public async UniTask ConnectAsync(string gatewayUrl, CancellationToken ct = default)
        {
            // Mock 모드에서는 실제 연결하지 않음
            if (PlayerPrefs.GetInt("OpenDesk_MockMode", 0) == 1)
            {
                Debug.Log("[Bridge] * Mock 모드 — WebSocket 연결 건너뜀");
                _lastGatewayUrl = gatewayUrl;
                return;
            }

            if (_socket != null)
                await DisconnectInternalAsync(intentional: true);

            _lastToken = _pendingToken;
            _intentionalDisconnect = false;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 토큰이 있으면 URL 쿼리 파라미터로 전달 (OpenClaw 방식: connect.params.auth.token)
            var connectUrl = gatewayUrl;
            if (!string.IsNullOrEmpty(_lastToken))
            {
                var separator = gatewayUrl.Contains("?") ? "&" : "?";
                connectUrl = $"{gatewayUrl}{separator}token={_lastToken}";
                Debug.Log("[Bridge] 토큰 인증 쿼리 파라미터 포함");
            }

            _lastGatewayUrl = connectUrl;

            // Origin 헤더 추가 — Gateway의 Control UI origin 체크 통과 필수
            var headers = new System.Collections.Generic.Dictionary<string, string>
            {
                ["Origin"] = "http://127.0.0.1:18789"
            };
            _socket = new WebSocket(connectUrl, headers);

            _socket.OnOpen    += OnOpen;
            _socket.OnMessage += OnMessage;
            _socket.OnError   += OnError;
            _socket.OnClose   += OnClose;

            // NativeWebSocket: Connect()는 연결 종료까지 블로킹하므로 await 금지
            // fire-and-forget로 시작하고, OnOpen 콜백에서 핸드셰이크 진행
            _socket.Connect().ContinueWith(_ => { }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

            // 수신 디스패치 루프 (NativeWebSocket 요구사항 — OnOpen/OnMessage 콜백 전달)
            DispatchLoopAsync(_loopCts.Token).Forget();

            // 하트비트 루프
            HeartbeatLoopAsync(_loopCts.Token).Forget();

            // OnOpen → 핸드셰이크 → _connectionState=true 될 때까지 최대 10초 대기
            for (int i = 0; i < 100; i++)
            {
                if (_connectionState.Value) return;
                await UniTask.Delay(100, cancellationToken: _loopCts.Token);
            }
            Debug.LogWarning("[Bridge] 핸드셰이크 대기 타임아웃 (10초)");
        }

        public async UniTask DisconnectAsync()
        {
            await DisconnectInternalAsync(intentional: true);
        }

        public async UniTask SendMessageAsync(string sessionId, string message, CancellationToken ct = default)
        {
            if (_socket == null || !IsConnected)
            {
                if (_pendingMessages.Count < MaxPendingMessages)
                    _pendingMessages.Enqueue((sessionId, message));
                else
                    Debug.LogWarning("[Bridge] 메시지 버퍼 초과 — 메시지 드롭");
                return;
            }

            // Gateway 프로토콜: "chat.send" 메서드 (operator.write 스코프 필요)
            var escapedMsg = message.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            var msgId = Guid.NewGuid().ToString("N");
            var payload =
                "{\"type\":\"req\"," +
                $"\"id\":\"chat-{msgId}\"," +
                "\"method\":\"chat.send\"," +
                "\"params\":{" +
                    $"\"sessionKey\":\"default\"," +
                    $"\"message\":\"{escapedMsg}\"," +
                    $"\"idempotencyKey\":\"{msgId}\"" +
                "}}";

            Debug.Log($"[Bridge] 채팅 전송: {message}");
            await _socket.SendText(payload);
        }

        // ── 내부 연결/해제 ──────────────────────────────────────────────

        private async UniTask DisconnectInternalAsync(bool intentional)
        {
            _intentionalDisconnect = intentional;
            _loopCts?.Cancel();

            if (_socket != null)
            {
                _socket.OnOpen    -= OnOpen;
                _socket.OnMessage -= OnMessage;
                _socket.OnError   -= OnError;
                _socket.OnClose   -= OnClose;

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
                        // Gateway 프로토콜: req 포맷으로 health RPC 호출 (keepalive 역할)
                        var pingMsg = $"{{\"type\":\"req\",\"id\":\"ping-{Guid.NewGuid():N}\",\"method\":\"health\",\"params\":{{}}}}";
                        await _socket.SendText(pingMsg);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Bridge] 하트비트 전송 실패: {ex.Message}");
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
                Debug.Log("[Bridge] * Mock 모드 — 재연결 건너뜀");
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
                    // 이전 소켓/루프 정리
                    if (_socket != null)
                    {
                        _socket.OnOpen    -= OnOpen;
                        _socket.OnMessage -= OnMessage;
                        _socket.OnError   -= OnError;
                        _socket.OnClose   -= OnClose;
                        _socket = null;
                    }
                    _loopCts?.Cancel();
                    _loopCts?.Dispose();
                    _loopCts = new CancellationTokenSource();

                    // _lastGatewayUrl에 이미 토큰 쿼리 파라미터 포함됨
                    var reconnHeaders = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Origin"] = "http://127.0.0.1:18789"
                    };
                    _socket = new WebSocket(_lastGatewayUrl, reconnHeaders);
                    _socket.OnOpen    += OnOpen;
                    _socket.OnMessage += OnMessage;
                    _socket.OnError   += OnError;
                    _socket.OnClose   += OnClose;

                    _socket.Connect().ContinueWith(_ => { }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);

                    DispatchLoopAsync(_loopCts.Token).Forget();
                    HeartbeatLoopAsync(_loopCts.Token).Forget();

                    // 핸드셰이크 완료 대기 (최대 10초)
                    for (int w = 0; w < 100; w++)
                    {
                        if (_connectionState.Value) break;
                        await UniTask.Delay(100);
                    }

                    if (_connectionState.Value)
                    {
                        Debug.Log("[Bridge] 재연결 성공");
                        return;
                    }
                    Debug.LogWarning("[Bridge] 재연결 핸드셰이크 타임아웃");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Bridge] 재연결 실패: {ex.Message}");
                    if (_socket != null)
                    {
                        _socket.OnOpen    -= OnOpen;
                        _socket.OnMessage -= OnMessage;
                        _socket.OnError   -= OnError;
                        _socket.OnClose   -= OnClose;
                        _socket = null;
                    }
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
            if (_disposed) return;
            _connectedAtTime = Time.realtimeSinceStartup;
            Debug.Log("[Bridge] WebSocket 연결됨 — 핸드셰이크 전송 시작");

            // Gateway 프로토콜: 첫 프레임은 반드시 connect 요청이어야 함
            SendHandshakeAsync().Forget();
        }

        private async UniTaskVoid SendHandshakeAsync()
        {
            try
            {
                // Gateway 프로토콜 v3: openclaw-control-ui + ui 모드
                // dangerouslyDisableDeviceAuth + allowInsecureAuth 설정으로 디바이스 서명 불필요
                var authBlock = string.IsNullOrEmpty(_lastToken)
                    ? ""
                    : $",\"auth\":{{\"token\":\"{_lastToken}\"}}";

                var connectMsg =
                    "{\"type\":\"req\"," +
                    $"\"id\":\"opendesk-connect-{Guid.NewGuid():N}\"," +
                    "\"method\":\"connect\"," +
                    "\"params\":{" +
                        "\"minProtocol\":3,\"maxProtocol\":3," +
                        "\"client\":{\"id\":\"openclaw-control-ui\",\"version\":\"1.0.0\",\"platform\":\"win32\",\"mode\":\"ui\"}," +
                        "\"role\":\"operator\"," +
                        "\"scopes\":[\"operator.admin\",\"operator.read\",\"operator.write\",\"operator.approvals\"]" +
                        authBlock +
                    "}}";

                await _socket.SendText(connectMsg);
                Debug.Log("[Bridge] connect 핸드셰이크 전송 완료 (control-ui + insecure auth)");

                // 연결 완료 상태는 hello-ok 응답 확인 후 설정하지 않고
                // 여기서 바로 설정 (hello-ok 파싱은 OnMessage에서 로깅만)
                _connectionState.Value = true;
                _eventSubject.OnNext(new AgentEvent(AgentActionType.Connected));

                // 버퍼된 메시지 전송
                if (_pendingMessages.Count > 0)
                    FlushPendingMessagesAsync().Forget();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Bridge] 핸드셰이크 전송 실패: {ex.Message}");
            }
        }

        private void OnMessage(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);

            // 핸드셰이크 응답 로깅 (hello-ok 또는 에러)
            if (json.Contains("\"hello-ok\"") || json.Contains("\"connect\""))
                Debug.Log($"[Bridge] 서버 응답: {(json.Length > 200 ? json[..200] + "..." : json)}");
            else if (json.Contains("\"error\""))
                Debug.LogWarning($"[Bridge] 서버 에러: {(json.Length > 300 ? json[..300] + "..." : json)}");
            else if (json.Contains("\"event\":\"chat\""))
                Debug.Log($"[Bridge] Chat 이벤트 수신: {(json.Length > 300 ? json[..300] + "..." : json)}");
            else if (json.Contains("\"event\":\"agent\""))
                Debug.Log($"[Bridge] Agent 이벤트 수신: {(json.Length > 200 ? json[..200] + "..." : json)}");

            var e = _parser.Parse(json);

            if (e.HasValue)
            {
                // 채팅 응답 파싱 결과 확인
                if (e.Value.ActionType == Models.AgentActionType.ChatDelta ||
                    e.Value.ActionType == Models.AgentActionType.ChatFinal)
                    Debug.Log($"[Bridge] 채팅 파싱 완료: {e.Value.ActionType} | runId={e.Value.RunId} | msg={( e.Value.Message.Length > 80 ? e.Value.Message[..80] + "..." : e.Value.Message)}");

                _eventSubject.OnNext(e.Value);
            }
        }

        private void OnError(string errorMsg)
        {
            Debug.LogError($"[Bridge] WebSocket 오류: {errorMsg}");
        }

        private void OnClose(WebSocketCloseCode code)
        {
            _connectionState.Value = false;

            if (_disposed) return;

            if (!_intentionalDisconnect)
            {
                var uptime = Time.realtimeSinceStartup - _connectedAtTime;
                Debug.LogWarning($"[Bridge] 연결 끊김 (코드: {code}, 유지시간: {uptime:F1}초) — 자동 재연결 시작");
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
            if (_disposed) return;
            _disposed = true;

            _intentionalDisconnect = true;
            _loopCts?.Cancel();

            // 소켓 이벤트 핸들러 해제 + 닫기
            if (_socket != null)
            {
                _socket.OnOpen    -= OnOpen;
                _socket.OnMessage -= OnMessage;
                _socket.OnError   -= OnError;
                _socket.OnClose   -= OnClose;

                try { _ = _socket.Close(); }
                catch { /* Dispose 중 예외 무시 */ }
                _socket = null;
            }

            _loopCts?.Dispose();
            _eventSubject.Dispose();
            _connectionState.Dispose();
            _pendingMessages.Clear();
        }
    }
}
