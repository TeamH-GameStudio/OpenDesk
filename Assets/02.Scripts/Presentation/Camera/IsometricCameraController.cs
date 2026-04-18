using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenDesk.Presentation.Camera
{
    /// <summary>
    /// Cinemachine 기반 이소메트릭 카메라.
    /// - OverviewCam: 오피스 전체 조감 (고정 위치/각도)
    /// - AgentCam: 에이전트를 화면 중앙에 두고 따라감 (각도 불변, 스크롤 줌)
    /// - 포커스 해제 시 오버뷰 원위치로 복귀
    /// </summary>
    public class IsometricCameraController : MonoBehaviour
    {
        [Header("Virtual Cameras")]
        [SerializeField] private CinemachineVirtualCamera _overviewCam;
        [SerializeField] private CinemachineVirtualCamera _agentCam;

        [Header("에이전트 포커스 오프셋")]
        [Tooltip("에이전트 기준 카메라 위치 오프셋 (X=좌우, Y=높이, Z=앞뒤)")]
        [SerializeField] private Vector3 _agentOffset = new(0f, 3f, -5f);

        [Header("스크롤 줌")]
        [SerializeField] private float _zoomSpeed = 15f;
        [SerializeField] private float _minDistance = 0.5f;
        [SerializeField] private float _maxDistance = 30f;
        [Tooltip("줌인 시 카메라가 향하는 목표점 (에이전트 로컬 Y 오프셋)")]
        [SerializeField] private float _focusHeight = 0.8f;

        private const int PriorityHigh = 20;
        private const int PriorityLow = 10;

        private Transform _currentTarget;
        private bool _isFocused;
        private CinemachineTransposer _agentTransposer;

        private Vector3 _baseOffset;
        private float _currentDistance;
        private Vector3 _zoomDirection;

        private void Start()
        {
            _baseOffset = _agentOffset;

            // 줌 방향 = 오프셋에서 포커스 높이 지점(0, _focusHeight, 0)을 향하는 단위벡터
            // 줌인하면 이 방향으로 카메라가 에이전트 중심에 수렴
            var focusPoint = new Vector3(0f, _focusHeight, 0f);
            _zoomDirection = (_baseOffset - focusPoint).normalized;
            _currentDistance = (_baseOffset - focusPoint).magnitude;

            if (_overviewCam != null) _overviewCam.Priority = PriorityHigh;
            if (_agentCam != null)
            {
                _agentCam.Priority = PriorityLow;
                _agentTransposer = _agentCam.GetCinemachineComponent<CinemachineTransposer>();

                if (_agentTransposer != null)
                    _agentTransposer.m_FollowOffset = _agentOffset;

                // OverviewCam과 동일한 각도 — 각도 고정
                if (_overviewCam != null)
                    _agentCam.transform.rotation = _overviewCam.transform.rotation;

                // LookAt 없음 — 각도 회전 방지
                _agentCam.LookAt = null;
            }
        }

        private void Update()
        {
            if (!_isFocused || _agentTransposer == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // 스크롤 위 = 줌인 (거리 감소), 아래 = 줌아웃 (거리 증가)
            // scroll 값 정규화 (120 단위) + 현재 거리 비례 줌 (멀수록 빠르게)
            float normalizedScroll = scroll / 120f;
            _currentDistance -= normalizedScroll * _zoomSpeed * Mathf.Max(_currentDistance * 0.15f, 0.3f);
            _currentDistance = Mathf.Clamp(_currentDistance, _minDistance, _maxDistance);

            // 포커스 높이 지점에서 줌 방향으로 거리만큼 떨어진 위치
            var focusPoint = new Vector3(0f, _focusHeight, 0f);
            _agentTransposer.m_FollowOffset = focusPoint + _zoomDirection * _currentDistance;
        }

        /// <summary>에이전트 클릭 시 — 화면 중앙에 에이전트, 카메라 따라감 (각도 불변)</summary>
        public void FocusOnAgent(Transform agentTransform)
        {
            if (_agentCam == null) return;

            // 같은 에이전트 재클릭 → 오버뷰 복귀
            if (_isFocused && _currentTarget == agentTransform)
            {
                ReturnToOverview();
                return;
            }

            _currentTarget = agentTransform;
            _isFocused = true;

            // 줌 리셋 — 기본 거리로
            var focusPoint = new Vector3(0f, _focusHeight, 0f);
            _currentDistance = (_baseOffset - focusPoint).magnitude;

            _agentCam.Follow = agentTransform;
            _agentCam.LookAt = null;

            if (_agentTransposer != null)
                _agentTransposer.m_FollowOffset = _baseOffset;

            _agentCam.Priority = PriorityHigh;
            if (_overviewCam != null) _overviewCam.Priority = PriorityLow;
        }

        /// <summary>오버뷰로 복귀 (원래 위치/각도)</summary>
        public void ReturnToOverview()
        {
            _isFocused = false;
            _currentTarget = null;

            if (_overviewCam != null) _overviewCam.Priority = PriorityHigh;
            if (_agentCam != null) _agentCam.Priority = PriorityLow;
        }

        public bool IsFocused => _isFocused;
        public Transform CurrentTarget => _currentTarget;
    }
}
