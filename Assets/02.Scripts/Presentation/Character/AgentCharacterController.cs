using System;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.Character.Context;
using OpenDesk.Presentation.Character.States;
using OpenDesk.SkillDiskette;
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
        [Tooltip("미들웨어 에이전트 ID (researcher/writer/analyst)")]
        [SerializeField] private string _agentId = "";

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
        private AgentErrorState _errorState;
        private IDisposable _subscription;
        private NavMeshAgent _navAgent;

        public string SessionId => _sessionId;
        public string AgentName => _agentName;
        /// <summary>미들웨어 에이전트 ID (researcher/writer/analyst)</summary>
        public string AgentId => _agentId;

        /// <summary>디스켓 장착 관리자 (같은 GameObject 또는 자식에 부착)</summary>
        public AgentEquipmentManager Equipment { get; private set; }

        /// <summary>에이전트 프로필 SO (Spawner에서 설정)</summary>
        public AgentProfileSO Profile { get; private set; }

        private bool _initialized;
        private AgentCharacterContext _ctx;

        /// <summary>카메라 포커스 상태 설정 — 포커스 중이면 배회 중지</summary>
        public void SetFocused(bool focused)
        {
            if (_ctx != null) _ctx.IsFocused = focused;
        }

        /// <summary>프로필 SO 설정 (Spawner에서 호출)</summary>
        public void SetProfile(AgentProfileSO profile) => Profile = profile;

        /// <summary>외부에서 세션/이름/에이전트ID 설정 후 FSM 초기화 (Spawner에서 호출)</summary>
        public void SetIdentity(string sessionId, string agentName, string agentId = "")
        {
            _sessionId = sessionId;
            _agentName = agentName;
            if (!string.IsNullOrEmpty(agentId))
                _agentId = agentId;

            // Instantiate 직후에는 Animator Controller가 아직 로드 안 됐을 수 있음
            // Start()에서 초기화하도록 플래그만 설정
            _identitySet = true;
        }

        private bool _identitySet;

        // ── 초기화 ───────────────────────────────────────────────────────────

        private void Awake()
        {
            // 스케일 보존 (NavMeshAgent 추가 시 리셋 방지)
            var savedScale = transform.localScale;

            SetupNavMeshAgent();

            // 스케일 복원
            transform.localScale = savedScale;

            // EquipmentManager 탐색 (같은 GO 또는 자식)
            Equipment = GetComponent<AgentEquipmentManager>();
            if (Equipment == null)
                Equipment = GetComponentInChildren<AgentEquipmentManager>();
            if (Equipment == null)
                Equipment = gameObject.AddComponent<AgentEquipmentManager>();
        }

        private void Start()
        {
            // SetIdentity가 호출됐든 안 됐든 Start에서 초기화
            // (Instantiate 직후 Animator Controller 로드 타이밍 보장)
            if (!_initialized)
                InitializeFSM();
        }

        private void InitializeFSM()
        {
            if (_initialized) return;
            _initialized = true;

            var animator = GetComponentInChildren<Animator>();

            // Controller가 null이면 Resources 또는 직접 경로에서 로드 시도
            if (animator != null && animator.runtimeAnimatorController == null)
            {
                var ctrl = Resources.Load<RuntimeAnimatorController>("AgentAnimatorController");
                if (ctrl == null)
                {
                    // Addressables/Resources에 없으면 에디터에서만 로드
#if UNITY_EDITOR
                    ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/05.Prefabs/Agent/AgentAnimatorController.controller");
#endif
                }
                if (ctrl != null)
                {
                    animator.runtimeAnimatorController = ctrl;
                    Debug.Log($"[{_agentName}] Animator Controller 런타임 할당 완료");
                }
                else
                {
                    Debug.LogError($"[{_agentName}] Animator Controller를 찾을 수 없습니다!");
                }
            }

            var animCtrl = new UnityAnimatorController(animator, _agentName);
            _ctx = new AgentCharacterContext(
                animCtrl, _sessionId, _agentName, _agentId,
                _navAgent, transform);

            // FaceSwapController 연결 (IExpressionController 구현체)
            var faceSwap = GetComponentInChildren<FaceSwapController>();
            if (faceSwap != null)
                _ctx.Expression = faceSwap;

            // HUD 상태 텍스트 콜백 연결
            var hud = GetComponentInChildren<AgentHUDController>();
            if (hud != null)
            {
                _ctx.OnHUDStatusChanged = (text) =>
                {
                    var so = hud;
                    // HUD의 상태 텍스트를 직접 업데이트
                    so.ForceStatusText(text);
                };
            }

            BuildFSM(_ctx);

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
            _completedState.SetThinkingState(thinkingState);
            _errorState = new AgentErrorState(ctx);
            var disconnectedState = new AgentDisconnectedState(ctx);
            var chattingState = new AgentChattingState(ctx);

            // 완료 모션 끝 → Idle 자동 복귀 + 카메라 Office 복귀
            _completedState.OnCompletionAnimDone += () =>
            {
                thinkingState.ResetSeated();
                _fsm.TransitionTo<AgentIdleState>();

                var focusCam = FindFirstObjectByType<Presentation.Camera.AgentFocusCameraController>();
                if (focusCam != null) focusCam.ReturnToOffice();
            };
            // 에러 3초 후 → Idle 자동 복귀
            _errorState.OnErrorAnimDone += () => _fsm.TransitionTo<AgentIdleState>();

            _fsm.AddState(idleState);
            _fsm.AddState(typingState);
            _fsm.AddState(thinkingState);
            _fsm.AddState(_completedState);
            _fsm.AddState(_errorState);
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
                    _fsm.TransitionTo<AgentErrorState>();
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

        /// <summary>"작업 완료" 버튼 — 앉아있는 에이전트를 일어나게 하고 Idle로 복귀</summary>
        public void DismissFromWork()
        {
            if (_fsm != null && _fsm.IsInState<AgentCompletedState>())
                _completedState.DismissAgent();
            else
                _fsm?.TransitionTo<AgentIdleState>();
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
        [ContextMenu("Test: Error")]
        private void DbgError() => OnAgentStateChanged(AgentActionType.TaskFailed);
#endif
    }
}
