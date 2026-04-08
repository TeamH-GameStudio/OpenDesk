using Cysharp.Threading.Tasks;
using OpenDesk.Claude;
using OpenDesk.Claude.Models;
using TMPro;
using UnityEngine;

namespace OpenDesk.Presentation.UI
{
    /// <summary>
    /// 에이전트 머리 위 생각 말풍선 (World Space Canvas).
    /// agent_thinking 수신 시 표시, delta/idle 시 숨김.
    /// </summary>
    public class ThinkingBubbleController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Canvas _worldCanvas;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _thinkingText;
        [SerializeField] private GameObject _bubbleRoot;

        [Header("설정")]
        [SerializeField] private float _yOffset = 2.2f;
        [SerializeField] private float _fadeInDuration = 0.3f;
        [SerializeField] private float _fadeOutDuration = 0.5f;
        [SerializeField] private int _maxDisplayChars = 80;

        [Header("WebSocket")]
        [SerializeField] private ClaudeWebSocketClient _wsClient;

        private Transform _followTarget;
        private string _currentAgentId;
        private bool _isVisible;

        // ── 초기화 ─────────────────────────────────────────

        private void Start()
        {
            if (_bubbleRoot != null)
                _bubbleRoot.SetActive(false);

            if (_canvasGroup != null)
                _canvasGroup.alpha = 0;

            if (_wsClient != null)
            {
                _wsClient.OnAgentThinking += HandleThinking;
                _wsClient.OnAgentDelta += HandleDelta;
                _wsClient.OnAgentState += HandleState;
            }
        }

        private void OnDestroy()
        {
            if (_wsClient != null)
            {
                _wsClient.OnAgentThinking -= HandleThinking;
                _wsClient.OnAgentDelta -= HandleDelta;
                _wsClient.OnAgentState -= HandleState;
            }
        }

        // ── 외부 API ───────────────────────────────────────

        /// <summary>추적할 에이전트 설정</summary>
        public void SetTarget(Transform target, string agentId)
        {
            _followTarget = target;
            _currentAgentId = agentId;
        }

        public void Show(string thinkingText)
        {
            if (_bubbleRoot == null) return;

            var display = thinkingText?.Length > _maxDisplayChars
                ? thinkingText[.._maxDisplayChars] + "..."
                : thinkingText;

            if (_thinkingText != null)
                _thinkingText.SetText(display ?? "");

            if (!_isVisible)
            {
                _bubbleRoot.SetActive(true);
                FadeIn().Forget();
                _isVisible = true;
            }
        }

        public void Hide()
        {
            if (!_isVisible) return;
            _isVisible = false;
            FadeOut().Forget();
        }

        // ── 이벤트 핸들러 ──────────────────────────────────

        private void HandleThinking(AgentThinkingMessage msg)
        {
            if (!string.IsNullOrEmpty(_currentAgentId) && msg.agent_id != _currentAgentId) return;
            Show(msg.thinking);
        }

        private void HandleDelta(AgentDeltaMessage msg)
        {
            // 응답 시작 시 생각 말풍선 숨김
            if (!string.IsNullOrEmpty(_currentAgentId) && msg.agent_id != _currentAgentId) return;
            Hide();
        }

        private void HandleState(AgentStateMessage msg)
        {
            if (!string.IsNullOrEmpty(_currentAgentId) && msg.agent_id != _currentAgentId) return;

            // idle이면 즉시 숨김
            if (msg.state == "idle")
                Hide();
        }

        // ── 위치 추적 + 빌보드 ─────────────────────────────

        private void LateUpdate()
        {
            if (_followTarget == null || _worldCanvas == null) return;

            transform.position = _followTarget.position + Vector3.up * _yOffset;

            // 카메라 빌보드
            var cam = UnityEngine.Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        // ── 페이드 ─────────────────────────────────────────

        private async UniTaskVoid FadeIn()
        {
            if (_canvasGroup == null) return;
            var elapsed = 0f;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeInDuration);
                await UniTask.Yield(destroyCancellationToken);
            }
            _canvasGroup.alpha = 1f;
        }

        private async UniTaskVoid FadeOut()
        {
            if (_canvasGroup == null) return;
            var elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / _fadeOutDuration);
                await UniTask.Yield(destroyCancellationToken);
            }
            _canvasGroup.alpha = 0f;
            if (_bubbleRoot != null) _bubbleRoot.SetActive(false);
        }
    }
}
