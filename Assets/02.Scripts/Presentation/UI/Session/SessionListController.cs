using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Session
{
    /// <summary>
    /// 세션 리스트 패널 컨트롤러.
    /// 에이전트별 대화 세션 목록 표시, 활성 세션 보더 하이라이트,
    /// 새 대화 생성, 세션 선택 이벤트 발행.
    /// </summary>
    public class SessionListController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Button _closeButton;

        [Header("Header")]
        [SerializeField] private TMP_Text _headerAgentName;
        [SerializeField] private TMP_Text _headerAgentRole;
        [SerializeField] private Button _newSessionButton;

        [Header("List")]
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _listContent;
        [SerializeField] private GameObject _sessionItemPrefab;

        [Header("Empty State")]
        [SerializeField] private GameObject _emptyState;

        [Header("Chat")]
        [SerializeField] private ChatPanelController _chatPanel;

        [Header("서버 세션 (새 프로토콜)")]
        [SerializeField] private ClaudeWebSocketClient _wsClient;

        // ── 상태 ────────────────────────────────────────────
        private int _currentAgentIndex = -1;
        private string _currentAgentId;   // 새 프로토콜용
        private string _currentAgentName;
        private AgentRole _currentRole;
        private int _activeSessionIndex = -1;
        private readonly List<GameObject> _spawnedItems = new();
        private bool _useServerSessions; // 서버 세션 모드 여부

        /// <summary>세션 선택 시 (sessionIndex)</summary>
        public event Action<int> OnSessionSelected;

        /// <summary>패널 열림/닫힘</summary>
        public event Action<bool> OnPanelToggled;

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
            _closeButton?.onClick.AddListener(ClosePanel);
            _newSessionButton?.onClick.AddListener(CreateNewSession);

            if (_panelRoot != null) _panelRoot.SetActive(false);

            // 서버 세션 이벤트 구독
            if (_wsClient != null)
            {
                _useServerSessions = true;
                _wsClient.OnSessionListResponse += HandleServerSessionList;
                _wsClient.OnSessionSwitched += HandleServerSessionSwitched;
            }
        }

        private void OnDestroy()
        {
            if (_wsClient != null)
            {
                _wsClient.OnSessionListResponse -= HandleServerSessionList;
                _wsClient.OnSessionSwitched -= HandleServerSessionSwitched;
            }
        }

        // ================================================================
        //  외부 API
        // ================================================================

        /// <summary>특정 에이전트의 세션 리스트를 열기 (3D 캐릭터 클릭 시 호출)</summary>
        public void OpenForAgent(int agentIndex, string agentName, AgentRole role)
        {
            _currentAgentIndex = agentIndex;
            _currentAgentName = agentName;
            _currentRole = role;

            if (_headerAgentName != null) _headerAgentName.text = agentName;
            if (_headerAgentRole != null)
                _headerAgentRole.text = RoleNames.GetValueOrDefault(role, "에이전트");

            if (_useServerSessions && _wsClient != null && _wsClient.IsConnected)
            {
                // 서버에 세션 목록 요청
                _wsClient.SendSessionList(_currentAgentId ?? "researcher");
            }
            else
            {
                RefreshList();
            }

            if (_panelRoot != null) _panelRoot.SetActive(true);
            OnPanelToggled?.Invoke(true);
        }

        /// <summary>서버 세션용 오버로드 (agentId 기반)</summary>
        public void OpenForAgent(string agentId, string agentName, AgentRole role)
        {
            _currentAgentId = agentId;
            OpenForAgent(0, agentName, role);
        }

        public void ClosePanel()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
            OnPanelToggled?.Invoke(false);
        }

        public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

        /// <summary>채팅 패널에서 뒤로가기 시 세션 목록 복귀</summary>
        public void ReturnFromChat()
        {
            RefreshList();
            if (_panelRoot != null) _panelRoot.SetActive(true);
            OnPanelToggled?.Invoke(true);
        }

        // ================================================================
        //  리스트 갱신
        // ================================================================

        public void RefreshList()
        {
            ClearItems();

            var sessions = AgentSessionStore.LoadByAgent(_currentAgentIndex);
            _activeSessionIndex = AgentSessionStore.GetActiveSessionIndex();

            if (sessions.Count == 0)
            {
                if (_emptyState != null) _emptyState.SetActive(true);
                return;
            }

            if (_emptyState != null) _emptyState.SetActive(false);

            // 최신순 정렬
            sessions.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));

            foreach (var session in sessions)
                SpawnSessionItem(session);
        }

        private void SpawnSessionItem(AgentSession session)
        {
            if (_sessionItemPrefab == null || _listContent == null) return;

            var item = Instantiate(_sessionItemPrefab, _listContent);
            item.SetActive(true);
            _spawnedItems.Add(item);

            // 텍스트 바인딩
            var texts = item.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length >= 3)
            {
                texts[0].text = session.Title;                                    // 제목
                texts[1].text = string.IsNullOrEmpty(session.LastMessage)
                    ? "아직 대화가 없습니다"
                    : Truncate(session.LastMessage, 40);                          // 미리보기
                texts[2].text = FormatTime(session.LastActivity);                 // 시간
            }

            // 보더 하이라이트 (활성 세션)
            var border = item.transform.Find("Border");
            if (border != null)
                border.gameObject.SetActive(session.IsActive);

            // 클릭 이벤트 — 세션 인덱스를 캡처
            var btn = item.GetComponent<Button>();
            if (btn != null)
            {
                var idx = FindSessionStoreIndex(session.SessionId);
                btn.onClick.AddListener(() => SelectSession(idx));
            }
        }

        private void SelectSession(int sessionIndex)
        {
            if (sessionIndex < 0) return;

            AgentSessionStore.SetActiveSession(sessionIndex);
            _activeSessionIndex = sessionIndex;

            // 세션 목록 숨기고 채팅 패널 열기
            var session = AgentSessionStore.Load(sessionIndex);
            if (session != null && _chatPanel != null)
            {
                if (_panelRoot != null) _panelRoot.SetActive(false);
                _chatPanel.Open(session.SessionId, _currentAgentName, _currentRole);
            }

            OnSessionSelected?.Invoke(sessionIndex);
            Debug.Log($"[SessionList] 세션 선택: [{sessionIndex}]");
        }

        // ================================================================
        //  새 세션 생성
        // ================================================================

        private void CreateNewSession()
        {
            if (_useServerSessions && _wsClient != null && _wsClient.IsConnected)
            {
                _wsClient.SendSessionNew(_currentAgentId ?? "researcher");
                Debug.Log($"[SessionList] 서버 새 세션 요청: {_currentAgentId}");
                return;
            }

            if (_currentAgentIndex < 0) return;

            var newIdx = AgentSessionStore.CreateSession(
                _currentAgentIndex, _currentAgentName, _currentRole);

            var session = AgentSessionStore.Load(newIdx);
            if (session != null && _chatPanel != null)
            {
                if (_panelRoot != null) _panelRoot.SetActive(false);
                _chatPanel.Open(session.SessionId, _currentAgentName, _currentRole);
            }

            OnSessionSelected?.Invoke(newIdx);
            Debug.Log($"[SessionList] 새 세션 생성: [{newIdx}] {_currentAgentName}");
        }

        // ── 서버 세션 핸들러 ────────────────────────────────

        private void HandleServerSessionList(SessionListResponse response)
        {
            if (response.agent_id != _currentAgentId) return;

            ClearItems();

            if (response.sessions == null || response.sessions.Length == 0)
            {
                if (_emptyState != null) _emptyState.SetActive(true);
                return;
            }

            if (_emptyState != null) _emptyState.SetActive(false);

            foreach (var info in response.sessions)
            {
                var isActive = info.session_id == response.current_session_id;
                SpawnServerSessionItem(info, isActive);
            }
        }

        private void SpawnServerSessionItem(SessionInfo info, bool isActive)
        {
            if (_sessionItemPrefab == null || _listContent == null) return;

            var item = Instantiate(_sessionItemPrefab, _listContent);
            item.SetActive(true);
            _spawnedItems.Add(item);

            var texts = item.GetComponentsInChildren<TMP_Text>(true);
            var title = string.IsNullOrEmpty(info.title) ? "(새 대화)" : info.title;

            if (texts.Length >= 3)
            {
                texts[0].text = title;
                texts[1].text = $"메시지 {info.message_count}개";
                texts[2].text = FormatTimestamp(info.updated_at);
            }
            else if (texts.Length >= 1)
            {
                texts[0].text = $"{title} ({info.message_count})";
            }

            var border = item.transform.Find("Border");
            if (border != null)
                border.gameObject.SetActive(isActive);

            var btn = item.GetComponent<Button>();
            if (btn != null)
            {
                var sid = info.session_id;
                btn.onClick.AddListener(() =>
                {
                    _wsClient.SendSessionSwitch(_currentAgentId, sid);
                    Debug.Log($"[SessionList] 서버 세션 전환: {sid}");
                });
            }
        }

        private void HandleServerSessionSwitched(SessionSwitchedMessage msg)
        {
            if (msg.agent_id != _currentAgentId) return;

            if (_chatPanel != null)
            {
                if (_panelRoot != null) _panelRoot.SetActive(false);
                _chatPanel.Open(msg.session_id, _currentAgentId, _currentAgentName, _currentRole);

                // 히스토리 로드는 ChatPanel 내부에서 처리하지 않고 여기서 직접
                // → Phase 3에서 ChatPanel이 session_switched를 구독하도록 확장 가능
            }
        }

        private static string FormatTimestamp(double unixTimestamp)
        {
            if (unixTimestamp <= 0) return "";
            var dt = DateTimeOffset.FromUnixTimeSeconds((long)unixTimestamp).LocalDateTime;
            var now = DateTime.Now;
            if (dt.Date == now.Date) return dt.ToString("HH:mm");
            if (dt.Date == now.Date.AddDays(-1)) return "어제";
            return dt.ToString("MM/dd");
        }

        // ================================================================
        //  유틸
        // ================================================================

        private void ClearItems()
        {
            foreach (var item in _spawnedItems)
                if (item != null) Destroy(item);
            _spawnedItems.Clear();
        }

        private static int FindSessionStoreIndex(string sessionId)
        {
            int count = AgentSessionStore.Count;
            for (int i = 0; i < count; i++)
            {
                var s = AgentSessionStore.Load(i);
                if (s != null && s.SessionId == sessionId)
                    return i;
            }
            return -1;
        }

        private static string Truncate(string text, int maxLen)
        {
            if (text.Length <= maxLen) return text;
            return text[..(maxLen - 3)] + "...";
        }

        private static string FormatTime(DateTime utcTime)
        {
            var local = utcTime.ToLocalTime();
            var now = DateTime.Now;

            if (local.Date == now.Date)
                return local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1))
                return "어제";
            return local.ToString("MM/dd");
        }
    }
}
