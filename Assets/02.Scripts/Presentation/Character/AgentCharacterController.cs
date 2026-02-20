using System;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.Character.Context;
using OpenDesk.Presentation.Character.States;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 에이전트 캐릭터 MonoBehaviour
    ///
    /// 역할:
    ///   1. IAgentStateService 구독 → 상태 변화 수신
    ///   2. AgentStateMachine에 전환 명령
    ///   3. Unity Update에서 FSM 구동
    ///
    /// 결합도:
    ///   - IAgentStateService 인터페이스만 참조 (코어 서비스 직접 의존 없음)
    ///   - IAnimationController로 애니메이션 추상화 (ProjectH 재사용)
    /// </summary>
    public class AgentCharacterController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────
        [Header("에이전트 정보")]
        [SerializeField] private string _sessionId = "main";
        [SerializeField] private string _agentName = "팀장";

        [Header("오프라인 비주얼")]
        [SerializeField] private Renderer[] _renderers;    // 오프라인 시 반투명 처리
        [SerializeField] private float _offlineAlpha = 0.3f;

        // ── DI ───────────────────────────────────────────────────────────────
        [Inject] private IAgentStateService _agentStateService;

        // ── 내부 ─────────────────────────────────────────────────────────────
        private AgentStateMachine _fsm;
        private AgentCompletedState _completedState;
        private IDisposable _subscription;

        // ── 초기화 ───────────────────────────────────────────────────────────

        private void Awake()
        {
            var animator   = GetComponent<Animator>();
            var animCtrl   = new UnityAnimatorController(animator, _agentName);
            var ctx        = new AgentCharacterContext(animCtrl, _sessionId, _agentName);

            BuildFSM(ctx);
        }

        private void Start()
        {
            // IAgentStateService 구독 — 해당 세션 이벤트만 필터링
            _subscription = _agentStateService.OnStateChanged
                .Where(e => e.SessionId == _sessionId)
                .Subscribe(e => OnAgentStateChanged(e.State));
        }

        // ── FSM 구성 ─────────────────────────────────────────────────────────

        private void BuildFSM(AgentCharacterContext ctx)
        {
            _fsm = new AgentStateMachine();

            var idleState         = new AgentIdleState(ctx);
            var typingState       = new AgentTypingState(ctx);
            var thinkingState     = new AgentThinkingState(ctx);
            _completedState       = new AgentCompletedState(ctx);
            var disconnectedState = new AgentDisconnectedState(ctx);

            // 완료 모션 끝 → Idle 자동 복귀
            _completedState.OnCompletionAnimDone += () => _fsm.TransitionTo<AgentIdleState>();

            _fsm.AddState(idleState);
            _fsm.AddState(typingState);
            _fsm.AddState(thinkingState);
            _fsm.AddState(_completedState);
            _fsm.AddState(disconnectedState);

            _fsm.Initialize<AgentIdleState>();

            // 상태 전환 로그
            _fsm.OnStateChanged += (from, to) =>
                Debug.Log($"[{_agentName}] FSM: {from} → {to}");
        }

        // ── 이벤트 → FSM 전환 ────────────────────────────────────────────────

        private void OnAgentStateChanged(AgentActionType state)
        {
            switch (state)
            {
                case AgentActionType.Idle:
                    _fsm.TransitionTo<AgentIdleState>();
                    break;

                case AgentActionType.TaskStarted:
                    _fsm.TransitionTo<AgentTypingState>();
                    break;

                case AgentActionType.Thinking:
                    _fsm.TransitionTo<AgentThinkingState>();
                    break;

                case AgentActionType.TaskCompleted:
                    _fsm.TransitionTo<AgentCompletedState>();
                    break;

                case AgentActionType.TaskFailed:
                    // 실패도 완료 모션 (다른 리액션으로 교체 가능)
                    _fsm.TransitionTo<AgentCompletedState>();
                    break;

                case AgentActionType.Disconnected:
                    _fsm.TransitionTo<AgentDisconnectedState>();
                    SetOfflineVisual(true);
                    break;

                case AgentActionType.Connected:
                    _fsm.TransitionTo<AgentIdleState>();
                    SetOfflineVisual(false);
                    break;
            }
        }

        // ── Unity 루프 ───────────────────────────────────────────────────────

        private void Update()
        {
            _fsm?.Update(Time.deltaTime);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
            _fsm?.Cleanup();
        }

        // ── 오프라인 비주얼 ──────────────────────────────────────────────────

        private void SetOfflineVisual(bool isOffline)
        {
            if (_renderers == null) return;

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var color = r.material.color;
                color.a = isOffline ? _offlineAlpha : 1f;
                r.material.color = color;
            }
        }

        // ── 디버그 ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Test: Typing")]
        private void DbgTyping()  => OnAgentStateChanged(AgentActionType.TaskStarted);
        [ContextMenu("Test: Done")]
        private void DbgDone()    => OnAgentStateChanged(AgentActionType.TaskCompleted);
        [ContextMenu("Test: Idle")]
        private void DbgIdle()    => OnAgentStateChanged(AgentActionType.Idle);
#endif
    }
}
