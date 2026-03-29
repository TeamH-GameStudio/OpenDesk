using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Session
{
    /// <summary>
    /// 채팅 패널 — 세션별 대화 내역 표시 + 메시지 입력/전송.
    /// SessionListController에서 세션 선택 시 Open됨.
    /// 서버 연결 전까지는 Mock AI 응답.
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

        // ── 상태 ────────────────────────────────────────────
        private string _currentSessionId;
        private string _currentAgentName;
        private AgentRole _currentRole;
        private readonly List<GameObject> _spawnedBubbles = new();
        private bool _isSending;

        // 간단한 Mock 응답
        private static readonly string[] MockResponses =
        {
            "네, 말씀하세요! 어떻게 도와드릴까요?",
            "흥미로운 질문이네요. 좀 더 자세히 알려주시겠어요?",
            "이해했습니다. 바로 확인해보겠습니다.",
            "좋은 아이디어입니다! 한번 진행해볼까요?",
            "잠시만요, 관련 자료를 찾아보고 있어요...\n\n확인 완료! 원하시는 방향으로 진행 가능합니다.",
            "네, 해당 내용은 제 전문 분야입니다. 자세히 설명드릴게요.",
        };

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
        }

        // ================================================================
        //  외부 API
        // ================================================================

        /// <summary>세션 선택 시 채팅 패널 오픈 + 히스토리 로드</summary>
        public void Open(string sessionId, string agentName, AgentRole role)
        {
            _currentSessionId = sessionId;
            _currentAgentName = agentName;
            _currentRole = role;

            if (_headerTitle != null)
                _headerTitle.text = agentName;
            if (_headerSubtitle != null)
                _headerSubtitle.text = RoleNames.GetValueOrDefault(role, "에이전트") + " · 대화 중";

            LoadHistory();

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

            // 사용자 메시지 표시 + 저장
            ChatMessageStore.Append(_currentSessionId, ChatSender.User, text);
            SpawnBubble(ChatSender.User, text, System.DateTime.Now, true);

            // AI 타이핑 표시
            var typingBubble = SpawnBubble(ChatSender.Agent, "...", System.DateTime.Now, true);

            // Mock 딜레이 (서버 연결 전)
            await UniTask.Delay(Random.Range(800, 2000), cancellationToken: destroyCancellationToken);

            // Mock 응답
            var response = MockResponses[Random.Range(0, MockResponses.Length)];

            // 타이핑 버블을 실제 응답으로 교체
            if (typingBubble != null)
            {
                var tmp = typingBubble.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.text = FormatBubbleText(ChatSender.Agent, response, System.DateTime.Now);
            }

            // 저장
            ChatMessageStore.Append(_currentSessionId, ChatSender.Agent, response);

            await ScrollToBottomNextFrame();

            _isSending = false;
            if (_sendButton != null) _sendButton.interactable = true;
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
            // 레이아웃 갱신 대기
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, destroyCancellationToken);
            if (_scrollRect != null) _scrollRect.normalizedPosition = Vector2.zero;
        }
    }
}
