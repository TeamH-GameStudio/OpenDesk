using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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

        /// <summary>활성 provider 변경 알림 (provider, available, info)</summary>
        public event Action<string, bool, string> OnProviderChanged;

        /// <summary>OAuth 로그인 진행 이벤트 (state, message, url, code)</summary>
        public event Action<Models.AuthEventMessage> OnAuthEvent;

        /// <summary>
        /// 캐릭터 입모양/타이핑용 토큰 청크 (sessionId, agentId, text).
        /// 채팅 UI 누적용 <see cref="OnDelta"/> 와 별개 채널 — Multi-agent runner (PROTOCOL.md) 에서 송출.
        /// </summary>
        public event Action<string, string, string> OnTextDelta;

        /// <summary>발화 시작 (sessionId, agentId). AgentTalkingController 가 구독해 입모양 + 텍스트 버퍼 시작.</summary>
        public event Action<string, string> OnTalkingStart;

        /// <summary>발화 종료 (sessionId, agentId, reason). reason: "complete" | "error" | "interrupted"</summary>
        public event Action<string, string, string> OnTalkingStop;

        /// <summary>인터랙티브 ask_user 도구가 사용자 응답을 요청. ChatPanelView 가 인라인 카드 렌더.</summary>
        public event Action<ToolUserAskMessage> OnToolUserAsk;

        /// <summary>서브에이전트 spawn 이벤트.</summary>
        public event Action<SubAgentSpawnedMessage> OnSubAgentSpawned;

        /// <summary>서브에이전트 완료 이벤트.</summary>
        public event Action<SubAgentCompletedMessage> OnSubAgentCompleted;

        /// <summary>서브에이전트 실패 이벤트.</summary>
        public event Action<SubAgentFailedMessage> OnSubAgentFailed;

        /// <summary>백그라운드 작업 상태 변화.</summary>
        public event Action<TaskStateMessage> OnTaskState;

        /// <summary>예약 작업 발화/상태 변화.</summary>
        public event Action<CronStateMessage> OnCronState;

        /// <summary>
        /// 미들웨어 hook chain 의 telemetry 이벤트.
        /// event: "first_token" | "request_complete" | "error" | "retry".
        /// IAgentTelemetryService 가 구독하여 reactive prop 으로 노출.
        /// </summary>
        public event Action<TelemetryEvent> OnTelemetry;

        // ── 생명주기 ──────────────────────────────────────────

        private void Start()
        {
            _cts = new CancellationTokenSource();
            ConnectAsync().Forget();
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

        public async UniTask ConnectAsync(CancellationToken ct = default)
        {
            _intentionalDisconnect = false;
            _reconnectAttempts = 0;

            await CreateAndConnect(ct);
        }

        public async UniTask DisconnectAsync()
        {
            _intentionalDisconnect = true;
            if (_socket != null && _socket.State == WebSocketState.Open)
                await _socket.Close();
        }

        private async UniTask CreateAndConnect(CancellationToken ct = default)
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
                ct.ThrowIfCancellationRequested();
                // NativeWebSocket.Connect()는 연결 종료까지 반환하지 않으므로 fire-and-forget
                _socket.Connect().ContinueWith(
                    _ => { },
                    System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously
                );
                await UniTask.Yield();
            }
            catch (OperationCanceledException) { /* 정상 취소 */ }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] 연결 실패: {ex.Message}");
            }
        }

        // ── 전송 메서드 ───────────────────────────────────────

        public void SendChat(string message) => SendChatAsync(message).Forget();

        public async UniTask SendChatAsync(string message)
        {
            if (!_isConnected || _socket == null)
            {
                OnError?.Invoke("서버에 연결되지 않았습니다");
                return;
            }

            try
            {
                var req = new ChatRequest { message = message };
                var json = JsonUtility.ToJson(req);
                Debug.Log($"[ClaudeWS] 전송: {message.Substring(0, Math.Min(message.Length, 50))}...");
                await _socket.SendText(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendChat 실패: {ex.Message}");
                OnError?.Invoke($"메시지 전송 실패: {ex.Message}");
            }
        }

        public void SendClear() => SendClearAsync().Forget();

        public async UniTask SendClearAsync()
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var json = JsonUtility.ToJson(new ClearRequest());
                await _socket.SendText(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendClear 실패: {ex.Message}");
            }
        }

        public void SendConfig(string systemPrompt) => SendConfigAsync(systemPrompt, null).Forget();
        public void SendConfig(string systemPrompt, string model) => SendConfigAsync(systemPrompt, model).Forget();

        public async UniTask SendConfigAsync(string systemPrompt, string model = null)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new ConfigRequest { systemPrompt = systemPrompt, model = model ?? string.Empty };
                await _socket.SendText(JsonUtility.ToJson(req));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendConfig 실패: {ex.Message}");
            }
        }

        public void SendSetProvider(string provider) => SendSetProviderAsync(provider).Forget();

        public async UniTask SendSetProviderAsync(string provider)
        {
            if (!_isConnected || _socket == null || string.IsNullOrEmpty(provider)) return;
            try
            {
                var req = new SetProviderRequest { provider = provider };
                await _socket.SendText(JsonUtility.ToJson(req));
                Debug.Log($"[ClaudeWS] set_provider 전송: {provider}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendSetProvider 실패: {ex.Message}");
            }
        }

        /// <summary>장착 플러그인 기반 MCP config 를 미들웨어에 전달. 빈 페이로드면 비활성.</summary>
        public void SendSetMcpConfig(OpenDesk.Core.Models.Plugins.McpConfigPayload payload)
            => SendSetMcpConfigAsync(payload).Forget();

        public async UniTask SendSetMcpConfigAsync(OpenDesk.Core.Models.Plugins.McpConfigPayload payload)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new SetMcpConfigRequest { payload = payload };
                await _socket.SendText(JsonUtility.ToJson(req));
                var serverCount = payload?.servers != null ? payload.servers.Count : 0;
                Debug.Log($"[ClaudeWS] set_mcp_config 전송 ({serverCount} servers)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendSetMcpConfig 실패: {ex.Message}");
            }
        }

        /// <summary>장착 스킬 인덱스(이름+설명)를 미들웨어에 전달. 본문은 read_skill_body 도구로 지연 로드.</summary>
        public void SendSetSkillLoadout(OpenDesk.Core.Models.Skills.SkillLoadoutPayload payload)
            => SendSetSkillLoadoutAsync(payload).Forget();

        public async UniTask SendSetSkillLoadoutAsync(OpenDesk.Core.Models.Skills.SkillLoadoutPayload payload)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new SetSkillLoadoutRequest { payload = payload };
                await _socket.SendText(JsonUtility.ToJson(req));
                var count = payload?.skills != null ? payload.skills.Count : 0;
                Debug.Log($"[ClaudeWS] set_skill_loadout 전송 ({count} skills)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendSetSkillLoadout 실패: {ex.Message}");
            }
        }

        /// <summary>ask_user / route_capability 도구에 대한 사용자 응답을 미들웨어로 송신.</summary>
        public void SendToolUserResponse(string toolUseId, string response, string[] selected)
            => SendToolUserResponseAsync(toolUseId, response, selected, remember: false).Forget();

        /// <summary>capability_pick 카드용 — "다음부터 자동" 선호 저장 여부 포함.</summary>
        public void SendToolUserResponse(string toolUseId, string response, string[] selected, bool remember)
            => SendToolUserResponseAsync(toolUseId, response, selected, remember).Forget();

        public async UniTask SendToolUserResponseAsync(string toolUseId, string response, string[] selected, bool remember = false)
        {
            if (!_isConnected || _socket == null) return;
            if (string.IsNullOrEmpty(toolUseId)) return;
            try
            {
                var req = new ToolUserResponseRequest
                {
                    tool_use_id = toolUseId,
                    response = response ?? string.Empty,
                    selected = selected ?? Array.Empty<string>(),
                    remember = remember,
                };
                await _socket.SendText(JsonUtility.ToJson(req));
                Debug.Log($"[ClaudeWS] tool_user_response 전송 ({toolUseId}, remember={remember})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendToolUserResponse 실패: {ex.Message}");
            }
        }

        /// <summary>설치된 플러그인 목록을 미들웨어로 push — route_capability 가 조회.</summary>
        public void SendPluginRegistry(string agentId, PluginRegistryEntry[] entries)
            => SendPluginRegistryAsync(agentId, entries).Forget();

        public async UniTask SendPluginRegistryAsync(string agentId, PluginRegistryEntry[] entries)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new SetPluginRegistryRequest
                {
                    agent_id = agentId ?? string.Empty,
                    payload = entries ?? Array.Empty<PluginRegistryEntry>(),
                };
                await _socket.SendText(JsonUtility.ToJson(req));
                Debug.Log($"[ClaudeWS] set_plugin_registry 전송 ({(entries?.Length ?? 0)} plugin)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendPluginRegistry 실패: {ex.Message}");
            }
        }

        /// <summary>백그라운드 작업 제어. action: "stop" | "update".</summary>
        public void SendTaskControl(string action, string taskId)
            => SendTaskControlAsync(action, taskId).Forget();

        public async UniTask SendTaskControlAsync(string action, string taskId)
        {
            if (!_isConnected || _socket == null) return;
            if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(action)) return;
            try
            {
                var req = new TaskControlRequest { action = action, task_id = taskId };
                await _socket.SendText(JsonUtility.ToJson(req));
                Debug.Log($"[ClaudeWS] task_control 전송 ({action}:{taskId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendTaskControl 실패: {ex.Message}");
            }
        }

        public void SendPing() => SendPingAsync().Forget();

        public async UniTask SendPingAsync()
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                await _socket.SendText(JsonUtility.ToJson(new PingRequest()));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeWS] SendPing 실패: {ex.Message}");
            }
        }

        /// <summary>진행 중 CLI 응답 중단 요청. 미들웨어가 active subprocess 를 종료한다.</summary>
        public void SendCancel() => SendCancelAsync().Forget();

        public async UniTask SendCancelAsync()
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                await _socket.SendText(JsonUtility.ToJson(new Models.CancelRequest()));
                Debug.Log("[ClaudeWS] cancel 전송");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeWS] SendCancel 실패: {ex.Message}");
            }
        }

        /// <summary>OAuth 로그인 시작. 미들웨어가 격리된 CLAUDE_CONFIG_DIR 하에서 `claude login` 을 실행한다.</summary>
        public void SendAuthStart() => SendAuthStartAsync().Forget();

        public async UniTask SendAuthStartAsync()
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                await _socket.SendText(JsonUtility.ToJson(new AuthStartRequest()));
                Debug.Log("[ClaudeWS] auth_start 전송");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendAuthStart 실패: {ex.Message}");
            }
        }

        public void SendAuthCancel() => SendAuthCancelAsync().Forget();

        public async UniTask SendAuthCancelAsync()
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                await _socket.SendText(JsonUtility.ToJson(new AuthCancelRequest()));
                Debug.Log("[ClaudeWS] auth_cancel 전송");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendAuthCancel 실패: {ex.Message}");
            }
        }

        // ── 라이선스 / 크레딧 (hybrid routing) ─────────────────

        /// <summary>라이선스 활성화 요청. 미들웨어가 routing_client.activate 로 프록시.</summary>
        public void SendLicenseActivate(string licenseKey, string fingerprint, string deviceName)
            => SendLicenseActivateAsync(licenseKey, fingerprint, deviceName).Forget();

        public async UniTask SendLicenseActivateAsync(string licenseKey, string fingerprint, string deviceName)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new LicenseActivateRequest
                {
                    licenseKey = licenseKey ?? string.Empty,
                    fingerprint = fingerprint ?? string.Empty,
                    deviceName = deviceName ?? string.Empty,
                };
                await _socket.SendText(JsonUtility.ToJson(req));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendLicenseActivate 실패: {ex.Message}");
            }
        }

        /// <summary>complexity hint 만 갱신 (config 메시지의 light variant).</summary>
        public void SendComplexityHint(string hint) => SendComplexityHintAsync(hint).Forget();

        public async UniTask SendComplexityHintAsync(string hint)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new SetComplexityHintRequest { complexityHint = hint ?? "auto" };
                await _socket.SendText(JsonUtility.ToJson(req));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendComplexityHint 실패: {ex.Message}");
            }
        }

        /// <summary>라이선스 JWT 를 미들웨어 라우팅 클라이언트에 바인딩.</summary>
        public void SendAuthToken(string jwt) => SendAuthTokenAsync(jwt).Forget();

        public async UniTask SendAuthTokenAsync(string jwt)
        {
            if (!_isConnected || _socket == null) return;
            try
            {
                var req = new SetAuthRequest { jwt = jwt ?? string.Empty };
                await _socket.SendText(JsonUtility.ToJson(req));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendAuthToken 실패: {ex.Message}");
            }
        }

        // ── 라이선스 / 크레딧 이벤트 ─────────────────────────

        /// <summary>라이선스 활성화 성공.</summary>
        public event Action<LicenseActivatedMessage> OnLicenseActivated;

        /// <summary>라이선스 활성화/바인딩 실패.</summary>
        public event Action<LicenseErrorMessage> OnLicenseError;

        /// <summary>JWT 바인딩 상태 푸시.</summary>
        public event Action<AuthStatusMessage> OnAuthStatus;

        /// <summary>라우터 결정 (모델/tier/예상 크레딧).</summary>
        public event Action<CreditRoutingMessage> OnCreditRouting;

        /// <summary>잔액 + held 푸시. hold/settle/refund 모든 사이클에서 발생.</summary>
        public event Action<CreditBalanceMessage> OnCreditBalance;

        /// <summary>settle 완료 (실 토큰/실 크레딧).</summary>
        public event Action<CreditSettledMessage> OnCreditSettled;

        /// <summary>잔액 부족.</summary>
        public event Action<CreditInsufficientMessage> OnCreditInsufficient;

        /// <summary>대화 히스토리 JSON을 전송하여 세션 이어나기</summary>
        public void SendResume(string conversationJson) => SendResumeAsync(conversationJson).Forget();

        public async UniTask SendResumeAsync(string conversationJson)
        {
            if (!_isConnected || _socket == null)
            {
                OnError?.Invoke("서버에 연결되지 않았습니다");
                return;
            }

            try
            {
                var req = new ResumeRequest { conversation = conversationJson };
                var json = JsonUtility.ToJson(req);
                Debug.Log($"[ClaudeWS] resume 전송: {conversationJson.Length}자");
                await _socket.SendText(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] SendResume 실패: {ex.Message}");
                OnError?.Invoke($"세션 복원 실패: {ex.Message}");
            }
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

                case "mcp_config_updated":
                    Debug.Log("[ClaudeWS] MCP 설정 업데이트 완료");
                    break;

                case "provider_changed":
                    var pcMsg = JsonUtility.FromJson<ProviderChangedMessage>(json);
                    if (pcMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] provider 변경: {pcMsg.provider} (available={pcMsg.available}, info={pcMsg.info})");
                        OnProviderChanged?.Invoke(pcMsg.provider, pcMsg.available, pcMsg.info);
                    }
                    break;

                case "auth_event":
                    var authMsg = JsonUtility.FromJson<AuthEventMessage>(json);
                    if (authMsg != null)
                    {
                        Debug.Log($"[ClaudeWS] auth_event [{authMsg.state}]: {authMsg.message}");
                        OnAuthEvent?.Invoke(authMsg);
                    }
                    break;

                case "text_delta":
                    // Multi-agent runner 의 lightweight 토큰 청크 (입모양/타이핑 효과용).
                    // 기존 "delta" 와 채널 분리 — 채팅 UI 가 OnDelta 를 그대로 쓰므로 깨지지 않는다.
                    var txtDelta = JsonUtility.FromJson<TextDeltaMessage>(json);
                    if (txtDelta != null && !string.IsNullOrEmpty(txtDelta.text))
                        OnTextDelta?.Invoke(txtDelta.session_id ?? "", txtDelta.agent_id ?? "", txtDelta.text);
                    break;

                case "talking_start":
                    var talkStart = JsonUtility.FromJson<TalkingStartMessage>(json);
                    if (talkStart != null)
                        OnTalkingStart?.Invoke(talkStart.session_id ?? "", talkStart.agent_id ?? "");
                    break;

                case "talking_stop":
                    var talkStop = JsonUtility.FromJson<TalkingStopMessage>(json);
                    if (talkStop != null)
                        OnTalkingStop?.Invoke(
                            talkStop.session_id ?? "",
                            talkStop.agent_id ?? "",
                            talkStop.reason ?? "complete");
                    break;

                case "tool_user_ask":
                    var askMsg = JsonUtility.FromJson<ToolUserAskMessage>(json);
                    if (askMsg != null)
                        OnToolUserAsk?.Invoke(askMsg);
                    break;

                case "sub_agent_spawned":
                    var subSpawned = JsonUtility.FromJson<SubAgentSpawnedMessage>(json);
                    if (subSpawned != null)
                        OnSubAgentSpawned?.Invoke(subSpawned);
                    break;

                case "sub_agent_completed":
                    var subDone = JsonUtility.FromJson<SubAgentCompletedMessage>(json);
                    if (subDone != null)
                        OnSubAgentCompleted?.Invoke(subDone);
                    break;

                case "sub_agent_failed":
                    var subFail = JsonUtility.FromJson<SubAgentFailedMessage>(json);
                    if (subFail != null)
                        OnSubAgentFailed?.Invoke(subFail);
                    break;

                case "task_state":
                    var taskMsg = JsonUtility.FromJson<TaskStateMessage>(json);
                    if (taskMsg != null)
                        OnTaskState?.Invoke(taskMsg);
                    break;

                case "cron_state":
                    var cronMsg = JsonUtility.FromJson<CronStateMessage>(json);
                    if (cronMsg != null)
                        OnCronState?.Invoke(cronMsg);
                    break;

                case "telemetry":
                    var telemetryMsg = JsonUtility.FromJson<TelemetryEvent>(json);
                    if (telemetryMsg != null)
                        OnTelemetry?.Invoke(telemetryMsg);
                    break;

                case "license.activated":
                    var licOk = JsonUtility.FromJson<LicenseActivatedMessage>(json);
                    if (licOk != null)
                    {
                        Debug.Log($"[Credit] license.activated user={licOk.userId} plan={licOk.planTier} balance={licOk.balance}");
                        OnLicenseActivated?.Invoke(licOk);
                    }
                    break;

                case "license.error":
                    var licErr = JsonUtility.FromJson<LicenseErrorMessage>(json);
                    if (licErr != null)
                    {
                        Debug.LogWarning($"[Credit] license.error code={licErr.code} message={licErr.message}");
                        OnLicenseError?.Invoke(licErr);
                    }
                    break;

                case "auth_status":
                    var authSt = JsonUtility.FromJson<AuthStatusMessage>(json);
                    if (authSt != null)
                    {
                        Debug.Log($"[Credit] auth_status authenticated={authSt.authenticated}");
                        OnAuthStatus?.Invoke(authSt);
                    }
                    break;

                case "credit.routing":
                    var crRoute = JsonUtility.FromJson<CreditRoutingMessage>(json);
                    if (crRoute != null)
                    {
                        Debug.Log($"[Credit] routing task={crRoute.taskId} model={crRoute.model} tier={crRoute.tier} estimated={crRoute.estimatedCredits} (reason: {crRoute.reasoning})");
                        OnCreditRouting?.Invoke(crRoute);
                    }
                    break;

                case "credit.balance":
                    var crBal = JsonUtility.FromJson<CreditBalanceMessage>(json);
                    if (crBal != null)
                    {
                        Debug.Log($"[Credit] balance={crBal.balance} held={crBal.held}");
                        OnCreditBalance?.Invoke(crBal);
                    }
                    break;

                case "credit.settled":
                    var crSet = JsonUtility.FromJson<CreditSettledMessage>(json);
                    if (crSet != null)
                    {
                        Debug.Log($"[Credit] settled task={crSet.taskId} actual={crSet.actualCredits} (in={crSet.inputTokens}, out={crSet.outputTokens}) balance={crSet.balance}");
                        OnCreditSettled?.Invoke(crSet);
                    }
                    break;

                case "credit.insufficient":
                    var crIns = JsonUtility.FromJson<CreditInsufficientMessage>(json);
                    if (crIns != null)
                    {
                        Debug.LogWarning($"[Credit] insufficient required={crIns.required} balance={crIns.balance} code={crIns.code}");
                        OnCreditInsufficient?.Invoke(crIns);
                    }
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
                TryReconnectAsync().Forget();
        }

        // ── 자동 재연결 ──────────────────────────────────────

        private async UniTask TryReconnectAsync()
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

            try
            {
                await UniTask.Delay(
                    (int)(_reconnectInterval * 1000),
                    cancellationToken: _cts.Token
                );

                if (!_intentionalDisconnect && !_isConnected)
                    await CreateAndConnect(_cts.Token);
            }
            catch (OperationCanceledException) { /* 정상 취소 */ }
            catch (Exception ex)
            {
                Debug.LogError($"[ClaudeWS] 재연결 실패: {ex.Message}");
            }
        }
    }
}
