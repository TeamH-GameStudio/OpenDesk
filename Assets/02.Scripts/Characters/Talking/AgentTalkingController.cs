using System.Collections;
using OpenDesk.Claude;
using OpenDesk.Presentation.Character;
using UnityEngine;
using VContainer;

namespace OpenDesk.Characters.Talking
{
    /// <summary>
    /// PROTOCOL.md 의 talking_start / text_delta / talking_stop 메시지를 받아
    /// 캐릭터의 <see cref="MouthAnimator"/> 를 트리거하는 슬림 라우터.
    ///
    /// <para><b>부착 방법 (Inspector 작업, 필수 = 2개):</b></para>
    /// <list type="number">
    ///   <item>에이전트 prefab 루트에 <c>AgentTalkingController</c> 컴포넌트 추가.</item>
    ///   <item>같은 GO 또는 자식에 <c>MouthAnimator</c> 추가:
    ///     <list type="bullet">
    ///       <item><c>_bodyRenderer</c> = WardrobeApplier 와 같은 body Renderer</item>
    ///       <item><c>_mouthMaterialIndex</c> = WardrobeApplier 와 동일 (기본 2)</item>
    ///       <item>closed/half/open 텍스처 3장 할당</item>
    ///     </list>
    ///   </item>
    ///   <item>SessionId 는 같은 GO 의 <see cref="AgentCharacterController"/> 에서 자동 차용 — 별도 입력 불필요.</item>
    ///   <item><c>ClaudeWebSocketClient</c> 는 VContainer DI 자동 주입. 미주입 시 Inspector <c>_wsClientFallback</c> 슬롯에 수동 할당 가능.</item>
    /// </list>
    ///
    /// <para><b>선택 (캐릭터 옆 텍스트 버블이 필요한 경우만):</b></para>
    /// <list type="bullet">
    ///   <item><c>StreamingTextBuffer</c> 를 같은 GO/자식에 추가 + TMP_Text 슬롯 할당. 비우면 캐릭터 측 버블 출력 없이 입모양만 동기화.</item>
    /// </list>
    /// (메인 채팅 패널 ChatPanelView 의 타이핑 페이싱은 별개 — ChatPanelView 가 자기 GO 에 StreamingTextBuffer 를 자동 부착해 동작.)
    ///
    /// <para><b>흐름:</b></para>
    /// <code>
    /// talking_start  → buffer.Clear() (있으면) + mouth.StartTalking()
    /// text_delta     → buffer.Enqueue(text) (버퍼 없으면 no-op)
    /// talking_stop:
    ///   complete    → (버퍼 있으면 idle 까지 대기) → mouth.StopTalking()
    ///   error|interrupted → 즉시 buffer.Flush() (있으면) + mouth.StopTalking()
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AgentTalkingController : MonoBehaviour
    {
        [Header("바인딩 (필수)")]
        [Tooltip("같은 GO/자식의 MouthAnimator. 비우면 자동 탐색.")]
        [SerializeField] private MouthAnimator _mouth;

        [Tooltip("AgentCharacterController. 비우면 자동 탐색. SessionId 추출에 사용.")]
        [SerializeField] private AgentCharacterController _character;

        [Header("바인딩 (선택 — 캐릭터 옆 텍스트 버블이 있을 때만)")]
        [Tooltip("같은 GO/자식의 StreamingTextBuffer. 비우면 자동 탐색하되, 없으면 text_delta 는 무시 (입모양만 동기화).")]
        [SerializeField] private StreamingTextBuffer _textBuffer;

        [Header("세션 매칭")]
        [Tooltip("필터링할 sessionId. 비워두면 _character.SessionId 자동 사용.")]
        [SerializeField] private string _sessionIdOverride = "";

        [Header("WebSocket (DI 미주입 시 fallback)")]
        [SerializeField] private ClaudeWebSocketClient _wsClientFallback;

        [Header("타임아웃")]
        [Tooltip("talking_stop(complete) 후 버퍼 flush 를 기다리는 최대 시간 (초). 초과 시 강제 flush.")]
        [SerializeField, Range(0.5f, 30f)] private float _completeFlushTimeoutSeconds = 8f;

        // ── DI ────────────────────────────────────────────────
        private ClaudeWebSocketClient _wsClient;

        [Inject]
        public void Construct(ClaudeWebSocketClient wsClient)
        {
            _wsClient = wsClient;
        }

        // ── 내부 ──────────────────────────────────────────────
        private bool _subscribed;
        private Coroutine _stopWaitCoroutine;

        private string ResolvedSessionId =>
            !string.IsNullOrEmpty(_sessionIdOverride)
                ? _sessionIdOverride
                : (_character != null ? _character.SessionId : "");

        // ── 생명주기 ──────────────────────────────────────────

        private void Awake()
        {
            if (_textBuffer == null) _textBuffer = GetComponentInChildren<StreamingTextBuffer>(includeInactive: true);
            if (_mouth == null)      _mouth      = GetComponentInChildren<MouthAnimator>(includeInactive: true);
            if (_character == null)  _character  = GetComponent<AgentCharacterController>()
                                                 ?? GetComponentInParent<AgentCharacterController>();
        }

        private void OnEnable()
        {
            // DI 가 늦게 주입되는 경우 (VContainer InjectGameObject 가 SetIdentity 보다 먼저 호출되지만
            // 안전망으로 fallback 도 검사) — _wsClient 가 null 이면 fallback 사용.
            if (_wsClient == null) _wsClient = _wsClientFallback;
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (_stopWaitCoroutine != null)
            {
                StopCoroutine(_stopWaitCoroutine);
                _stopWaitCoroutine = null;
            }
        }

        // ── 구독 ──────────────────────────────────────────────

        private void Subscribe()
        {
            if (_subscribed || _wsClient == null) return;
            _wsClient.OnTalkingStart += HandleTalkingStart;
            _wsClient.OnTextDelta    += HandleTextDelta;
            _wsClient.OnTalkingStop  += HandleTalkingStop;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _wsClient == null) return;
            _wsClient.OnTalkingStart -= HandleTalkingStart;
            _wsClient.OnTextDelta    -= HandleTextDelta;
            _wsClient.OnTalkingStop  -= HandleTalkingStop;
            _subscribed = false;
        }

