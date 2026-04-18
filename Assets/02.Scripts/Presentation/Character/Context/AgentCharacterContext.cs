using UnityEngine;
using UnityEngine.AI;

namespace OpenDesk.Presentation.Character.Context
{
    /// <summary>
    /// 캐릭터 상태 머신이 접근하는 컨텍스트.
    /// 각 State는 이것만 통해 외부 시스템 접근.
    ///
    /// 확장 포인트:
    ///   - Navigation: NavMeshAgent 기반 이동
    ///   - Expression: 표정/이펙트 슬롯 (추후 추가)
    /// </summary>
    public class AgentCharacterContext
    {
        // 애니메이션
        public IAnimationController Animation { get; }

        // 네비게이션
        public NavMeshAgent NavAgent { get; }
        public Transform Transform { get; }

        // 에이전트 식별
        public string SessionId { get; }
        public string AgentName { get; }
        public string AgentId { get; }

        // ── 확장 슬롯 ──────────────────────────────────────────
        /// <summary>표정 변화 컨트롤러</summary>
        public IExpressionController Expression { get; set; }

        /// <summary>카메라가 이 에이전트를 포커스 중인지 (포커스 중이면 배회 안 함)</summary>
        public bool IsFocused { get; set; }

        /// <summary>HUD 상태 텍스트 직접 업데이트 (FSM 서브상태용)</summary>
        public System.Action<string> OnHUDStatusChanged { get; set; }

        /// <summary>현재 사용 중인 WorkStation (앉아있을 때 non-null)</summary>
        public WorkStation CurrentWorkStation { get; set; }

        public AgentCharacterContext(
            IAnimationController animation,
            string sessionId,
            string agentName,
            string agentId = "",
            NavMeshAgent navAgent = null,
            Transform transform = null)
        {
            Animation = animation;
            SessionId = sessionId;
            AgentName = agentName;
            AgentId = agentId;
            NavAgent = navAgent;
            Transform = transform;
        }

        /// <summary>NavMesh 목적지 설정 (있으면 이동, 없으면 무시)</summary>
        public bool MoveTo(Vector3 destination)
        {
            if (NavAgent == null || !NavAgent.isOnNavMesh) return false;
            NavAgent.isStopped = false;
            return NavAgent.SetDestination(destination);
        }

        /// <summary>이동 중지</summary>
        public void StopMoving()
        {
            if (NavAgent != null && NavAgent.isOnNavMesh)
            {
                NavAgent.isStopped = true;
                NavAgent.ResetPath();
            }
        }

        /// <summary>목적지에 도착했는지</summary>
        public bool HasReachedDestination
        {
            get
            {
                if (NavAgent == null || !NavAgent.isOnNavMesh) return true;
                if (NavAgent.pathPending) return false;
                return NavAgent.remainingDistance <= NavAgent.stoppingDistance + 0.1f;
            }
        }

        /// <summary>현재 이동 중인지</summary>
        public bool IsMoving =>
            NavAgent != null && NavAgent.isOnNavMesh && !NavAgent.isStopped &&
            NavAgent.velocity.sqrMagnitude > 0.01f;
    }

    /// <summary>표정/이펙트 제어 인터페이스 (추후 구현)</summary>
    public interface IExpressionController
    {
        void SetExpression(string expressionName);
        void PlayEffect(string effectName);
    }
}
