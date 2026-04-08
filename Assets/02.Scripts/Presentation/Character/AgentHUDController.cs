using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// World Space HUD — 에이전트 머리 위에 이름 + 상태바를 표시.
    /// IAgentStateService 구독으로 실시간 상태 갱신.
    /// Billboard 처리로 항상 카메라를 향함.
    /// </summary>
    public class AgentHUDController : MonoBehaviour
    {
        [Header("HUD UI")]
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Slider _statusBar;
        [SerializeField] private Image _statusBarFill;

        [Header("버블 UI (상태 표시용)")]
        [SerializeField] private GameObject _bubbleRoot;
        [SerializeField] private TMP_Text _bubbleText;
        [SerializeField] private Image _bubbleBg;

        [Header("Settings")]
        [SerializeField] private float _pulseSpeed = 1.5f;

        // ── 상태 ────────────────────────────────────────────
        private string _sessionId;
        private Color _hudColor;
        private AgentActionType _currentAction = AgentActionType.Idle;
        private bool _isPulsing;
        private UnityEngine.Camera _mainCamera;
        private IAgentStateService _stateService;
        private System.IDisposable _subscription;

        // ── 상태 → 표시 매핑 ────────────────────────────────

        private static readonly Color ColIdle       = new Color32(150, 150, 150, 255);
        private static readonly Color ColThinking   = new Color32(255, 235, 59,  255);
        private static readonly Color ColPlanning   = new Color32(255, 152, 0,   255);
        private static readonly Color ColExecuting  = new Color32(66,  165, 245, 255);
        private static readonly Color ColReviewing  = new Color32(171, 71,  188, 255);
        private static readonly Color ColTool       = new Color32(0,   188, 212, 255);
        private static readonly Color ColChat       = new Color32(102, 187, 106, 255);
        private static readonly Color ColComplete   = new Color32(76,  175, 80,  255);
        private static readonly Color ColError      = new Color32(244, 67,  54,  255);
        private static readonly Color ColDisconnect = new Color32(244, 67,  54,  255);

        // ================================================================
        //  초기화
        // ================================================================

        /// <summary>
        /// AgentSpawner에서 호출 — 프로필 + 서비스 주입
        /// </summary>
        public void Initialize(AgentProfileSO profile, IAgentStateService stateService = null)
        {
            _sessionId = profile.SessionId;
            _hudColor = profile.HudColor;
            _stateService = stateService;
            _mainCamera = UnityEngine.Camera.main;

            if (_nameText != null)
            {
                _nameText.text = profile.AgentName;
                _nameText.color = _hudColor;
            }

            ApplyState(AgentActionType.Idle);

            // IAgentStateService 구독
            if (_stateService != null)
            {
                _subscription = _stateService.OnStateChanged
                    .Where(e => e.SessionId == _sessionId)
                    .Subscribe(e => ApplyState(e.State));
            }
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        // ================================================================
        //  매 프레임
        // ================================================================

        private void LateUpdate()
        {
            // Billboard — 항상 카메라를 향함
            if (_mainCamera != null)
                transform.forward = _mainCamera.transform.forward;

            // 펄스 애니메이션
            if (_isPulsing && _statusBar != null)
                _statusBar.value = 0.3f + 0.4f * Mathf.PingPong(Time.time * _pulseSpeed, 1f);
        }

        // ================================================================
        //  상태 적용
        // ================================================================

        public void ApplyState(AgentActionType action)
        {
            _currentAction = action;

            var (text, color, fillValue, pulse) = GetDisplayInfo(action);

            if (_statusText != null)
                _statusText.text = text;

            if (_statusBarFill != null)
                _statusBarFill.color = color;

            _isPulsing = pulse;

            if (!pulse && _statusBar != null)
                _statusBar.value = fillValue;

            // 버블 모드: Idle/Connected 이외 → HUD 숨기고 버블 표시
            var showBubble = action != AgentActionType.Idle
                          && action != AgentActionType.Connected;
            SetBubbleMode(showBubble, text, color);
        }

        /// <summary>버블 모드 전환 — HUD 바 숨기고 말풍선 표시</summary>
        private void SetBubbleMode(bool showBubble, string text, Color color)
        {
            // HUD 요소 숨기기/보이기
            if (_nameText != null) _nameText.gameObject.SetActive(!showBubble);
            if (_statusBar != null) _statusBar.gameObject.SetActive(!showBubble);
            if (_statusText != null && !showBubble) _statusText.gameObject.SetActive(true);

            // 버블 표시
            if (_bubbleRoot != null)
            {
                _bubbleRoot.SetActive(showBubble);
                if (showBubble)
                {
                    if (_bubbleText != null)
                        _bubbleText.text = text;
                    if (_bubbleBg != null)
                        _bubbleBg.color = new Color(color.r, color.g, color.b, 0.85f);
                }
            }
        }

        /// <summary>생각/도구 사용 등 상세 텍스트를 버블에 표시</summary>
        public void ShowBubbleMessage(string message)
        {
            if (_bubbleRoot != null) _bubbleRoot.SetActive(true);
            if (_bubbleText != null) _bubbleText.text = message;
            if (_nameText != null) _nameText.gameObject.SetActive(false);
            if (_statusBar != null) _statusBar.gameObject.SetActive(false);
        }

        /// <summary>버블 숨기고 HUD 복원</summary>
        public void HideBubble()
        {
            if (_bubbleRoot != null) _bubbleRoot.SetActive(false);
            if (_nameText != null) _nameText.gameObject.SetActive(true);
            if (_statusBar != null) _statusBar.gameObject.SetActive(true);
        }

        /// <summary>디버그/테스트용 — 외부에서 직접 상태 설정</summary>
        public void ForceState(AgentActionType action) => ApplyState(action);

        /// <summary>FSM 서브상태용 — 상태 텍스트만 직접 변경</summary>
        public void ForceStatusText(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
        }

        public AgentActionType CurrentAction => _currentAction;

        // ================================================================
        //  매핑 테이블
        // ================================================================

        private static (string text, Color color, float fill, bool pulse) GetDisplayInfo(AgentActionType action)
        {
            return action switch
            {
                AgentActionType.Idle            => ("대기 중",           ColIdle,       0f,    false),
                AgentActionType.Thinking        => ("생각 중...",        ColThinking,   0f,    true),
                AgentActionType.Planning        => ("계획 수립 중...",   ColPlanning,   0f,    true),
                AgentActionType.Executing       => ("실행 중...",        ColExecuting,  0f,    true),
                AgentActionType.Reviewing       => ("검토 중...",        ColReviewing,  0f,    true),
                AgentActionType.ToolUsing       => ("도구 호출 중...",   ColTool,       0.5f,  false),
                AgentActionType.ToolResult      => ("도구 결과 수신",    ColTool,       0.75f, false),
                AgentActionType.ChatDelta       => ("응답 중...",        ColChat,       0f,    true),
                AgentActionType.ChatFinal       => ("응답 완료",         ColComplete,   1f,    false),
                AgentActionType.TaskStarted     => ("작업 시작",         ColExecuting,  0.1f,  true),
                AgentActionType.TaskCompleted   => ("작업 완료!",        ColComplete,   1f,    false),
                AgentActionType.TaskFailed      => ("오류 발생",         ColError,      1f,    false),
                AgentActionType.Connected       => ("연결됨",            ColComplete,   0f,    false),
                AgentActionType.Disconnected    => ("연결 끊김",         ColDisconnect, 0f,    false),
                AgentActionType.SubAgentSpawned => ("서브에이전트 생성", ColExecuting,  0.3f,  true),
                _                               => ("...",               ColIdle,       0f,    false),
            };
        }
    }
}
