using UnityEngine;

namespace OpenDesk.Presentation.Camera
{
    /// <summary>
    /// 3단계 카메라 연출 컨트롤러.
    ///
    /// [1] Office View (기본) — OrbitCamera로 사무실 조감
    /// [2] Agent Focus (대화 대기) — 에이전트 쪽으로 수평 접근, 에이전트가 카메라를 바라봄
    /// [3] Monitor Cam (작업 중) — MonitorCamPoint로 이동, 웹캠 느낌으로 작업 모습 비춤
    ///
    /// Perspective 모드 사용 (원근감으로 자연스러운 연출).
    /// </summary>
    public class AgentFocusCameraController : MonoBehaviour
    {
        public enum CameraState { Office, AgentFocus, MonitorCam, Transitioning }

        [Header("Agent Focus (대화 대기)")]
        [Tooltip("왼쪽(-) / 오른쪽(+)")]
        [SerializeField] private float _포커스좌우 = 0f;
        [Tooltip("카메라 높이")]
        [SerializeField] private float _포커스높이 = 0.8f;
        [Tooltip("에이전트까지 거리")]
        [SerializeField] private float _포커스거리 = 2.0f;
        [Tooltip("에이전트 시선 높이")]
        [SerializeField] private float _포커스시선높이 = 0.6f;
        [Tooltip("FOV")]
        [SerializeField] private float _포커스FOV = 45f;

        [Header("Monitor Cam (작업 중)")]
        [Tooltip("모니터 기준 시선 높이 (에이전트의 어디를 바라볼지)")]
        [SerializeField] private float _모니터시선높이 = 0.5f;
        [Tooltip("FOV")]
        [SerializeField] private float _모니터FOV = 50f;

        [Header("Office View (기본)")]
        [Tooltip("FOV")]
        [SerializeField] private float _오피스FOV = 60f;

        [Header("전환 속도")]
        [Tooltip("Office → Agent Focus (초)")]
        [SerializeField] private float _포커스전환시간 = 0.8f;
        [Tooltip("Agent Focus → Monitor Cam (초)")]
        [SerializeField] private float _모니터전환시간 = 1.0f;
        [Tooltip("→ Office 복귀 (초)")]
        [SerializeField] private float _복귀시간 = 0.8f;

        // ── 내부 ────────────────────────────────────────────────
        private UnityEngine.Camera _mainCamera;
        private Character.OrbitCamera _orbitCamera;
        private Transform _currentTarget;
        private CameraState _state = CameraState.Office;

        // 보간
        private float _transitionProgress;
        private float _currentTransitionDuration;
        private Vector3 _startPos;
        private Quaternion _startRot;
        private float _startFOV;
        private Vector3 _targetPos;
        private Quaternion _targetRot;
        private float _targetFOV;

        // Office 복귀용
        private Vector3 _savedOfficePos;
        private Quaternion _savedOfficeRot;
        private float _savedOfficeFOV;

        // MonitorCamPoint
        private Transform _monitorCamPoint;

        public CameraState State => _state;
        public Transform CurrentTarget => _currentTarget;

        private void Awake()
        {
            _mainCamera = UnityEngine.Camera.main;
            if (_mainCamera != null)
            {
                _orbitCamera = _mainCamera.GetComponent<Character.OrbitCamera>();

                // Perspective 모드 보장
                _mainCamera.orthographic = false;
                _mainCamera.fieldOfView = _오피스FOV;

                if (_orbitCamera != null)
                    _orbitCamera.enabled = true;

                // 현재 Office 위치 저장
                SaveOfficePosition();
            }
        }

        // ================================================================
        //  외부 API
        // ================================================================

