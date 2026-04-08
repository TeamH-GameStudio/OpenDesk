using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Models;
using OpenDesk.Presentation.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Session
{
    /// <summary>
    /// 채팅 패널 -- 멀티 에이전트 미들웨어 프로토콜 대응.
    /// agent_id 기반 라우팅으로 현재 활성 에이전트만 처리.
    ///
    /// 흐름:
    ///   메시지 전송 -> agent_state(thinking) -> ForceState(Thinking)
    ///   -> agent_delta -> ForceState(ChatDelta)
    ///   -> agent_message -> ForceState(ChatFinal) -> Idle
    ///   -> agent_action -> 감정 모션 오버라이드
    /// </summary>
    public class ChatPanelController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Button _backButton;

        [Header("Header")]
        [SerializeField] private TMP_Text _headerTitle;
        [SerializeField] private TMP_Text _headerSubtitle;

        [Header("Messages")]
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _messageContent;
        [SerializeField] private GameObject _userBubblePrefab;
        [SerializeField] private GameObject _agentBubblePrefab;
        [SerializeField] private GameObject _systemBubblePrefab;

        [Header("Input")]
        [SerializeField] private TMP_InputField _inputField;
        [SerializeField] private Button _sendButton;

        [Header("Empty")]
        [SerializeField] private GameObject _emptyHint;

        [Header("Claude")]
        [SerializeField] private ClaudeWebSocketClient _claudeClient;

        // ── 상태 ────────────────────────────────────────────
        private string _currentAgentId;
        private string _currentAgentName;
        private AgentRole _currentRole;
        private readonly List<GameObject> _spawnedBubbles = new();
        private bool _isSending;
        private GameObject _streamingBubble;
        private TMP_Text _streamingText;
        private string _streamingContent = "";

        // ── 에이전트 연결 ───────────────────────────────────
        private AgentHUDController _linkedHUD;
        private AgentCharacterController _linkedCharCtrl;

        private static readonly Dictionary<AgentRole, string> RoleNames = new()
        {
            { AgentRole.Planning,    "기획" },
            { AgentRole.Development, "개발" },
            { AgentRole.Design,      "디자인" },
            { AgentRole.Legal,       "법률" },
            { AgentRole.Marketing,   "마케팅" },
            { AgentRole.Research,    "리서치" },
            { AgentRole.Support,     "고객지원" },
            { AgentRole.Finance,     "재무" },
        };

        // ================================================================
        //  초기화
        // ================================================================

        private void Start()
        {
            _backButton?.onClick.AddListener(Close);
            _sendButton?.onClick.AddListener(OnSendClicked);

            if (_inputField != null)
                _inputField.onSubmit.AddListener(_ => OnSendClicked());

            if (_panelRoot != null) _panelRoot.SetActive(false);

            if (_claudeClient != null)
            {
                _claudeClient.OnAgentDelta    += HandleDelta;
                _claudeClient.OnAgentMessage  += HandleMessage;
                _claudeClient.OnAgentError    += HandleError;
                _claudeClient.OnAgentState    += HandleState;
                _claudeClient.OnAgentThinking += HandleThinking;
                _claudeClient.OnAgentAction   += HandleAction;
                _claudeClient.OnSessionSwitched += HandleSessionSwitched;
            }
        }

        private void OnDestroy()
        {
            if (_claudeClient != null)
            {
                _claudeClient.OnAgentDelta    -= HandleDelta;
                _claudeClient.OnAgentMessage  -= HandleMessage;
                _claudeClient.OnAgentError    -= HandleError;
                _claudeClient.OnAgentState    -= HandleState;
                _claudeClient.OnAgentThinking -= HandleThinking;
                _claudeClient.OnAgentAction   -= HandleAction;
                _claudeClient.OnSessionSwitched -= HandleSessionSwitched;
            }
        }

        // ================================================================
        //  외부 API
        // ================================================================

        /// <summary>채팅 패널 열기 -- agentId는 미들웨어 에이전트 ID (researcher/writer/analyst)</summary>
        public void Open(string agentId, string agentName, AgentRole role)
        {
            _currentAgentId = agentId;
            _currentAgentName = agentName;
            _currentRole = role;

            if (_headerTitle != null)
                _headerTitle.text = agentName;
            if (_headerSubtitle != null)
                _headerSubtitle.text = RoleNames.GetValueOrDefault(role, "에이전트") + " · 대화 중";

            FindLinkedAgent();
            ClearBubbles();

            // 서버에서 세션 히스토리 요청
            if (_claudeClient != null && _claudeClient.IsConnected)
                _claudeClient.SendSessionList(_currentAgentId);

            if (_panelRoot != null) _panelRoot.SetActive(true);
            if (_emptyHint != null) _emptyHint.SetActive(true);
            if (_inputField != null)
            {
                _inputField.text = "";
                _inputField.ActivateInputField();
            }
        }

        public void Close()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
        }

        public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

        // ================================================================
        //  에이전트 연결 (HUD + FSM)
        // ================================================================

        private void FindLinkedAgent()
        {
            _linkedHUD = null;
            _linkedCharCtrl = null;

            var boot = Object.FindFirstObjectByType<AgentOfficeBootstrapper>();
            if (boot?.CurrentAgent != null)
            {
                _linkedHUD = boot.CurrentAgent.HUD;
                if (boot.CurrentAgent.ModelInstance != null)
                    _linkedCharCtrl = boot.CurrentAgent.ModelInstance.GetComponent<AgentCharacterController>();
            }

            if (_linkedCharCtrl == null)
                _linkedCharCtrl = Object.FindFirstObjectByType<AgentCharacterController>();
            if (_linkedHUD == null)
                _linkedHUD = Object.FindFirstObjectByType<AgentHUDController>();

            if (_linkedCharCtrl != null)
                Debug.Log($"[ChatPanel] 에이전트 연결됨: {_currentAgentName} (id: {_currentAgentId})");
            else
                Debug.LogWarning("[ChatPanel] 에이전트 연결 실패 -- FSM/HUD 상태 갱신 불가");
        }

        private void SetAgentState(AgentActionType state)
        {
            _linkedHUD?.ApplyState(state);
            _linkedCharCtrl?.ForceState(state);
        }

        // ================================================================
        //  메시지 전송
        // ================================================================

        private void OnSendClicked()
        {
            if (_isSending) return;
            if (_inputField == null) return;

            var text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            _inputField.text = "";
            _inputField.ActivateInputField();

            SendMessage(text).Forget();
        }

        private async UniTaskVoid SendMessage(string text)
        {
            _isSending = true;
            if (_sendButton != null) _sendButton.interactable = false;
            if (_emptyHint != null) _emptyHint.SetActive(false);

            SpawnBubble(ChatSender.User, text, System.DateTime.Now, true);

            SetAgentState(AgentActionType.Thinking);
            UpdateSubtitle("사고 중...");

            if (_claudeClient != null && _claudeClient.IsConnected)
            {
                _streamingContent = "";
                _streamingBubble = SpawnBubble(ChatSender.Agent, "...", System.DateTime.Now, true);
                _streamingText = _streamingBubble?.GetComponentInChildren<TMP_Text>();

                _claudeClient.SendChatMessage(_currentAgentId, text);
            }
            else
            {
                SpawnBubble(ChatSender.System,
                    "서버에 연결되지 않았습니다. 미들웨어 실행 상태를 확인해주세요.",
                    System.DateTime.Now, true);

                SetAgentState(AgentActionType.Idle);
                UpdateSubtitle("대기 중");
                _isSending = false;
                if (_sendButton != null) _sendButton.interactable = true;
            }
        }

        // ================================================================
        //  미들웨어 이벤트 핸들러
        // ================================================================

        private void HandleDelta(string agentId, string text)
        {
            if (agentId != _currentAgentId) return;
            if (_streamingBubble == null) return;

            if (string.IsNullOrEmpty(_streamingContent))
            {
                SetAgentState(AgentActionType.ChatDelta);
                UpdateSubtitle("응답 중...");
            }

            _streamingContent += text;

            if (_streamingText != null)
                _streamingText.text = FormatBubbleText(ChatSender.Agent, _streamingContent, System.DateTime.Now);

            ScrollToBottomNextFrame().Forget();
        }

        private void HandleMessage(string agentId, string message)
        {
            if (agentId != _currentAgentId) return;

            var finalText = string.IsNullOrEmpty(message) ? _streamingContent : message;

            if (_streamingText != null)
                _streamingText.text = FormatBubbleText(ChatSender.Agent, finalText, System.DateTime.Now);

            SetAgentState(AgentActionType.ChatFinal);
            UpdateSubtitle("응답 완료");

            RestoreSubtitleAfterDelay().Forget();

            _streamingBubble = null;
            _streamingText = null;
            _streamingContent = "";
            _isSending = false;
            if (_sendButton != null) _sendButton.interactable = true;

            ScrollToBottomNextFrame().Forget();
        }

        private void HandleError(string agentId, string error, string errorMessage)
        {
            if (agentId != _currentAgentId && !string.IsNullOrEmpty(agentId)) return;

            var displayMsg = $"[{error}] {errorMessage}";

            if (_streamingBubble != null && _streamingText != null)
                _streamingText.text = FormatBubbleText(ChatSender.System, displayMsg, System.DateTime.Now);
            else
                SpawnBubble(ChatSender.System, displayMsg, System.DateTime.Now, true);

            SetAgentState(AgentActionType.Idle);
            UpdateSubtitle("대기 중");

            _streamingBubble = null;
            _streamingText = null;
            _streamingContent = "";
            _isSending = false;
            if (_sendButton != null) _sendButton.interactable = true;
        }

        private void HandleState(string agentId, string state, string tool)
        {
            if (agentId != _currentAgentId) return;

            switch (state)
            {
                case "thinking":
                    if (_isSending)
                    {
                        SetAgentState(AgentActionType.Thinking);
                        UpdateSubtitle("사고 중...");
                    }
                    break;
                case "working":
                    SetAgentState(AgentActionType.Thinking);
                    var toolName = string.IsNullOrEmpty(tool) ? "도구" : tool;
                    UpdateSubtitle($"도구 실행: {toolName}");
                    break;
                case "complete":
                    // agent_message에서 처리
                    break;
                case "idle":
                    if (!_isSending)
                    {
                        SetAgentState(AgentActionType.Idle);
                        UpdateSubtitle("대기 중");
                    }
                    break;
            }
        }

        private void HandleThinking(string agentId, string thinking)
        {
            if (agentId != _currentAgentId) return;
            // thinking 내용을 subtitle에 미리보기
            if (_isSending)
            {
                var preview = thinking.Length > 25 ? thinking[..25] + "..." : thinking;
                UpdateSubtitle($"사고: {preview}");
            }
        }

        private void HandleAction(string agentId, string action)
        {
            if (agentId != _currentAgentId) return;

            // 서버 액션 -> Unity FSM 매핑
            var fsmState = action switch
            {
                "idle"     => AgentActionType.Idle,
                "typing"   => AgentActionType.ChatDelta,
                "walk"     => AgentActionType.Idle,    // Walking 없으면 Idle
                "cheering" => AgentActionType.ChatFinal,
                "sitting"  => AgentActionType.Thinking,
                "drinking" => AgentActionType.Thinking,
                "dancing"  => AgentActionType.ChatFinal,
                _          => AgentActionType.Idle
            };

            SetAgentState(fsmState);
            Debug.Log($"[ChatPanel] 액션 수신: {action} -> {fsmState}");
        }

        private void HandleSessionSwitched(string agentId, string sessionId, ChatHistoryEntry[] history)
        {
            if (agentId != _currentAgentId) return;

            ClearBubbles();

            if (history == null || history.Length == 0)
            {
                if (_emptyHint != null) _emptyHint.SetActive(true);
                return;
            }

            if (_emptyHint != null) _emptyHint.SetActive(false);

            foreach (var entry in history)
            {
                var sender = entry.role == "user" ? ChatSender.User : ChatSender.Agent;
                SpawnBubble(sender, entry.text, System.DateTime.Now, false);
            }

            ScrollToBottomNextFrame().Forget();
        }

        // ================================================================
        //  서브타이틀 업데이트
        // ================================================================

        private void UpdateSubtitle(string status)
        {
            if (_headerSubtitle == null) return;
            var roleName = RoleNames.GetValueOrDefault(_currentRole, "에이전트");
            _headerSubtitle.text = $"{roleName} · {status}";
        }

        private async UniTaskVoid RestoreSubtitleAfterDelay()
        {
            await UniTask.Delay(3500, cancellationToken: destroyCancellationToken);
            if (!_isSending)
                UpdateSubtitle("대화 중");
        }

        // ================================================================
        //  버블 생성
        // ================================================================

        private GameObject SpawnBubble(ChatSender sender, string text, System.DateTime time, bool autoScroll)
        {
            var prefab = sender switch
            {
                ChatSender.User   => _userBubblePrefab,
                ChatSender.Agent  => _agentBubblePrefab,
                ChatSender.System => _systemBubblePrefab,
                _ => _agentBubblePrefab
            };

            if (prefab == null || _messageContent == null) return null;

            var bubble = Instantiate(prefab, _messageContent);
            bubble.SetActive(true);
            _spawnedBubbles.Add(bubble);

            var tmp = bubble.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.text = FormatBubbleText(sender, text, time);

            if (autoScroll) ScrollToBottomNextFrame().Forget();

            return bubble;
        }

        private static string FormatBubbleText(ChatSender sender, string text, System.DateTime time)
        {
            var timeStr = time.ToLocalTime().ToString("HH:mm");
            var label = sender switch
            {
                ChatSender.User => "나",
                ChatSender.Agent => "AI",
                ChatSender.System => "",
                _ => ""
            };

            if (sender == ChatSender.System)
                return $"<color=#888><size=11>{timeStr}</size></color>  {text}";

            return $"<color=#888><size=11>{timeStr}</size></color>  <b>{label}</b>\n{text}";
        }

        // ================================================================
        //  유틸
        // ================================================================

        private void ClearBubbles()
        {
            foreach (var b in _spawnedBubbles)
                if (b != null) Destroy(b);
            _spawnedBubbles.Clear();
        }

        private async UniTask ScrollToBottomNextFrame()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);
            if (_scrollRect != null) _scrollRect.normalizedPosition = Vector2.zero;
        }
    }
}
