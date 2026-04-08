using System;
using System.Threading;
using NativeWebSocket;
using OpenDesk.Claude.Models;
using UnityEngine;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Python 미들웨어 서버와 WebSocket 통신 전담.
    /// 새 프로토콜: 멀티 에이전트 + 세션 관리 + thinking + 2단계 스트리밍.
    /// </summary>
    public class ClaudeWebSocketClient : MonoBehaviour
    {
        [Header("서버 설정")]
        [SerializeField] private string _serverUrl = "ws://localhost:8765";
        [SerializeField] private float  _reconnectInterval = 3f;
        [SerializeField] private int    _maxReconnectAttempts = 5;
        [SerializeField] private bool   _autoConnect = true;

        private WebSocket _socket;
        private bool      _isConnected;
        private bool      _intentionalDisconnect;
        private int       _reconnectAttempts;
        private CancellationTokenSource _cts;

        // ── 공개 프로퍼티 ──────────────────────────────────────

        public bool IsConnected => _isConnected;

        // ── 이벤트 (새 프로토콜 6종) ─────────────────────────────

        /// <summary>에이전트 상태 변화 (idle/thinking/working/complete/error)</summary>
        public event Action<AgentStateMessage> OnAgentState;

        /// <summary>실시간 텍스트 청크 (raw 마크다운, TMP 미적용)</summary>
        public event Action<AgentDeltaMessage> OnAgentDelta;

        /// <summary>최종 완성 응답 (TMP 포매팅 적용)</summary>
        public event Action<AgentMessageMessage> OnAgentMessage;

        /// <summary>에이전트 추론 과정</summary>
        public event Action<AgentThinkingMessage> OnAgentThinking;

        /// <summary>세션 목록 응답</summary>
        public event Action<SessionListResponse> OnSessionListResponse;

        /// <summary>세션 전환 완료 + 대화 기록</summary>
        public event Action<SessionSwitchedMessage> OnSessionSwitched;

        /// <summary>WebSocket 연결 상태 변경</summary>
        public event Action<bool> OnConnectionChanged;

        // ── 생명주기 ──────────────────────────────────────────

        private async void Start()
        {
            _cts = new CancellationTokenSource();
            if (_autoConnect)
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
                _socket.OnError   -= HandleSocketError;
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
            if (_socket != null)
            {
                _socket.OnOpen    -= HandleOpen;
                _socket.OnMessage -= HandleMessage;
                _socket.OnError   -= HandleSocketError;
                _socket.OnClose   -= HandleClose;
                try { _ = _socket.Close(); } catch { }
            }

            _socket = new WebSocket(_serverUrl);
            _socket.OnOpen    += HandleOpen;
            _socket.OnMessage += HandleMessage;
            _socket.OnError   += HandleSocketError;
            _socket.OnClose   += HandleClose;

            Debug.Log($"[ClaudeWS] 연결 시도: {_serverUrl}");

            try
            {
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

        // ── 송신 메서드 (7종) ─────────────────────────────────

        public async void SendChatMessage(string agentId, string message)
        {
            if (!EnsureConnected()) return;
            var req = new ChatMessageRequest { agent_id = agentId, message = message };
            Debug.Log($"[ClaudeWS] chat_message -> {agentId}: {message.Substring(0, Math.Min(message.Length, 50))}");
            await _socket.SendText(JsonUtility.ToJson(req));
        }

        public async void SendChatClear(string agentId)
        {
            if (!EnsureConnected()) return;
            await _socket.SendText(JsonUtility.ToJson(new ChatClearRequest { agent_id = agentId }));
        }

        public async void SendSessionList(string agentId)
        {
            if (!EnsureConnected()) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionListRequest { agent_id = agentId }));
        }

        public async void SendSessionSwitch(string agentId, string sessionId)
        {
            if (!EnsureConnected()) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionSwitchRequest { agent_id = agentId, session_id = sessionId }));
        }

        public async void SendSessionNew(string agentId)
        {
            if (!EnsureConnected()) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionNewRequest { agent_id = agentId }));
        }

        public async void SendSessionDelete(string agentId, string sessionId)
        {
            if (!EnsureConnected()) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionDeleteRequest { agent_id = agentId, session_id = sessionId }));
        }

        public async void SendStatusRequest()
        {
            if (!EnsureConnected()) return;
            Debug.Log("[ClaudeWS] status_request 전송");
            await _socket.SendText(JsonUtility.ToJson(new StatusRequest()));
        }

        private bool EnsureConnected()
        {
            if (_isConnected && _socket != null) return true;
            Debug.LogWarning("[ClaudeWS] 서버에 연결되지 않았습니다");
            return false;
        }

        // ── WebSocket 이벤트 핸들러 ───────────────────────────

        private void HandleOpen()
        {
            Debug.Log("[ClaudeWS] WebSocket 연결됨");
            _isConnected = true;
            _reconnectAttempts = 0;
            OnConnectionChanged?.Invoke(true);

            // 연결 즉시 전체 상태 요청
            SendStatusRequest();
        }

        private void HandleMessage(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);

            var baseMsg = JsonUtility.FromJson<ServerMessage>(json);
            if (baseMsg == null || string.IsNullOrEmpty(baseMsg.type))
            {
                Debug.LogWarning($"[ClaudeWS] 알 수 없는 메시지: {json.Substring(0, Math.Min(json.Length, 100))}");
                return;
            }

            switch (baseMsg.type)
            {
                case "agent_state":
                    var stateMsg = JsonUtility.FromJson<AgentStateMessage>(json);
                    if (stateMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] agent_state: {stateMsg.agent_id} -> {stateMsg.state} {stateMsg.tool}");
                        OnAgentState?.Invoke(stateMsg);
                    }
                    break;

                case "agent_delta":
                    var deltaMsg = JsonUtility.FromJson<AgentDeltaMessage>(json);
                    if (deltaMsg != null)
                        OnAgentDelta?.Invoke(deltaMsg);
                    break;

                case "agent_message":
                    var msgMsg = JsonUtility.FromJson<AgentMessageMessage>(json);
                    if (msgMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] agent_message: {msgMsg.agent_id} ({msgMsg.message?.Length ?? 0}자)");
                        OnAgentMessage?.Invoke(msgMsg);
                    }
                    break;

                case "agent_thinking":
                    var thinkMsg = JsonUtility.FromJson<AgentThinkingMessage>(json);
                    if (thinkMsg != null)
                        OnAgentThinking?.Invoke(thinkMsg);
                    break;

                case "session_list_response":
                    var listMsg = JsonUtility.FromJson<SessionListResponse>(json);
                    if (listMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] session_list: {listMsg.agent_id} ({listMsg.sessions?.Length ?? 0}개)");
                        OnSessionListResponse?.Invoke(listMsg);
                    }
                    break;

                case "session_switched":
                    var switchMsg = JsonUtility.FromJson<SessionSwitchedMessage>(json);
                    if (switchMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] session_switched: {switchMsg.agent_id} -> {switchMsg.session_id}");
                        OnSessionSwitched?.Invoke(switchMsg);
                    }
                    break;

                default:
                    Debug.Log($"[ClaudeWS] 미처리 메시지 타입: {baseMsg.type}");
                    break;
            }
        }

        private void HandleSocketError(string errorMsg)
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
                OnConnectionChanged?.Invoke(false);
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
