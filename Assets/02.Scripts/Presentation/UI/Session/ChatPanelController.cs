using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Claude;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Skills;
using OpenDesk.Presentation.Character;
using OpenDesk.Pipeline;
using OpenDesk.SkillDiskette;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Session
{
    /// <summary>
    /// [Deprecated] uGUI 채팅 패널. UI Toolkit 버전 <see cref="OpenDesk.Presentation.UI.Chat.ChatPanelView"/> 로 대체됨.
    /// 신규 씬은 ChatPanelView 를 사용하세요. 본 클래스는 마이그레이션 기간 동안 보존됩니다.
    /// </summary>
    [System.Obsolete("uGUI 채팅 패널은 OpenDesk.Presentation.UI.Chat.ChatPanelView (UI Toolkit) 으로 대체되었습니다.")]
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

        [Header("Claude (Inspector binding — used only as fallback if DI-injected service is unavailable)")]
        [SerializeField] private ClaudeWebSocketClient _claudeClient;
        [SerializeField] private MiddlewareLauncher _middlewareLauncher;

        // ── AI 백엔드 (DI) ──────────────────────────────────
        // CLI 백엔드: AnthropicCliChatService → ClaudeWebSocketClient 래핑
        // API 백엔드: AnthropicApiChatService → HTTP 직접 호출
        // 토글: PlayerPrefs `OpenDesk_ChatBackend` ("cli" | "api")
        private IAiChatService _chat;
        private IAgentSkillLoadoutService _loadoutService;
        private ISkillCatalogService _catalogService;

        [Inject]
        public void Construct(
            IAiChatService chat,
            IAgentSkillLoadoutService loadoutService = null,
            ISkillCatalogService catalogService = null)
        {
            _chat = chat;
            _loadoutService = loadoutService;
            _catalogService = catalogService;
        }

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
        // 레거시 _linkedHUD (AgentHUDController) 제거됨 — AgentHudView 가 상태 구독 담당.
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

            if (_chat != null)
            {
                _chat.OnDelta  += HandleDelta;
                _chat.OnFinal  += HandleFinal;
                _chat.OnError  += HandleError;
                _chat.OnStatus += HandleStatus;
            }
        }

        private void OnDestroy()
        {
            if (_chat != null)
            {
                _chat.OnDelta  -= HandleDelta;
                _chat.OnFinal  -= HandleFinal;
                _chat.OnError  -= HandleError;
                _chat.OnStatus -= HandleStatus;
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
            _linkedCharCtrl = null;

            // 1차: AgentSpawner 의 SpawnedAgents 에서 첫 번째 에이전트 사용 (단일 에이전트 채팅 가정).
            // 다중 에이전트 채팅 라우팅은 별건 PR.
            // 레거시 AgentHUDController 슬롯은 제거됨 — AgentHudView 가 상태를 자체 구독.
            var spawner = Object.FindFirstObjectByType<AgentSpawner>();
            if (spawner != null)
            {
                foreach (var kv in spawner.SpawnedAgents)
                {
                    var spawned = kv.Value;
                    if (spawned == null) continue;
                    if (spawned.ModelInstance != null)
                        _linkedCharCtrl = spawned.ModelInstance.GetComponent<AgentCharacterController>();
                    break;
                }
            }

            // 2차: Spawner 없거나 비었으면 씬에서 직접 탐색.
            if (_linkedCharCtrl == null)
                _linkedCharCtrl = Object.FindFirstObjectByType<AgentCharacterController>();

            if (_linkedCharCtrl != null)
                Debug.Log($"[ChatPanel] 에이전트 연결됨: {_currentAgentName} (FSM: {_linkedCharCtrl.SessionId})");
            else
                Debug.LogWarning("[ChatPanel] 에이전트 연결 실패 — FSM/HUD 상태 갱신 불가");
        }

        /// <summary>에이전트 상태 변경 → FSM 갱신 (HUD 는 AgentHudView 가 IAgentStateService 로 직접 구독)</summary>
        private void SetAgentState(AgentActionType state)
        {
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
            if (_chat == null || !_chat.IsConnected) return;

            var convFile = ChatMessageStore.LoadConversationFile(_currentSessionId);
            if (convFile == null || convFile.Messages.Count == 0)
            {
                _chat.ClearHistory();
                ApplySystemPrompt();
                return;
            }

            var historyJson = JsonUtility.ToJson(convFile);
            _chat.ResumeSession(historyJson);
        }

        /// <summary>
        /// 장착 디스켓 + In-box 파일 기반 system prompt 합성 및 적용.
        /// 디스켓이 없으면 기본 프로필로 fallback.
        /// </summary>
        private void ApplySystemPrompt()
        {
            if (_chat == null || !_chat.IsConnected) return;

            var equipment = _linkedCharCtrl?.Equipment;
            if (equipment != null)
            {
                // JSON-SSOT: profile.Source(AgentDraftRecord) 로 BindAgent — traits 까지 손실 없이 전달.
                // Source 가 비어있는 디자이너 SO 는 옛 SetAgentProfile 로 폴백.
                var record = _linkedCharCtrl.Profile?.Source;
                if (record != null)
                {
                    equipment.BindAgent(record);
                }
                else
                {
                    var tone = _linkedCharCtrl.Profile != null
                        ? _linkedCharCtrl.Profile.Tone
                        : AgentTone.None;
#pragma warning disable CS0618 // BindAgent 가 불가능한 디자이너 SO 폴백 경로.
                    equipment.SetAgentProfile(_currentAgentName, _currentRole, tone, agentId: _currentAgentName);
#pragma warning restore CS0618
                }

                // Loadout 영속과 카탈로그를 바인딩 — 마켓 패널의 장착 변경이 즉시 반영된다.
                // 키는 기존 호환을 위해 _currentAgentName 유지 (record.id 로 갈아끼우면 PlayerPrefs 호환 깨짐).
                if (_loadoutService != null && _catalogService != null)
                    equipment.BindLoadoutService(_loadoutService, _catalogService, _currentAgentName);

                var pipeline = FindFirstObjectByType<OfficePipelineManager>();
                var prompt = pipeline != null
                    ? pipeline.BuildFullSystemPrompt(equipment)
                    : equipment.BuildSystemPrompt();

                if (!string.IsNullOrEmpty(prompt))
                {
                    _chat.SetSystemPrompt(prompt);
                    _chat.SendSkillLoadout(equipment.BuildSkillLoadoutPayload());
                    var fileCount = pipeline?.Inbox?.FilePaths?.Count ?? 0;
                    Debug.Log($"[ChatPanel] System prompt 적용 ({prompt.Length}자, 스킬 {equipment.EquippedCount}개, 파일 {fileCount}개)");
                    return;
                }
            }

            // fallback: 디스켓 없을 때 기본 프롬프트
            var fallbackRole = RoleNames.GetValueOrDefault(_currentRole, "에이전트");
            _chat.SetSystemPrompt(
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

            if (_chat != null && _chat.IsConnected)
            {
                _streamingContent = "";
                _streamingBubble = SpawnBubble(ChatSender.Agent, "...", System.DateTime.Now, true);
                _streamingText = _streamingBubble?.GetComponentInChildren<TMP_Text>();

                _chat.SendMessage(text);
            }
            else
            {
                SpawnBubble(ChatSender.System,
                    "AI 백엔드에 연결되지 않았습니다. (CLI: 미들웨어 실행 / API: Anthropic 키 설정 확인)",
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