        /// <summary>[1→2] 에이전트 클릭 → Agent Focus</summary>
        public void FocusOnAgent(Transform agentTransform)
        {
            if (agentTransform == null || _mainCamera == null) return;

            // 같은 에이전트 재클릭 → Office 복귀
            if (_state == CameraState.AgentFocus && _currentTarget == agentTransform)
            {
                ReturnToOffice();
                return;
            }

            // 이전 타겟 해제
            SetTargetFocused(false);

            _currentTarget = agentTransform;
            SetTargetFocused(true);

            // 에이전트가 카메라를 바라보게
            MakeAgentLookAtCamera();

            // Office 위치 저장 (복귀용)
            if (_state == CameraState.Office)
                SaveOfficePosition();

            // OrbitCamera 비활성화
            if (_orbitCamera != null)
                _orbitCamera.enabled = false;

            // Agent Focus 목표 계산
            CalcAgentFocusTarget();
            StartTransition(_포커스전환시간, _포커스FOV, CameraState.AgentFocus);
        }

        /// <summary>[2→3] 프롬프트 전송 → Monitor Cam</summary>
        public void TransitionToMonitorCam()
        {
            Debug.Log($"[Camera] TransitionToMonitorCam 호출 -- state={_state} target={(_currentTarget != null ? _currentTarget.name : "null")}");

            if (_currentTarget == null || _mainCamera == null)
            {
                Debug.LogWarning("[Camera] TransitionToMonitorCam 실패 -- target 또는 camera가 null");
                return;
            }

            // 이미 MonitorCam이면 스킵
            if (_state == CameraState.MonitorCam) return;

            // MonitorCamPoint 찾기
            _monitorCamPoint = FindMonitorCamPoint();
            if (_monitorCamPoint == null)
            {
                Debug.LogWarning("[Camera] MonitorCamPoint를 찾을 수 없음 -- Agent Focus 유지");
                return;
            }

            // 위치: MonitorCamPoint의 position
            _targetPos = _monitorCamPoint.position;

            // 회전: 자동으로 에이전트를 바라보기 (rotation 수동 설정 불필요)
            var lookAtPos = _currentTarget.position + Vector3.up * _모니터시선높이;
            _targetRot = Quaternion.LookRotation(lookAtPos - _targetPos);

            StartTransition(_모니터전환시간, _모니터FOV, CameraState.MonitorCam);
        }

        /// <summary>[any→1] Office View로 복귀</summary>
        public void ReturnToOffice()
        {
            Debug.Log($"[Camera] ReturnToOffice 호출 -- state={_state}\n{UnityEngine.StackTraceUtility.ExtractStackTrace()}");
            if (_state == CameraState.Office) return;

            SetTargetFocused(false);
            _currentTarget = null;

            _targetPos = _savedOfficePos;
            _targetRot = _savedOfficeRot;
            StartTransition(_복귀시간, _savedOfficeFOV, CameraState.Office);
        }

        // ================================================================
        //  전환 엔진
        // ================================================================

        private void StartTransition(float duration, float targetFOV, CameraState nextState)
        {
            _startPos = _mainCamera.transform.position;
            _startRot = _mainCamera.transform.rotation;
            _startFOV = _mainCamera.fieldOfView;
            _targetFOV = targetFOV;
            _currentTransitionDuration = duration;
            _transitionProgress = 0f;
            _state = nextState; 
        }

        private void LateUpdate()
        {
            if (_state == CameraState.Office && !_isReturning) return;

            // 보간 진행
            if (_transitionProgress < 1f)
            {
                _transitionProgress += Time.deltaTime / _currentTransitionDuration;
                float t = SmoothStep(Mathf.Clamp01(_transitionProgress));

                _mainCamera.transform.position = Vector3.Lerp(_startPos, _targetPos, t);
                _mainCamera.transform.rotation = Quaternion.Slerp(_startRot, _targetRot, t);
                _mainCamera.fieldOfView = Mathf.Lerp(_startFOV, _targetFOV, t);

                if (_transitionProgress >= 1f)
                    OnTransitionComplete();
            }
            else
            {
                // 전환 완료 후 타겟 추적
                if (_state == CameraState.AgentFocus && _currentTarget != null)
                {
                    CalcAgentFocusTarget();
                    _mainCamera.transform.position = _targetPos;
                    _mainCamera.transform.rotation = _targetRot;
                    MakeAgentLookAtCamera();
                }
                else if (_state == CameraState.MonitorCam && _monitorCamPoint != null && _currentTarget != null)
                {
                    _mainCamera.transform.position = _monitorCamPoint.position;
                    var lookAt = _currentTarget.position + Vector3.up * _모니터시선높이;
                    _mainCamera.transform.rotation = Quaternion.LookRotation(lookAt - _monitorCamPoint.position);
                }
            }
        }

