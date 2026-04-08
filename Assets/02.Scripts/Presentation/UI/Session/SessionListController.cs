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
    /// 세션 리스트 패널 -- 서버 세션 연동.
    /// ClaudeWebSocketClient의 session_list_response를 수신하여 UI 갱신.
    /// 세션 CRUD는 모두 서버에 요청.
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

        [Header("Claude")]
        [SerializeField] private ClaudeWebSocketClient _claudeClient;

        // ── 상태 ────────────────────────────────────────────
        private string _currentAgentId;
        private string _currentAgentName;
        private AgentRole _currentRole;
        private string _currentSessionId;
        private readonly List<GameObject> _spawnedItems = new();

        /// <summary>세션 선택 시 (agentId, sessionId)</summary>
        public event Action<string, string> OnSessionSelected;

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

            if (_claudeClient != null)
            {
                _claudeClient.OnSessionList     += HandleSessionList;
                _claudeClient.OnSessionSwitched += HandleSessionSwitched;
            }
        }

        private void OnDestroy()
        {
            if (_claudeClient != null)
            {
                _claudeClient.OnSessionList     -= HandleSessionList;
                _claudeClient.OnSessionSwitched -= HandleSessionSwitched;
            }
        }

        // ================================================================
        //  외부 API
        // ================================================================

        /// <summary>특정 에이전트의 세션 리스트를 열기 (3D 캐릭터 클릭 시 호출)</summary>
        public void OpenForAgent(string agentId, string agentName, AgentRole role)
        {
            _currentAgentId = agentId;
            _currentAgentName = agentName;
            _currentRole = role;

            if (_headerAgentName != null) _headerAgentName.text = agentName;
            if (_headerAgentRole != null)
                _headerAgentRole.text = RoleNames.GetValueOrDefault(role, "에이전트");

            // 서버에 세션 목록 요청
            if (_claudeClient != null && _claudeClient.IsConnected)
                _claudeClient.SendSessionList(_currentAgentId);

            // 목록 도착 전 빈 상태로 표시
            ClearItems();
            if (_emptyState != null) _emptyState.SetActive(true);

            if (_panelRoot != null) _panelRoot.SetActive(true);
            OnPanelToggled?.Invoke(true);
        }

        public void ClosePanel()
        {
            if (_panelRoot != null) _panelRoot.SetActive(false);
            OnPanelToggled?.Invoke(false);
        }

        public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

        // ================================================================
        //  서버 이벤트 핸들러
        // ================================================================

        private void HandleSessionList(string agentId, string currentSessionId, SessionInfo[] sessions)
        {
            if (agentId != _currentAgentId) return;

            _currentSessionId = currentSessionId;
            ClearItems();

            if (sessions == null || sessions.Length == 0)
            {
                if (_emptyState != null) _emptyState.SetActive(true);
                return;
            }

            if (_emptyState != null) _emptyState.SetActive(false);

            foreach (var session in sessions)
                SpawnSessionItem(session);
        }

        private void HandleSessionSwitched(string agentId, string sessionId, ChatHistoryEntry[] history)
        {
            if (agentId != _currentAgentId) return;

            _currentSessionId = sessionId;

            // 채팅 패널이 열려있으면 히스토리 복원은 ChatPanelController가 처리
            // 세션 목록의 활성 표시만 갱신
            RefreshActiveHighlight();
        }

        // ================================================================
        //  리스트 렌더링
        // ================================================================

        private void SpawnSessionItem(SessionInfo session)
        {
            if (_sessionItemPrefab == null || _listContent == null) return;

            var item = Instantiate(_sessionItemPrefab, _listContent);
            item.SetActive(true);
            _spawnedItems.Add(item);

            // 텍스트 바인딩
            var texts = item.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length >= 3)
            {
                texts[0].text = string.IsNullOrEmpty(session.title) ? "새 대화" : session.title;
                texts[1].text = $"메시지 {session.message_count}개";
                texts[2].text = FormatTimestamp(session.updated_at);
            }

            // 보더 하이라이트 (활성 세션)
            var border = item.transform.Find("Border");
            if (border != null)
                border.gameObject.SetActive(session.session_id == _currentSessionId);

            // 클릭 이벤트
            var btn = item.GetComponent<Button>();
            if (btn != null)
            {
                var sid = session.session_id;
                btn.onClick.AddListener(() => SelectSession(sid));
            }
        }

        private void RefreshActiveHighlight()
        {
            // 세션 목록 갱신 요청
            if (_claudeClient != null && _claudeClient.IsConnected)
                _claudeClient.SendSessionList(_currentAgentId);
        }

        // ================================================================
        //  세션 선택
        // ================================================================

        private void SelectSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            // 서버에 세션 전환 요청
            _claudeClient?.SendSessionSwitch(_currentAgentId, sessionId);

            // 채팅 패널 열기
            if (_chatPanel != null)
                _chatPanel.Open(_currentAgentId, _currentAgentName, _currentRole);

            OnSessionSelected?.Invoke(_currentAgentId, sessionId);
            Debug.Log($"[SessionList] 세션 선택: {_currentAgentId}/{sessionId}");
        }

        // ================================================================
        //  새 세션 생성
        // ================================================================

        private void CreateNewSession()
        {
            if (string.IsNullOrEmpty(_currentAgentId)) return;

            // 서버에 새 세션 요청 -> session_switched 이벤트로 응답
            _claudeClient?.SendSessionNew(_currentAgentId);

            // 채팅 패널 열기
            if (_chatPanel != null)
                _chatPanel.Open(_currentAgentId, _currentAgentName, _currentRole);

            Debug.Log($"[SessionList] 새 세션 생성 요청: {_currentAgentId}");
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

        private static string FormatTimestamp(double unixTimestamp)
        {
            if (unixTimestamp <= 0) return "";

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dt = epoch.AddSeconds(unixTimestamp).ToLocalTime();
            var now = DateTime.Now;

            if (dt.Date == now.Date)
                return dt.ToString("HH:mm");
            if (dt.Date == now.Date.AddDays(-1))
                return "어제";
            return dt.ToString("MM/dd");
        }
    }
}
