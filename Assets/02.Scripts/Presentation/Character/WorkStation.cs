using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    public enum WorkStationType
    {
        Main,       // 메인 에이전트 (researcher)
        Sub,        // 서브 에이전트 (writer/analyst)
        Resting     // 휴식용 (Idle 상태)
    }

    public enum ApproachSide
    {
        Right,  // 의자 오른쪽에서 접근
        Left    // 의자 왼쪽에서 접근
    }

    /// <summary>
    /// 의자-책상 페어링 컴포넌트.
    /// 의자 오브젝트(또는 부모)에 부착하여 SitPoint/ApproachPoint를 명시적으로 지정.
    ///
    /// 구조:
    ///   [WorkStation]
    ///     ├── Chair (메시)
    ///     ├── Desk (메시)
    ///     ├── SitPoint (앉을 위치+방향, forward=바라볼 방향)
    ///     └── ApproachPoint (NavMesh 접근 위치, 의자 뒤/옆 0.5~0.8m)
    ///
    /// SitPoint/ApproachPoint가 미지정이면 자동 계산 (의자 위치 기반).
    /// </summary>
    public class WorkStation : MonoBehaviour
    {
        [Header("유형")]
        [SerializeField] private WorkStationType _type = WorkStationType.Main;

        [Header("포인트 (미지정 시 자동 계산)")]
        [Tooltip("앉을 정확한 위치")]
        [SerializeField] private Transform _sitPoint;

        [Tooltip("NavMesh 접근 위치. 의자 뒤쪽/옆 0.5~0.8m 지점")]
        [SerializeField] private Transform _approachPoint;

        [Tooltip("앉아서 바라볼 대상 (책상 등). 지정하면 이 방향을 향해 앉음")]
        [SerializeField] private Transform _lookAtTarget;

        [Header("자동 계산 설정 (포인트 미지정 시)")]
        [SerializeField] private float _seatYOffset = 0.3f;
        [SerializeField] private float _approachDistance = 0.6f;
        [Tooltip("접근 방향: 의자 기준 오른쪽(Right) 또는 왼쪽(Left). 등받이 회피용")]
        [SerializeField] private ApproachSide _approachSide = ApproachSide.Right;

        public WorkStationType Type => _type;
        public bool IsOccupied { get; set; }

        /// <summary>앉을 위치 (월드 좌표)</summary>
        public Vector3 SitPosition =>
            _sitPoint != null
                ? _sitPoint.position
                : transform.position + Vector3.up * _seatYOffset;

        /// <summary>앉았을 때 바라볼 방향 (월드). lookAtTarget > sitPoint.forward > transform.forward 순</summary>
        public Vector3 SitForward
        {
            get
            {
                if (_lookAtTarget != null)
                {
                    var dir = _lookAtTarget.position - SitPosition;
                    dir.y = 0;
                    if (dir.sqrMagnitude > 0.01f)
                        return dir.normalized;
                }
                return _sitPoint != null ? _sitPoint.forward : transform.forward;
            }
        }

        /// <summary>NavMesh 접근 위치 (월드 좌표). 등받이 회피를 위해 옆쪽으로 자동 계산.</summary>
        public Vector3 ApproachPosition
        {
            get
            {
                if (_approachPoint != null)
                    return _approachPoint.position;

                // 의자 옆쪽 (right 또는 left)에서 접근 — 등받이 관통 방지
                var sideDir = _approachSide == ApproachSide.Right
                    ? Vector3.Cross(Vector3.up, SitForward).normalized   // 오른쪽
                    : Vector3.Cross(SitForward, Vector3.up).normalized;  // 왼쪽
                return SitPosition + sideDir * _approachDistance;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // SitPoint — 파란 구
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(SitPosition, 0.15f);
            // SitForward 방향 — 파란 선
            Gizmos.DrawRay(SitPosition, SitForward * 0.5f);

            // ApproachPoint — 초록 구
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(ApproachPosition, 0.15f);

            // 접근 경로
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(ApproachPosition, SitPosition);

            // LookAtTarget — 빨간 선
            if (_lookAtTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(SitPosition, _lookAtTarget.position);
                Gizmos.DrawWireSphere(_lookAtTarget.position, 0.1f);
            }
        }
#endif
    }
}
