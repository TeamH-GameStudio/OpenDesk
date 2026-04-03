using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 마우스 드래그(회전) + 휠(줌) + 미들 드래그(팬) 오빗 카메라.
    /// 오피스 씬에서 에이전트와 사무실을 자유롭게 관찰.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Vector3 _targetPoint = new(4f, 1f, 2f);

        [Header("Orbit")]
        [SerializeField] private float _distance = 8f;
        [SerializeField] private float _minDistance = 2f;
        [SerializeField] private float _maxDistance = 20f;
        [SerializeField] private float _rotationSpeed = 3f;
        [SerializeField] private float _zoomSpeed = 2f;
        [SerializeField] private float _panSpeed = 0.01f;

        [Header("Angle Limits")]
        [SerializeField] private float _minVerticalAngle = 5f;
        [SerializeField] private float _maxVerticalAngle = 80f;

        [Header("Smoothing")]
        [SerializeField] private float _smoothTime = 0.1f;

        private float _yaw;
        private float _pitch = 30f;
        private Vector3 _currentVelocity;
        private Vector3 _targetPosition;

        private void Start()
        {
            // 현재 카메라 위치에서 초기 각도 계산
            var offset = transform.position - _targetPoint;
            _distance = offset.magnitude;
            _yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(offset.y / _distance) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(_pitch, _minVerticalAngle, _maxVerticalAngle);

            UpdateTargetPosition();
            transform.position = _targetPosition;
        }

        private void LateUpdate()
        {
            HandleInput();
            UpdateTargetPosition();

            // 부드러운 이동
            transform.position = Vector3.SmoothDamp(
                transform.position, _targetPosition, ref _currentVelocity, _smoothTime);
            transform.LookAt(_targetPoint);
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var delta = mouse.delta.ReadValue();

            // 우클릭 드래그 → 회전
            if (mouse.rightButton.isPressed)
            {
                _yaw += delta.x * _rotationSpeed * 0.1f;
                _pitch -= delta.y * _rotationSpeed * 0.1f;
                _pitch = Mathf.Clamp(_pitch, _minVerticalAngle, _maxVerticalAngle);
            }

            // 미들 드래그 → 팬
            if (mouse.middleButton.isPressed)
            {
                var right = transform.right;
                var up = transform.up;
                var panDelta = (-delta.x * right + -delta.y * up) * _panSpeed * _distance * 0.1f;
                _targetPoint += panDelta;
            }

            // 스크롤 휠 → 줌
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _distance -= scroll * _zoomSpeed * _distance * 0.001f;
                _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
            }
        }

        private void UpdateTargetPosition()
        {
            var pitchRad = _pitch * Mathf.Deg2Rad;
            var yawRad = _yaw * Mathf.Deg2Rad;

            var offset = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
            ) * _distance;

            _targetPosition = _targetPoint + offset;
        }

        /// <summary>외부에서 타겟 포인트 변경</summary>
        public void SetTarget(Vector3 point)
        {
            _targetPoint = point;
        }
    }
}
