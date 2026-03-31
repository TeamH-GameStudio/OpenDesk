using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Claude;
using OpenDesk.Core.Models;
using OpenDesk.Presentation.Character;
using OpenDesk.Pipeline;
using OpenDesk.SkillDiskette;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Session
{
    /// <summary>
    /// 채팅 패널 — Claude CLI 미들웨어 연동.
    /// 메시지 전송/수신 시 에이전트 HUD + FSM 상태를 동기화.
    ///
    /// 흐름:
    ///   메시지 전송 → Thinking (의자로 이동+생각)
    ///   → delta 수신 → Chatting (타이핑)
    ///   → final 수신 → Completed (환호) → Idle
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
        [SerializeField] private MiddlewareLauncher _middlewareLauncher;

        // ── 상태 ────────────────────────────────────────────
        private string _currentSessionId;
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
                _claudeClient.OnDelta += HandleDelta;
                _claudeClient.OnFinal += HandleFinal;
                _claudeClient.OnError += HandleError;
                _claudeClient.OnStatus += HandleStatus;
            }
        }

        private void OnDestroy()
        {
            if (_claudeClient != null)
            {
                _claudeClient.OnDelta -= HandleDelta;
                _claudeClient.OnFinal -= HandleFinal;
                _claudeClient.OnError -= HandleError;
                _claudeClient.OnStatus -= HandleStatus;
            }
        }

        // ================================================================
        //  외부 API
        // ================================================================

        public void Open(string sessionId, string agentName, AgentRole role)
        {
            _currentSessionId = sessionId;
            _currentAgentName = agentName;
            _currentRole = role;

            if (_headerTitle != null)
                _headerTitle.text = agentName;
            if (_headerSubtitle != null)
                _headerSubtitle.text = RoleNames.GetValueOrDefault(role, "에이전트") + " · 대화 중";

            FindLinkedAgent();
            LoadHistory();
            ResumeSession();

            if (_panelRoot != null) _panelRoot.SetActive(true);
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

        /// <summary>현재 씬에서 에이전트의 HUD/CharacterController 찾기</summary>
        private void FindLinkedAgent()
        {
            _linkedHUD = null;
            _linkedCharCtrl = null;

            // 1차: Bootstrapper에서 현재 에이전트 가져오기
            var boot = Object.FindFirstObjectByType<AgentOfficeBootstrapper>();
            if (boot?.CurrentAgent != null)
            {
                _linkedHUD = boot.CurrentAgent.HUD;
                if (boot.CurrentAgent.ModelInstance != null)
                    _linkedCharCtrl = boot.CurrentAgent.ModelInstance.GetComponent<AgentCharacterController>();
            }

            // 2차: Bootstrapper 없으면 씬에서 직접 탐색
            if (_linkedCharCtrl == null)
                _linkedCharCtrl = Object.FindFirstObjectByType<AgentCharacterController>();
            if (_linkedHUD == null)
                _linkedHUD = Object.FindFirstObjectByType<AgentHUDController>();

            if (_linkedCharCtrl != null)
                Debug.Log($"[ChatPanel] 에이전트 연결됨: {_currentAgentName} (FSM: {_linkedCharCtrl.SessionId})");
            else
                Debug.LogWarning("[ChatPanel] 에이전트 연결 실패 — FSM/HUD 상태 갱신 불가");
        }

        /// <summary>에이전트 상태 변경 → HUD + FSM 동시 갱신</summary>
        private void SetAgentState(AgentActionType state)
        {
            _linkedHUD?.ApplyState(state);
            _linkedCharCtrl?.ForceState(state);
        }

        // ================================================================
        //  히스토리 로드
        // ================================================================

        private void LoadHistory()
        {
            ClearBubbles();

            var messages = ChatMessageStore.Load(_currentSessionId);

            if (messages.Count == 0)
            {
                if (_emptyHint != null) _emptyHint.SetActive(true);
                return;
            }

            if (_emptyHint != null) _emptyHint.SetActive(false);

            foreach (var msg in messages)
                SpawnBubble(msg.Sender, msg.Text, msg.Time, false);

            ScrollToBottomNextFrame().Forget();
        }

        // ================================================================
        //  Claude 세션 Resume
        // ================================================================

        private void ResumeSession()
        {
            if (_claudeClient == null || !_claudeClient.IsConnected) return;

            var convFile = ChatMessageStore.LoadConversationFile(_currentSessionId);
            if (convFile == null || convFile.Messages.Count == 0)
            {
                _claudeClient.SendClear();
                ApplySystemPrompt();
                return;
            }

            var historyJson = JsonUtility.ToJson(convFile);
            _claudeClient.SendResume(historyJson);
        }

        /// <summary>
        /// 장착 디스켓 + In-box 파일 기반 system prompt 합성 및 적용.
        /// 디스켓이 없으면 기본 프로필로 fallback.
        /// </summary>
        private void ApplySystemPrompt()
        {
            if (_claudeClient == null || !_claudeClient.IsConnected) return;

            var equipment = _linkedCharCtrl?.Equipment;
            if (equipment != null)
            {
                // 에이전트 프로필 설정 (위저드 데이터 반영)
                var roleName = RoleNames.GetValueOrDefault(_currentRole, "에이전트");
                var toneName = _currentRole.ToString();
                equipment.SetAgentProfile(_currentAgentName, roleName, toneName);

                // 파이프라인 매니저가 있으면 파일 컨텍스트도 포함
                var pipeline = FindFirstObjectByType<OfficePipelineManager>();
                var prompt = pipeline != null
                    ? pipeline.BuildFullSystemPrompt(equipment)
                    : equipment.BuildSystemPrompt();

                if (!string.IsNullOrEmpty(prompt))
                {
                    _claudeClient.SendConfig(prompt);
                    var fileCount = pipeline?.Inbox?.FilePaths?.Count ?? 0;
                    Debug.Log($"[ChatPanel] System prompt 적용 ({prompt.Length}자, 디스켓 {equipment.EquippedDisks.Count}개, 파일 {fileCount}개)");
                    return;
                }
            }

            // fallback: 디스켓 없을 때 기본 프롬프트
            var fallbackRole = RoleNames.GetValueOrDefault(_currentRole, "에이전트");
            _claudeClient.SendConfig(
                $"당신은 '{_currentAgentName}'이라는 이름의 {fallbackRole} 전문 에이전트입니다. " +
                "한국어로 대화하며, 사용자의 요청에 전문적으로 답변합니다.");
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

            // 사용자 메시지 표시 + JSON 저장
            ChatMessageStore.Append(_currentSessionId, ChatSender.User, text, _currentAgentName);
            SpawnBubble(ChatSender.User, text, System.DateTime.Now, true);

            // ★ 에이전트 상태: Thinking (의자로 이동 + 생각 모션)
            SetAgentState(AgentActionType.Thinking);
            UpdateSubtitle("생각 중...");

            // 장착 디스켓 변경 반영 (매 메시지마다 최신 prompt 적용)
            ApplySystemPrompt();

            if (_claudeClient != null && _claudeClient.IsConnected)
            {
                _streamingContent = "";
                _streamingBubble = SpawnBubble(ChatSender.Agent, "...", System.DateTime.Now, true);
                _streamingText = _streamingBubble?.GetComponentInChildren<TMP_Text>();

                _claudeClient.SendChat(text);
            }
            else
            {
                SpawnBubble(ChatSender.System,
                    "Claude 서버에 연결되지 않았습니다. 미들웨어 실행 상태를 확인해주세요.",
                    System.DateTime.Now, true);

                SetAgentState(AgentActionType.Idle);
                UpdateSubtitle("대기 중");
                _isSending = false;
                if (_sendButton != null) _sendButton.interactable = true;
            }
        }

        // ================================================================
        //  Claude 이벤트 핸들러
        // ================================================================

        private void HandleDelta(string text)
        {
            if (_streamingBubble == null) return;

            // ★ 첫 delta 수신 → Typing 상태 (생각 끝, 타이핑 시작)
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

        private void HandleFinal(string text, float cost)
        {
            var finalText = string.IsNullOrEmpty(text) ? _streamingContent : text;

            if (_streamingText != null)
                _streamingText.text = FormatBubbleText(ChatSender.Agent, finalText, System.DateTime.Now);

            if (!string.IsNullOrEmpty(finalText))
                ChatMessageStore.Append(_currentSessionId, ChatSender.Agent, finalText, _currentAgentName);

            // ★ 에이전트 상태: Completed (환호 → 3초 후 Idle로 자동 복귀)
            SetAgentState(AgentActionType.ChatFinal);
            UpdateSubtitle("응답 완료");

            // Out-box 연동: In-box에 파일이 있었으면 결과도 저장
            var pipeline = FindFirstObjectByType<OfficePipelineManager>();
            if (pipeline?.Outbox != null && pipeline.Inbox != null
                && pipeline.Inbox.FilePaths.Count > 0
                && !string.IsNullOrEmpty(finalText))
            {
                pipeline.Outbox.ReceiveResult(finalText);
            }

            // 3초 후 서브타이틀 복귀
            RestoreSubtitleAfterDelay().Forget();

            _streamingBubble = null;
            _streamingText = null;
            _streamingContent = "";
            _isSending = false;
            if (_sendButton != null) _sendButton.interactable = true;

            ScrollToBottomNextFrame().Forget();
        }

        private void HandleError(string errorMsg)
        {
            if (_streamingBubble != null && _streamingText != null)
                _streamingText.text = FormatBubbleText(ChatSender.System, $"[오류] {errorMsg}", System.DateTime.Now);
            else
                SpawnBubble(ChatSender.System, $"[오류] {errorMsg}", System.DateTime.Now, true);

            SetAgentState(AgentActionType.Idle);
            UpdateSubtitle("대기 중");

            _streamingBubble = null;
            _streamingText = null;
            _streamingContent = "";
            _isSending = false;
            if (_sendButton != null) _sendButton.interactable = true;
        }

        private void HandleStatus(string statusText)
        {
            if (_isSending)
                UpdateSubtitle(statusText);
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