        private bool _isReturning;

        private void OnTransitionComplete()
        {
            switch (_state)
            {
                case CameraState.Office:
                    // OrbitCamera 복귀
                    if (_orbitCamera != null)
                        _orbitCamera.enabled = true;
                    _isReturning = false;
                    break;

                case CameraState.AgentFocus:
                    break;

                case CameraState.MonitorCam:
                    break;
            }
        }

        // ================================================================
        //  목표 계산
        // ================================================================

        private void CalcAgentFocusTarget()
        {
            var agent = _currentTarget;
            var agentPos = agent.position;

            // 카메라→에이전트 방향 (수평)
            var camToAgent = agentPos - _mainCamera.transform.position;
            camToAgent.y = 0;
            if (camToAgent.sqrMagnitude < 0.01f)
                camToAgent = -agent.forward;
            camToAgent.Normalize();

            // 에이전트의 오른쪽 (수평 접근 기준)
            var right = Vector3.Cross(Vector3.up, camToAgent).normalized;

            _targetPos = agentPos
                - camToAgent * _포커스거리     // 에이전트 앞에 수평으로
                + right * _포커스좌우
                + Vector3.up * _포커스높이;

            var lookAtPos = agentPos + Vector3.up * _포커스시선높이;
            _targetRot = Quaternion.LookRotation(lookAtPos - _targetPos);
        }

        /// <summary>에이전트가 카메라를 바라보게 회전</summary>
        private void MakeAgentLookAtCamera()
        {
            if (_currentTarget == null || _mainCamera == null) return;
            var dir = _mainCamera.transform.position - _currentTarget.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
                _currentTarget.forward = dir.normalized;
        }

        // ================================================================
        //  MonitorCamPoint 검색
        // ================================================================

        private Transform FindMonitorCamPoint()
        {
            // 비활성 포함 전체 검색
            var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
            Transform closest = null;
            float closestDist = float.MaxValue;

            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                if (!t.name.Contains("MonitorCamPoint")) continue;
                // 에디터 프리팹 에셋이 아닌 씬 오브젝트만
                if (t.gameObject.scene.name == null) continue;

                Debug.Log($"[Camera] MonitorCamPoint 발견: {t.name} (parent={t.parent?.name}) active={t.gameObject.activeInHierarchy}");

                if (_currentTarget == null) return t;

                var dist = Vector3.Distance(_currentTarget.position, t.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = t;
                }
            }

            if (closest == null)
                Debug.LogWarning("[Camera] MonitorCamPoint를 씬에서 찾지 못함");

            return closest;
        }

        // ================================================================
        //  유틸
        // ================================================================

        private void SaveOfficePosition()
        {
            _savedOfficePos = _mainCamera.transform.position;
            _savedOfficeRot = _mainCamera.transform.rotation;
            _savedOfficeFOV = _mainCamera.fieldOfView;
        }

        private void SetTargetFocused(bool focused)
        {
            if (_currentTarget == null) return;
            var charCtrl = _currentTarget.GetComponent<Character.AgentCharacterController>();
            if (charCtrl != null) charCtrl.SetFocused(focused);
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

#if UNITY_EDITOR
        [ContextMenu("Test: Focus")]
        private void DbgFocus()
        {
            var agent = FindFirstObjectByType<Character.AgentCharacterController>();
            if (agent != null) FocusOnAgent(agent.transform);
        }
        [ContextMenu("Test: MonitorCam")]
        private void DbgMonitor() => TransitionToMonitorCam();
        [ContextMenu("Test: Return")]
        private void DbgReturn() => ReturnToOffice();
#endif
    }
}
