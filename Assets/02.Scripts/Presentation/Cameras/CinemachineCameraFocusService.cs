using System;
using Cinemachine;
using OpenDesk.Presentation.Character;
using UnityEngine;
using VContainer;

namespace OpenDesk.Presentation.Cameras
{
    /// <summary>
    /// Cinemachine 2.10 기반 <see cref="ICameraFocusService"/> 구현체.
    ///
    /// 씬에 배치된 OverviewVCam(priority 10) + FocusVCam(priority 0, LookAt/Follow null) 을 보유하고,
    /// FocusOn 시 spawner.GetAgent(sessionId).ModelInstance 의 "CameraTarget" 자식 Transform 을
    /// FocusVCam.LookAt/Follow 에 set 한 뒤 priority 를 활성값(20)으로 올린다.
    /// CinemachineBrain.DefaultBlend(EaseInOut, 1.0s — --motion-camera 토큰)에 의해 자연스럽게 전환.
    ///
    /// AgentSpawner.Despawned 이벤트를 구독해 포커스 중이던 캐릭터가 사라지면 자동으로 release.
    /// </summary>
    public sealed class CinemachineCameraFocusService : MonoBehaviour, ICameraFocusService
    {
        [Header("VCams")]
        [Tooltip("오피스 overview — 항상 enabled, priority 10 고정")]
        [SerializeField] private CinemachineVirtualCamera _overviewVCam;
        [Tooltip("캐릭터 포커스용 — priority 0 으로 시작, FocusOn 시 20 으로 승격")]
        [SerializeField] private CinemachineVirtualCamera _focusVCam;

        [Header("Priority")]
        [SerializeField] private int _overviewPriority = 10;
        [SerializeField] private int _focusActivePriority = 20;
        [SerializeField] private int _focusInactivePriority = 0;

        [Header("Target")]
        [Tooltip("AgentSpawner.EnsureCameraTarget 가 생성하는 자식 Transform 이름")]
        [SerializeField] private string _cameraTargetChildName = "CameraTarget";

        private AgentSpawner _spawner;
        private string _currentSessionId;

        [Inject]
        public void Construct(AgentSpawner spawner)
        {
            _spawner = spawner;
        }

        public bool IsFocused => !string.IsNullOrEmpty(_currentSessionId);
        public string CurrentSessionId => _currentSessionId;

        private void Awake()
        {
            if (_overviewVCam != null) _overviewVCam.Priority = _overviewPriority;
            if (_focusVCam != null) _focusVCam.Priority = _focusInactivePriority;
        }

        // Despawned 구독은 Start 에서 — VContainer [Inject] Construct 가 OnEnable 이후 호출될 수 있어
        // OnEnable 시점에는 _spawner 가 null 일 수 있다.
        private void Start()
        {
            if (_spawner != null)
                _spawner.Despawned += OnAgentDespawned;
        }

        private void OnDestroy()
        {
            if (_spawner != null)
                _spawner.Despawned -= OnAgentDespawned;
        }

        public void FocusOn(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogWarning("[CameraFocusService] FocusOn 호출에 sessionId 가 비어있음");
                return;
            }
            if (_focusVCam == null)
            {
                Debug.LogWarning("[CameraFocusService] FocusVCam 미할당 — 인스펙터에서 연결 필요");
                return;
            }
            if (_currentSessionId == sessionId) return; // 같은 대상 재요청 → 깜빡임 방지

            var target = ResolveTarget(sessionId);
            if (target == null) return;

            // Follow 만 — Body(Transposer) 가 position 을 계산. LookAt(Aim) 은 미설정이라 회전은 VCam transform 그대로 유지.
            // FocusVCam 인스펙터 권장 설정: Aim=Do Nothing, Body=Transposer(Binding Mode=Lock To Target On Assign), Follow Offset 으로 위치 보정.
            _focusVCam.LookAt = null;
            _focusVCam.Follow = target;
            _focusVCam.Priority = _focusActivePriority;
            _currentSessionId = sessionId;
        }

        public void ReleaseFocus()
        {
            if (string.IsNullOrEmpty(_currentSessionId)) return;
            if (_focusVCam != null)
                _focusVCam.Priority = _focusInactivePriority;
            _currentSessionId = null;
        }

        private Transform ResolveTarget(string sessionId)
        {
            if (_spawner == null) return null;

            var spawned = _spawner.GetAgent(sessionId);
            if (spawned == null || spawned.ModelInstance == null)
            {
                Debug.LogWarning($"[CameraFocusService] 에이전트 모델 없음: {sessionId}");
                return null;
            }

            var target = spawned.ModelInstance.transform.Find(_cameraTargetChildName);
            // 폴백: 자식 없으면 root (발 위치라 미디엄샷 구도 어긋날 수 있음 — Spawner 가 EnsureCameraTarget 호출하면 발생 X)
            return target != null ? target : spawned.ModelInstance.transform;
        }

        private void OnAgentDespawned(string sessionId)
        {
            if (_currentSessionId == sessionId) ReleaseFocus();
        }
    }
}
