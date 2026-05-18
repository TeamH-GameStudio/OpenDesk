using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace OpenDesk.Characters.Talking
{
    /// <summary>
    /// API 스트림이 burst 로 토큰을 던져도 캐릭터별 char 큐로 다시 흘려보내
    /// 자연스러운 타이핑 효과를 만드는 컴포넌트.
    ///
    /// 정책:
    ///   - <see cref="Enqueue"/> — 들어오는 chunk 를 1글자씩 큐에 push
    ///   - Update 에서 _msPerChar 간격으로 1글자씩 TMP_Text 에 append
    ///   - 큐가 burst threshold 를 넘으면 자동 가속 (UI 가 영원히 못 따라잡는 사태 방지)
    ///   - <see cref="Flush"/> — 즉시 모두 출력 (talking_stop 이후 잔여 처리)
    ///   - <see cref="Clear"/> — 새 발화 시작 시 누적 텍스트/큐 초기화
    ///
    /// 단독 테스트 가능. 어떤 외부 컴포넌트(WardrobeApplier/AgentTalkingController)에도 의존하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreamingTextBuffer : MonoBehaviour
    {
        [Header("출력 대상")]
        [Tooltip("토큰을 append 할 TMP_Text. 비워두면 GetComponentInChildren 으로 자동 탐색.")]
        [SerializeField] private TMP_Text _target;

        [Header("페이싱")]
        [Tooltip("char 1글자를 출력하는 데 걸리는 시간 (ms). 30~50 권장.")]
        [SerializeField, Range(10f, 100f)] private float _msPerChar = 35f;

        [Tooltip("큐가 이 길이를 초과하면 페이스를 절반으로 줄여 가속한다 (UI 가 영원히 못 따라잡는 사태 방지).")]
        [SerializeField, Range(20, 1000)] private int _burstThresholdChars = 100;

        [Tooltip("burst 모드 진입 시 적용할 페이스 배율 (0.5 = 2배 속도). 0.1 ~ 1.0 사이.")]
        [SerializeField, Range(0.1f, 1f)] private float _burstPaceMultiplier = 0.5f;

        // ── 내부 상태 ──────────────────────────────────────────
        private readonly Queue<char> _queue = new();
        private readonly StringBuilder _accumulated = new();
        private float _msAccum;

        // ── 공개 프로퍼티 ──────────────────────────────────────

        /// <summary>큐가 비어있고 append 작업이 끝나 있으면 true.</summary>
        public bool IsIdle => _queue.Count == 0;

        /// <summary>지금까지 append 된 누적 텍스트 (빈 문자열 가능).</summary>
        public string Accumulated => _accumulated.ToString();

        /// <summary>큐에 남아있는 미출력 char 수.</summary>
        public int PendingChars => _queue.Count;

        // ── 이벤트 ────────────────────────────────────────────

        /// <summary>큐가 비어 idle 로 전환되는 순간 1회 발화. <see cref="AgentTalkingController"/> 가 입모양 종료 타이밍에 사용.</summary>
        public event Action OnIdle;

        /// <summary>
        /// char 가 append 될 때마다 현재 누적 텍스트로 발화.
        /// TMP_Text 외 ViewModel/UI Toolkit 등 다른 출력 surface 에 흘려보낼 때 사용.
        /// (예: ChatPanelView 가 _streamingMessage VM 본문 갱신용으로 구독)
        /// </summary>
        public event Action<string> OnAppended;

        // ── 생명주기 ──────────────────────────────────────────

        private void Awake()
        {
            if (_target == null)
                _target = GetComponentInChildren<TMP_Text>(includeInactive: true);
        }

        private void Update()
        {
            if (_queue.Count == 0) return;

            _msAccum += Time.unscaledDeltaTime * 1000f;
            float pace = EffectivePace;
            if (pace <= 0f) pace = 1f;

            // 한 프레임에 누적된 만큼 한 번에 emit — 60fps 라도 큐 길어지면 여러 글자 출력
            int emit = Mathf.FloorToInt(_msAccum / pace);
            if (emit <= 0) return;

            for (int i = 0; i < emit && _queue.Count > 0; i++)
                AppendChar(_queue.Dequeue());

            _msAccum -= emit * pace;
            if (_queue.Count == 0)
            {
                _msAccum = 0f;
                OnIdle?.Invoke();
            }
        }

        // ── 공개 API ──────────────────────────────────────────

        /// <summary>토큰 청크를 char 단위로 큐에 push. 빈 문자열은 무시.</summary>
        public void Enqueue(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            for (int i = 0; i < delta.Length; i++)
                _queue.Enqueue(delta[i]);
        }

        /// <summary>큐에 남은 모든 char 를 즉시 append. talking_stop 이후 잔여 처리에 사용.</summary>
        public void Flush()
        {
            if (_queue.Count == 0) return;
            while (_queue.Count > 0)
                AppendChar(_queue.Dequeue());
            _msAccum = 0f;
            OnIdle?.Invoke();
        }

        /// <summary>새 발화 시작 시 누적 텍스트 + 큐 초기화. TMP_Text 도 비운다.</summary>
        public void Clear()
        {
            _queue.Clear();
            _accumulated.Clear();
            _msAccum = 0f;
            if (_target != null)
                _target.SetText(string.Empty);
        }

        /// <summary>
        /// 외부 누적 텍스트와 sync. 진행 중 응답을 다른 컨텍스트(예: 패널 Close→Open)에서
        /// 복구할 때 raw 텍스트로 한 번 정렬한 뒤 이후 delta 부터는 정상적으로 페이싱한다.
        /// 큐는 비우고, _accumulated 를 주어진 텍스트로 교체한다. <see cref="OnAppended"/>는 emit 하지 않는다 (호출자가 직접 본문 갱신).
        /// </summary>
        public void Reset(string accumulated)
        {
            _queue.Clear();
            _accumulated.Clear();
            if (!string.IsNullOrEmpty(accumulated))
                _accumulated.Append(accumulated);
            _msAccum = 0f;
            if (_target != null)
                _target.SetText(_accumulated);
        }

        /// <summary>TMP_Text 외부 교체용 (런타임 prefab 변경 등).</summary>
        public void SetTarget(TMP_Text target)
        {
            _target = target;
        }

        // ── 내부 ──────────────────────────────────────────────

        private void AppendChar(char c)
        {
            _accumulated.Append(c);
            // _target 이 할당된 경우 직접 SetText (캐릭터 prefab 시나리오).
            // _target 이 null 이어도 OnAppended 구독자가 있으면 그쪽으로 흘러간다 (ChatPanelView 등).
            if (_target != null)
                _target.SetText(_accumulated);
            // 누적 텍스트로 발화 — 구독자가 매번 전체 본문을 받게 하면 idempotent 갱신이 가능.
            // (UI Toolkit Label.text 같은 setter 가 항상 전체 문자열을 받는 패턴과 호환)
            OnAppended?.Invoke(_accumulated.ToString());
        }

        private float EffectivePace
            => _queue.Count > _burstThresholdChars
                ? _msPerChar * _burstPaceMultiplier
                : _msPerChar;

#if UNITY_EDITOR
        [ContextMenu("Test: Enqueue '안녕하세요!' 10회")]
        private void DbgEnqueue()
        {
            for (int i = 0; i < 10; i++)
                Enqueue("안녕하세요! ");
        }

        [ContextMenu("Test: Flush")]
        private void DbgFlush() => Flush();

        [ContextMenu("Test: Clear")]
        private void DbgClear() => Clear();
#endif
    }
}
