using System;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.Character.Context;
using OpenDesk.Presentation.Character.States;
using R3;
using UnityEngine;
using UnityEngine.AI;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 에이전트 캐릭터 MonoBehaviour.
    ///
    /// 역할:
    ///   1. IAgentStateService 구독 → 상태 변화 수신
    ///   2. AgentStateMachine에 전환 명령
    ///   3. NavMeshAgent 기반 이동
    ///   4. 채팅 상태 (ChatDelta/ChatFinal) 연동
    ///
    /// 확장 포인트:
    ///   - AgentCharacterContext.Expression: 표정/이펙트
    ///   - 새 State 추가: FSM에 AddState + 전환 매핑
    /// </summary>
    public class AgentCharacterController : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────
        [Header("에이전트 정보")]
        [SerializeField] private string _sessionId = "main";
        [SerializeField] private string _agentName = "팀장";

        [Header("오프라인 비주얼")]
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private float _offlineAlpha = 0.3f;

        [Header("네비게이션")]
        [SerializeField] private float _navSpeed = 1.5f;
        [SerializeField] private float _navAngularSpeed = 360f;
        [SerializeField] private float _navStoppingDistance = 0.3f;

        // ── DI ───────────────────────────────────────────────────────────────
        private IAgentStateService _agentStateService;

        [Inject]
        public void Construct(IAgentStateService agentStateService)
        {
            _agentStateService = agentStateService;
        }

        // ── 내부 ─────────────────────────────────────────────────────────────
        private AgentStateMachine _fsm;
        private AgentCompletedState _completedState;
        private IDisposable _subscription;
        private NavMeshAgent _navAgent;

        public string SessionId => _sessionId;
        public string AgentName => _agentName;

        private bool _initialized;

        /// <summary>외부에서 세션/이름 설정 후 FSM 초기화 (Spawner에서 호출)</summary>
        public void SetIdentity(string sessionId, string agentName)
        {
            _sessionId = sessionId;
            _agentName = agentName;

            // SetIdentity 이후 FSM 초기화 (Awake 타이밍 문제 회피)
            InitializeFSM();
        }

        // ── 초기화 ───────────────────────────────────────────────────────────

        private void Awake()
        {
            SetupNavMeshAgent();
        }

        private void Start()
        {
            // SetIdentity가 호출 안 된 경우 (Inspector 기본값으로 초기화)
            if (!_initialized)
                InitializeFSM();
        }

        private void InitializeFSM()
        {
            if (_initialized) return;
            _initialized = true;

            var animator = GetComponentInChildren<Animator>();
            var animCtrl = new UnityAnimatorController(animator, _agentName);
            var ctx = new AgentCharacterContext(
                animCtrl, _sessionId, _agentName,
                _navAgent, transform);

            BuildFSM(ctx);

            // IAgentStateService 구독
            if (_agentStateService != null)
            {
                _subscription = _agentStateService.OnStateChanged
                    .Where(e => e.SessionId == _sessionId)
                    .Subscribe(e => OnAgentStateChanged(e.State));
            }

            Debug.Log($"[{_agentName}] FSM 초기화 완료 (sessionId={_sessionId})");
        }

        private void SetupNavMeshAgent()
        {
            _navAgent = GetComponent<NavMeshAgent>();
            if (_navAgent == null)
                _navAgent = gameObject.AddComponent<NavMeshAgent>();

            _navAgent.speed = _navSpeed;
            _navAgent.angularSpeed = _navAngularSpeed;
            _navAgent.stoppingDistance = _navStoppingDistance;
            _navAgent.radius = 0.3f;
            _navAgent.height = 1.8f;
            _navAgent.baseOffset = 0f;
            _navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            _navAgent.autoTraverseOffMeshLink = true;
        }

        // ── FSM 구성 ─────────────────────────────────────────────────────────

        private void BuildFSM(AgentCharacterContext ctx)
        {
            _fsm = new AgentStateMachine();

            var idleState = new AgentIdleState(ctx);
            var typingState = new AgentTypingState(ctx);
            var thinkingState = new AgentThinkingState(ctx);
            _completedState = new AgentCompletedState(ctx);
            var disconnectedState = new AgentDisconnectedState(ctx);
            var chattingState = new AgentChattingState(ctx);

            // 완료 모션 끝 → Idle 자동 복귀
            _completedState.OnCompletionAnimDone += () => _fsm.TransitionTo<AgentIdleState>();

            _fsm.AddState(idleState);
            _fsm.AddState(typingState);
            _fsm.AddState(thinkingState);
            _fsm.AddState(_completedState);
            _fsm.AddState(disconnectedState);
            _fsm.AddState(chattingState);

            _fsm.Initialize<AgentIdleState>();

            _fsm.OnStateChanged += (from, to) =>
                Debug.Log($"[{_agentName}] FSM: {from} -> {to}");
        }

        // ── 이벤트 → FSM 전환 ────────────────────────────────────────────────

        private void OnAgentStateChanged(AgentActionType state)
        {
            switch (state)
            {
                case AgentActionType.Idle:
                    _fsm.TransitionTo<AgentIdleState>();
                    break;

                // 채팅 스트리밍 → Chatting (Typing 모션)
                case AgentActionType.ChatDelta:
                    _fsm.TransitionTo<AgentChattingState>();
                    break;

                // 채팅 완료 → Completed (Cheering)
                case AgentActionType.ChatFinal:
                    _fsm.TransitionTo<AgentCompletedState>();
                    break;

                // AI 사고 중
                case AgentActionType.Thinking:
                case AgentActionType.Planning:
                    _fsm.TransitionTo<AgentThinkingState>();
                    break;

                // 도구 사용 / 실행 중 → Typing
                case AgentActionType.Executing:
                case AgentActionType.ToolUsing:
                case AgentActionType.ToolResult:
                case AgentActionType.TaskStarted:
                    _fsm.TransitionTo<AgentTypingState>();
                    break;

                case AgentActionType.TaskCompleted:
                    _fsm.TransitionTo<AgentCompletedState>();
                    break;

                case AgentActionType.TaskFailed:
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

        /// <summary>외부에서 직접 상태 전환 — ChatPanelController 등에서 호출</summary>
        public void ForceState(AgentActionType actionType)
        {
            OnAgentStateChanged(actionType);
        }

        // ── Unity 루프 ───────────────────────────────────────────────────────

        private void Update()
        {
            _fsm?.Update(Time.deltaTime);

            // NavMeshAgent 이동 시 Walk 애니메이션 자동 동기화
            SyncWalkAnimation();
        }

        /// <summary>NavAgent 속도에 따라 Animator Speed 보정</summary>
        private void SyncWalkAnimation()
        {
            if (_navAgent == null) return;
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;

            animator.SetFloat("MoveSpeed", _navAgent.velocity.magnitude);
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
        [ContextMenu("Test: Idle")]
        private void DbgIdle() => OnAgentStateChanged(AgentActionType.Idle);
        [ContextMenu("Test: Chatting")]
        private void DbgChat() => OnAgentStateChanged(AgentActionType.ChatDelta);
        [ContextMenu("Test: Done")]
        private void DbgDone() => OnAgentStateChanged(AgentActionType.ChatFinal);
        [ContextMenu("Test: Typing")]
        private void DbgTyping() => OnAgentStateChanged(AgentActionType.TaskStarted);
        [ContextMenu("Test: Thinking")]
        private void DbgThink() => OnAgentStateChanged(AgentActionType.Thinking);
#endif
    }
}
