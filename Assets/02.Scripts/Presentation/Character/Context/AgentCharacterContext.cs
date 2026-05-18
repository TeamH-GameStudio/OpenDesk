using OpenDesk.Characters.Wardrobe.Expressions;
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

        // ── 확장 슬롯 (추후 표정/이펙트/사운드 등) ──────────────
        /// <summary>표정 변화 컨트롤러 (추후 연결)</summary>
        public IExpressionController Expression { get; set; }

        public AgentCharacterContext(
            IAnimationController animation,
            string sessionId,
            string agentName,
            NavMeshAgent navAgent = null,
            Transform transform = null)
        {
            Animation = animation;
            SessionId = sessionId;
            AgentName = agentName;
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

    /// <summary>
    /// 표정/이펙트 제어 인터페이스. 캐릭터 프리팹의 <see cref="OpenDesk.Characters.Wardrobe.WardrobeApplier"/>
    /// 가 실제 PSD eye/mouth 텍스처 swap 을 수행하며, 그 구현체는
    /// <see cref="OpenDesk.Presentation.Character.WardrobeExpressionController"/> 가 어댑터로 제공한다.
    /// FSM States 가 이 인터페이스를 통해 enum 키를 발행하면 시네마틱과 동일한 표정 시스템이 동작.
    /// </summary>
    public interface IExpressionController
    {
        void SetExpression(AgentExpressionKey key);
        void PlayEffect(string effectName);
    }
}
