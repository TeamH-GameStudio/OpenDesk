using OpenDesk.Characters.Wardrobe;
using OpenDesk.Characters.Wardrobe.Expressions;
using OpenDesk.Presentation.Character.Context;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// <see cref="IExpressionController"/> 의 WardrobeApplier 어댑터.
    /// 캐릭터 프리팹 어디든 (루트나 face/body 하위) 붙여두면 FSM States 가 호출하는 표정 변경이
    /// 시네마틱과 동일한 PSD eye/mouth 텍스처 swap (MaterialPropertyBlock) 으로 실행된다.
    ///
    /// <see cref="AgentCharacterController.InitializeFSM"/> 가 spawn 시점에 자식 컴포넌트에서 자동 탐색해
    /// <see cref="AgentCharacterContext.Expression"/> 에 주입.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WardrobeExpressionController : MonoBehaviour, IExpressionController
    {
        [Tooltip("이 캐릭터의 WardrobeApplier. 비워두면 GetComponentInChildren 으로 자동 탐색.")]
        [SerializeField] private WardrobeApplier _applier;

        private void Awake()
        {
            if (_applier == null)
                _applier = GetComponentInChildren<WardrobeApplier>(includeInactive: true);
        }

        public void SetExpression(AgentExpressionKey key)
        {
            // Applier 미할당 / eye option 미장착 시 WardrobeApplier.SetEyeExpression 내부에서 no-op.
            _applier?.SetEyeExpression(key);
        }

        public void PlayEffect(string effectName)
        {
            // 이펙트 시스템 (파티클/사운드/VFX) 도입 시 여기서 라우팅.
        }
    }
}
