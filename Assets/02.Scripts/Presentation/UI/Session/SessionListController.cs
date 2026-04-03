using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
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

        // ── 상태 ────────────────────────────────────────────
        private int _currentAgentIndex = -1;
        private string _currentAgentName;
        private AgentRole _currentRole;
        private int _activeSessionIndex = -1;
        private readonly List<GameObject> _spawnedItems = new();

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

            RefreshList();

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

            // 보더 갱신
            RefreshList();

            // 채팅 패널 열기
            var session = AgentSessionStore.Load(sessionIndex);
            if (session != null && _chatPanel != null)
            {
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
            if (_currentAgentIndex < 0) return;

            var newIdx = AgentSessionStore.CreateSession(
                _currentAgentIndex, _currentAgentName, _currentRole);

            RefreshList();

            // 바로 채팅 패널 열기
            var session = AgentSessionStore.Load(newIdx);
            if (session != null && _chatPanel != null)
                _chatPanel.Open(session.SessionId, _currentAgentName, _currentRole);

            OnSessionSelected?.Invoke(newIdx);

            Debug.Log($"[SessionList] 새 세션 생성: [{newIdx}] {_currentAgentName}");
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
