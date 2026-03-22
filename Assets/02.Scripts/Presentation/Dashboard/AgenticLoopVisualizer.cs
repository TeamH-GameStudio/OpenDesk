using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.Dashboard
{
    /// <summary>
    /// 에이전틱 루프 시각화 — 해석→계획→선택→실행→검토 노드 그래프
    /// 각 단계 노드를 하이라이트하며 진행 상태를 시각적으로 표시
    /// </summary>
    public class AgenticLoopVisualizer : MonoBehaviour
    {
        [Header("루프 노드 (Inspector에서 순서대로 연결)")]
        [SerializeField] private LoopNodeUI _thinkingNode;    // 해석/사고
        [SerializeField] private LoopNodeUI _planningNode;    // 계획
        [SerializeField] private LoopNodeUI _executingNode;   // 실행
        [SerializeField] private LoopNodeUI _reviewingNode;   // 검토
        [SerializeField] private LoopNodeUI _completedNode;   // 완료

        [Header("연결선")]
        [SerializeField] private Image[] _connectionLines;     // 노드 사이 연결선

        [Header("상태 텍스트")]
        [SerializeField] private TMP_Text _statusText;         // "계획 수립 중..."
        [SerializeField] private TMP_Text _sessionIdText;      // "main"

        [Header("설정")]
        [SerializeField] private string _targetSessionId = "main";
        [SerializeField] private Color  _activeColor     = new(0.2f, 0.8f, 1f, 1f);
        [SerializeField] private Color  _inactiveColor   = new(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color  _completedColor  = new(0.2f, 1f, 0.4f, 1f);
        [SerializeField] private Color  _failedColor     = new(1f, 0.3f, 0.3f, 1f);

        [Inject] private IAgentStateService _agentState;

        private LoopNodeUI _currentActiveNode;

        private void Start()
        {
            if (_agentState == null) return;

            // 초기 상태: 모든 노드 비활성
            ResetAllNodes();

            if (_sessionIdText != null)
                _sessionIdText.text = _targetSessionId;

            // 상태 변경 구독
            _agentState.OnStateChanged
                .Where(e => e.SessionId == _targetSessionId || string.IsNullOrEmpty(e.SessionId))
                .Subscribe(e => OnStateChanged(e.State))
                .AddTo(this);
        }

        private void OnStateChanged(AgentActionType state)
        {
            ResetAllNodes();

            switch (state)
            {
                case AgentActionType.Thinking:
                    ActivateNode(_thinkingNode, "사고 중...");
                    break;

                case AgentActionType.Planning:
                    ActivateNode(_planningNode, "계획 수립 중...");
                    HighlightConnection(0); // thinking → planning
                    break;

                case AgentActionType.TaskStarted:
                case AgentActionType.Executing:
                    ActivateNode(_executingNode, "실행 중...");
                    HighlightConnection(0);
                    HighlightConnection(1); // planning → executing
                    break;

                case AgentActionType.Reviewing:
                    ActivateNode(_reviewingNode, "결과 검토 중...");
                    HighlightConnection(0);
                    HighlightConnection(1);
                    HighlightConnection(2); // executing → reviewing
                    break;

                case AgentActionType.TaskCompleted:
                    ActivateNode(_completedNode, "작업 완료!", _completedColor);
                    HighlightAllConnections();
                    break;

                case AgentActionType.TaskFailed:
                    ActivateNode(_completedNode, "작업 실패", _failedColor);
                    break;

                case AgentActionType.Idle:
                case AgentActionType.Disconnected:
                    if (_statusText != null)
                        _statusText.text = state == AgentActionType.Disconnected
                            ? "연결 끊김" : "대기 중";
                    break;
            }
        }

        private void ActivateNode(LoopNodeUI node, string statusMessage, Color? overrideColor = null)
        {
            if (node == null) return;

            _currentActiveNode = node;
            node.SetActive(true, overrideColor ?? _activeColor);

            if (_statusText != null)
                _statusText.text = statusMessage;
        }

        private void HighlightConnection(int index)
        {
            if (_connectionLines == null || index >= _connectionLines.Length) return;
            if (_connectionLines[index] != null)
                _connectionLines[index].color = _activeColor;
        }

        private void HighlightAllConnections()
        {
            if (_connectionLines == null) return;
            foreach (var line in _connectionLines)
            {
                if (line != null) line.color = _completedColor;
            }
        }

        private void ResetAllNodes()
        {
            _thinkingNode?.SetActive(false, _inactiveColor);
            _planningNode?.SetActive(false, _inactiveColor);
            _executingNode?.SetActive(false, _inactiveColor);
            _reviewingNode?.SetActive(false, _inactiveColor);
            _completedNode?.SetActive(false, _inactiveColor);

            if (_connectionLines != null)
            {
                foreach (var line in _connectionLines)
                {
                    if (line != null) line.color = _inactiveColor;
                }
            }
        }
    }

    /// <summary>루프 노드 UI 요소 (Inspector에서 연결)</summary>
    [System.Serializable]
    public class LoopNodeUI
    {
        public Image   Background;
        public Image   Icon;
        public TMP_Text Label;

        public void SetActive(bool active, Color color)
        {
            if (Background != null) Background.color = color;
            if (Icon != null)       Icon.color = active ? Color.white : new Color(1, 1, 1, 0.3f);
            if (Label != null)      Label.color = active ? Color.white : new Color(1, 1, 1, 0.4f);
        }
    }
}
