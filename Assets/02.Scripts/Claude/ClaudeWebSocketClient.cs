using System;
using System.Threading;
using NativeWebSocket;
using OpenDesk.Claude.Models;
using UnityEngine;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Python 미들웨어 서버와 WebSocket 통신 전담.
    /// 연결/재연결/프로토콜 메시지 송수신 처리.
    /// </summary>
    public class ClaudeWebSocketClient : MonoBehaviour
    {
        [Header("서버 설정")]
        [SerializeField] private string _serverUrl = "ws://localhost:8765";
        [SerializeField] private float  _reconnectInterval = 3f;
        [SerializeField] private int    _maxReconnectAttempts = 5;

        private WebSocket _socket;
        private bool      _isConnected;
        private bool      _intentionalDisconnect;
        private int       _reconnectAttempts;
        private string    _currentModel = "";
        private CancellationTokenSource _cts;

        // ── 공개 프로퍼티 ──────────────────────────────────────

        public bool   IsConnected  => _isConnected;
        public string CurrentModel => _currentModel;

        // ── 이벤트 ────────────────────────────────────────────

        /// <summary>스트리밍 텍스트 청크 수신</summary>
        public event Action<string> OnDelta;

        /// <summary>최종 완성 응답 + 비용</summary>
        public event Action<string, float> OnFinal;

        /// <summary>에러 메시지</summary>
        public event Action<string> OnError;

        /// <summary>연결 상태 변경 (connected, modelName)</summary>
        public event Action<bool, string> OnConnectionChanged;

        /// <summary>히스토리 초기화 완료</summary>
        public event Action OnCleared;

        /// <summary>에이전트 상태 변화 ("💭 사고 중...", "🔧 도구 호출: xxx" 등)</summary>
        public event Action<string> OnStatus;

        // ── 생명주기 ──────────────────────────────────────────

        private async void Start()
        {
            _cts = new CancellationTokenSource();
            await ConnectAsync();
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _socket?.DispatchMessageQueue();
#endif
        }

        private void OnDestroy()
        {
            _intentionalDisconnect = true;
            _cts?.Cancel();
            _cts?.Dispose();
            if (_socket != null)
            {
                _socket.OnOpen    -= HandleOpen;
                _socket.OnMessage -= HandleMessage;
                _socket.OnError   -= HandleError;
                _socket.OnClose   -= HandleClose;

                try { _ = _socket.Close(); }
                catch { /* Destroy 중 예외 무시 */ }
                _socket = null;
            }
        }

        // ── 연결 ──────────────────────────────────────────────

        public async Cysharp.Threading.Tasks.UniTask ConnectAsync()
        {
            _intentionalDisconnect = false;
            _reconnectAttempts = 0;

            await CreateAndConnect();
        }

        public async Cysharp.Threading.Tasks.UniTask DisconnectAsync()
        {
            _intentionalDisconnect = true;
            if (_socket != null && _socket.State == WebSocketState.Open)
                await _socket.Close();
        }

        private async Cysharp.Threading.Tasks.UniTask CreateAndConnect()
        {
            // 기존 소켓 정리
            if (_socket != null)
            {
                _socket.OnOpen    -= HandleOpen;
                _socket.OnMessage -= HandleMessage;
                _socket.OnError   -= HandleError;
                _socket.OnClose   -= HandleClose;
                try { _ = _socket.Close(); } catch { }
            }

            _socket = new WebSocket(_serverUrl);
            _socket.OnOpen    += HandleOpen;
            _socket.OnMessage += HandleMessage;
            _socket.OnError   += HandleError;
            _socket.OnClose   += HandleClose;

            Debug.Log($"[ClaudeWS] 연결 시도: {_serverUrl}");

            try
            {
                // NativeWebSocket.Connect()는 연결 종료까지 반환하지 않으므로 fire-and-forget
                _socket.Connect().ContinueWith(
                    _ => { },
                    System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] 연결 실패: {ex.Message}");
            }
        }

        // ── 전송 메서드 ───────────────────────────────────────

        public async void SendChat(string message)
        {
            if (!_isConnected || _socket == null)
            {
                OnError?.Invoke("서버에 연결되지 않았습니다");
                return;
            }

            var req = new ChatRequest { message = message };
            var json = JsonUtility.ToJson(req);
            Debug.Log($"[ClaudeWS] 전송: {message.Substring(0, Math.Min(message.Length, 50))}...");
            await _socket.SendText(json);
        }

        public async void SendClear()
        {
            if (!_isConnected || _socket == null) return;
            var json = JsonUtility.ToJson(new ClearRequest());
            await _socket.SendText(json);
        }

        public async void SendConfig(string systemPrompt)
        {
            if (!_isConnected || _socket == null) return;
            var req = new ConfigRequest { systemPrompt = systemPrompt };
            await _socket.SendText(JsonUtility.ToJson(req));
        }

        public async void SendPing()
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(new PingRequest()));
        }

        /// <summary>대화 히스토리 JSON을 전송하여 세션 이어나기</summary>
        public async void SendResume(string conversationJson)
        {
            if (!_isConnected || _socket == null)
            {
                OnError?.Invoke("서버에 연결되지 않았습니다");
                return;
            }

            var req = new ResumeRequest { conversation = conversationJson };
            var json = JsonUtility.ToJson(req);
            Debug.Log($"[ClaudeWS] resume 전송: {conversationJson.Length}자");
            await _socket.SendText(json);
        }

        // ── WebSocket 이벤트 핸들러 ───────────────────────────

        private void HandleOpen()
        {
            Debug.Log("[ClaudeWS] WebSocket 연결됨");
            _reconnectAttempts = 0;
            // connected 메시지는 서버가 보내줌 → HandleMessage에서 처리
        }

        private void HandleMessage(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);

            // type 필드 먼저 파싱
            var baseMsg = JsonUtility.FromJson<ServerMessage>(json);
            if (baseMsg == null || string.IsNullOrEmpty(baseMsg.type))
            {
                Debug.LogWarning($"[ClaudeWS] 알 수 없는 메시지: {json.Substring(0, Math.Min(json.Length, 100))}");
                return;
            }

            switch (baseMsg.type)
            {
                case "connected":
                    var connMsg = JsonUtility.FromJson<ConnectedMessage>(json);
                    _currentModel = connMsg?.model ?? "";
                    _isConnected = true;
                    Debug.Log($"[ClaudeWS] 서버 연결 확인: model={_currentModel}");
                    OnConnectionChanged?.Invoke(true, _currentModel);
                    break;

                case "delta":
                    var deltaMsg = JsonUtility.FromJson<DeltaMessage>(json);
                    if (deltaMsg != null && !string.IsNullOrEmpty(deltaMsg.text))
                        OnDelta?.Invoke(deltaMsg.text);
                    break;

                case "final":
                    var finalMsg = JsonUtility.FromJson<FinalMessage>(json);
                    if (finalMsg != null)
                        OnFinal?.Invoke(finalMsg.text ?? "", finalMsg.cost);
                    break;

                case "error":
                    var errorMsg = JsonUtility.FromJson<ErrorMessage>(json);
                    var errText = errorMsg?.message ?? "알 수 없는 에러";
                    Debug.LogWarning($"[ClaudeWS] 서버 에러 [{errorMsg?.code}]: {errText}");
                    OnError?.Invoke(errText);
                    break;

                case "cleared":
                    Debug.Log("[ClaudeWS] 히스토리 초기화 완료");
                    OnCleared?.Invoke();
                    break;

                case "status":
                    var statusMsg = JsonUtility.FromJson<StatusMessage>(json);
                    if (statusMsg != null && !string.IsNullOrEmpty(statusMsg.text))
                        OnStatus?.Invoke(statusMsg.text);
                    break;

                case "pong":
                    // 하트비트 응답 — 무시
                    break;

                case "config_updated":
                    Debug.Log("[ClaudeWS] 설정 업데이트 완료");
                    break;

                default:
                    Debug.Log($"[ClaudeWS] 미처리 메시지 타입: {baseMsg.type}");
                    break;
            }
        }

        private void HandleError(string errorMsg)
        {
            Debug.LogError($"[ClaudeWS] WebSocket 오류: {errorMsg}");
        }

        private void HandleClose(WebSocketCloseCode code)
        {
            var wasConnected = _isConnected;
            _isConnected = false;

            if (wasConnected)
            {
                Debug.LogWarning($"[ClaudeWS] 연결 끊김 (code: {code})");
                OnConnectionChanged?.Invoke(false, "");
            }

            if (!_intentionalDisconnect)
                TryReconnect();
        }

        // ── 자동 재연결 ──────────────────────────────────────

        private async void TryReconnect()
        {
            if (_intentionalDisconnect) return;
            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                Debug.LogWarning($"[ClaudeWS] 최대 재연결 시도 횟수 도달 ({_maxReconnectAttempts}회)");
                OnError?.Invoke("서버에 연결할 수 없습니다. 서버 실행 상태를 확인해주세요.");
                return;
            }

            _reconnectAttempts++;
            Debug.Log($"[ClaudeWS] 재연결 시도 {_reconnectAttempts}/{_maxReconnectAttempts} ({_reconnectInterval}초 후)");

            await Cysharp.Threading.Tasks.UniTask.Delay(
                (int)(_reconnectInterval * 1000),
                cancellationToken: _cts.Token
            );

            if (!_intentionalDisconnect && !_isConnected)
                await CreateAndConnect();
        }
    }
}