        // ── 메시지 핸들러 ─────────────────────────────────────

        private void HandleTalkingStart(string sessionId, string agentId)
        {
            if (!IsForMe(sessionId)) return;

            // 진행 중인 stop-wait 가 있으면 취소 (이전 발화의 잔여 처리 무효화).
            if (_stopWaitCoroutine != null)
            {
                StopCoroutine(_stopWaitCoroutine);
                _stopWaitCoroutine = null;
            }

            _textBuffer?.Clear();
            _mouth?.StartTalking();
        }

        private void HandleTextDelta(string sessionId, string agentId, string text)
        {
            if (!IsForMe(sessionId)) return;
            _textBuffer?.Enqueue(text);
        }

        private void HandleTalkingStop(string sessionId, string agentId, string reason)
        {
            if (!IsForMe(sessionId)) return;

            if (reason == "error" || reason == "interrupted")
            {
                // 즉시 정리 — 잔여 토큰은 일단 모두 출력 후 입 닫기.
                _textBuffer?.Flush();
                _mouth?.StopTalking();
                return;
            }

            // complete — 버퍼 idle 까지 대기 후 mouth stop.
            // 큐가 이미 비어있으면 즉시 stop.
            if (_textBuffer == null || _textBuffer.IsIdle)
            {
                _mouth?.StopTalking();
                return;
            }

            if (_stopWaitCoroutine != null) StopCoroutine(_stopWaitCoroutine);
            _stopWaitCoroutine = StartCoroutine(WaitForBufferIdleThenStop());
        }

        private IEnumerator WaitForBufferIdleThenStop()
        {
            float elapsed = 0f;
            while (_textBuffer != null && !_textBuffer.IsIdle)
            {
                if (elapsed >= _completeFlushTimeoutSeconds)
                {
                    Debug.LogWarning(
                        $"[AgentTalkingController] {ResolvedSessionId}: flush timeout — 강제 flush.",
                        this);
                    _textBuffer.Flush();
                    break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _mouth?.StopTalking();
            _stopWaitCoroutine = null;
        }

        // ── 매칭 ──────────────────────────────────────────────

        private bool IsForMe(string sessionId)
        {
            var mine = ResolvedSessionId;
            if (string.IsNullOrEmpty(mine)) return false;
            // 미들웨어가 session_id 를 보내지 않는 경우 (구버전 호환) 모든 메시지 수락 — 단일 캐릭터 환경.
            if (string.IsNullOrEmpty(sessionId)) return true;
            return string.Equals(mine, sessionId, System.StringComparison.Ordinal);
        }
    }
}
