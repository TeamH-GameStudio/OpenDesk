using System;
using System.Threading;
using NativeWebSocket;
using OpenDesk.Claude.Models;
using UnityEngine;

namespace OpenDesk.Claude
{
    /// <summary>
    /// Python 미들웨어 서버와 WebSocket 통신 전담.
    /// 멀티 에이전트 프로토콜 — 모든 메시지에 agent_id 포함.
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
        private CancellationTokenSource _cts;

        // ── 공개 프로퍼티 ──────────────────────────────────────

        public bool IsConnected => _isConnected;

        // ── 이벤트 (멀티 에이전트 프로토콜) ──────────────────────

        /// <summary>에이전트 상태 변화 (agentId, state, tool)</summary>
        public event Action<string, string, string> OnAgentState;

        /// <summary>AI 추론 과정 스트리밍 (agentId, thinking)</summary>
        public event Action<string, string> OnAgentThinking;

        /// <summary>응답 텍스트 delta (agentId, text)</summary>
        public event Action<string, string> OnAgentDelta;

        /// <summary>최종 완성 응답 (agentId, message)</summary>
        public event Action<string, string> OnAgentMessage;

        /// <summary>캐릭터 액션 (agentId, action)</summary>
        public event Action<string, string> OnAgentAction;

        /// <summary>에러 (agentId, error, message)</summary>
        public event Action<string, string, string> OnAgentError;

        /// <summary>세션 목록 (agentId, currentSessionId, sessions)</summary>
        public event Action<string, string, SessionInfo[]> OnSessionList;

        /// <summary>세션 전환 완료 (agentId, sessionId, chatHistory)</summary>
        public event Action<string, string, ChatHistoryEntry[]> OnSessionSwitched;

        /// <summary>WebSocket 연결 상태 변경 (connected)</summary>
        public event Action<bool> OnConnectionChanged;

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

        // ══════════════════════════════════════════════════════
        //  전송 메서드 (Unity -> Middleware)
        // ══════════════════════════════════════════════════════

        /// <summary>채팅 메시지 전송</summary>
        public async void SendChatMessage(string agentId, string message)
        {
            if (!_isConnected || _socket == null)
            {
                OnAgentError?.Invoke(agentId, "not_connected", "서버에 연결되지 않았습니다");
                return;
            }

            var req = new ChatMessageRequest { agent_id = agentId, message = message };
            var json = JsonUtility.ToJson(req);
            Debug.Log($"[ClaudeWS] chat -> {agentId}: {message[..Math.Min(message.Length, 50)]}...");
            await _socket.SendText(json);
        }

        /// <summary>대화 초기화 (새 세션 생성)</summary>
        public async void SendChatClear(string agentId)
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(new ChatClearRequest { agent_id = agentId }));
        }

        /// <summary>전체 에이전트 상태 조회</summary>
        public async void SendStatusRequest()
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(new StatusRequest()));
        }

        /// <summary>세션 목록 조회</summary>
        public async void SendSessionList(string agentId)
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionListRequest { agent_id = agentId }));
        }

        /// <summary>새 세션 생성</summary>
        public async void SendSessionNew(string agentId)
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(new SessionNewRequest { agent_id = agentId }));
        }

        /// <summary>세션 전환</summary>
        public async void SendSessionSwitch(string agentId, string sessionId)
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(
                new SessionSwitchRequest { agent_id = agentId, session_id = sessionId }));
        }

        /// <summary>세션 삭제</summary>
        public async void SendSessionDelete(string agentId, string sessionId)
        {
            if (!_isConnected || _socket == null) return;
            await _socket.SendText(JsonUtility.ToJson(
                new SessionDeleteRequest { agent_id = agentId, session_id = sessionId }));
        }

        // ══════════════════════════════════════════════════════
        //  WebSocket 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void HandleOpen()
        {
            Debug.Log("[ClaudeWS] WebSocket 연결됨");
            _isConnected = true;
            _reconnectAttempts = 0;
            OnConnectionChanged?.Invoke(true);

            // 연결 직후 전체 에이전트 상태 조회
            SendStatusRequest();
        }

        private void HandleMessage(byte[] data)
        {
            var json = System.Text.Encoding.UTF8.GetString(data);

            var baseMsg = JsonUtility.FromJson<ServerMessage>(json);
            if (baseMsg == null || string.IsNullOrEmpty(baseMsg.type))
            {
                Debug.LogWarning($"[ClaudeWS] 알 수 없는 메시지: {json[..Math.Min(json.Length, 100)]}");
                return;
            }

            switch (baseMsg.type)
            {
                case "agent_state":
                {
                    var msg = JsonUtility.FromJson<AgentStateMessage>(json);
                    if (msg == null) break;

                    if (msg.state == "error")
                    {
                        Debug.LogWarning($"[ClaudeWS] 에이전트 에러 [{msg.agent_id}]: {msg.error} - {msg.message}");
                        OnAgentError?.Invoke(msg.agent_id, msg.error ?? "", msg.message ?? "");
                    }
                    else
                    {
                        OnAgentState?.Invoke(msg.agent_id, msg.state ?? "", msg.tool ?? "");
                    }
                    break;
                }

                case "agent_thinking":
                {
                    var msg = JsonUtility.FromJson<AgentThinkingMessage>(json);
                    if (msg != null && !string.IsNullOrEmpty(msg.thinking))
                        OnAgentThinking?.Invoke(msg.agent_id, msg.thinking);
                    break;
                }

                case "agent_delta":
                {
                    var msg = JsonUtility.FromJson<AgentDeltaMessage>(json);
                    if (msg != null && !string.IsNullOrEmpty(msg.text))
                        OnAgentDelta?.Invoke(msg.agent_id, msg.text);
                    break;
                }

                case "agent_message":
                {
                    var msg = JsonUtility.FromJson<AgentMessageResponse>(json);
                    if (msg != null)
                        OnAgentMessage?.Invoke(msg.agent_id, msg.message ?? "");
                    break;
                }

                case "agent_action":
                {
                    var msg = JsonUtility.FromJson<AgentActionMessage>(json);
                    if (msg != null && !string.IsNullOrEmpty(msg.action))
                        OnAgentAction?.Invoke(msg.agent_id, msg.action);
                    break;
                }

                case "session_list_response":
                {
                    var msg = JsonUtility.FromJson<SessionListResponse>(json);
                    if (msg != null)
                        OnSessionList?.Invoke(msg.agent_id, msg.current_session_id ?? "", msg.sessions ?? Array.Empty<SessionInfo>());
                    break;
                }

                case "session_switched":
                {
                    var msg = JsonUtility.FromJson<SessionSwitchedMessage>(json);
                    if (msg != null)
                        OnSessionSwitched?.Invoke(msg.agent_id, msg.session_id ?? "", msg.chat_history ?? Array.Empty<ChatHistoryEntry>());
                    break;
                }

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
                OnAgentError?.Invoke("", "reconnect_failed", "서버에 연결할 수 없습니다. 서버 실행 상태를 확인해주세요.");
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
