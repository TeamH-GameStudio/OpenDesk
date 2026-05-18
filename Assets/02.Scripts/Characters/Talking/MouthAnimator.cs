using System.Collections;
using UnityEngine;

namespace OpenDesk.Characters.Talking
{
    /// <summary>
    /// 캐릭터 입모양 사이클 컴포넌트.
    /// 단일 SkinnedMeshRenderer 의 mouth submaterial 의 _BaseMap 텍스처를 closed/half/open 으로 swap.
    /// MaterialPropertyBlock 사용 — 다른 캐릭터/표정 시스템과 머티리얼 격리.
    ///
    /// WardrobeApplier 와 동일한 _bodyRenderer / _mouthMaterialIndex / _mouthTextureProperty 를
    /// Inspector 에서 할당하면 표정 시스템 (eyes 슬롯) 과 격리된 채 mouth 만 사이클한다.
    ///
    /// <remarks>
    /// TODO: 표정 sub-region 분리는 1차 구현 범위 밖.
    /// WardrobeApplier 의 ExpressionSet 에 paired mouth PSD 가 있는 경우, 표정 변경
    /// (SetEyeExpression) 호출 시 mouth 슬롯이 expression mouth 로 덮어쓰여 사이클이
    /// 한 프레임 어긋날 수 있음. 추후 expression set 와 통합 시 talking 우선순위 + 표정
    /// 별 mouth-cycle texture set 으로 확장 권장.
    /// </remarks>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MouthAnimator : MonoBehaviour
    {
        [Header("렌더러 / 머티리얼")]
        [Tooltip("WardrobeApplier 와 동일한 body Renderer. mouth 가 submaterial 로 들어있는 SkinnedMeshRenderer.")]
        [SerializeField] private Renderer _bodyRenderer;

        [Tooltip("mouth submaterial 의 인덱스 (WardrobeApplier 기본값과 동일하게 2).")]
        [SerializeField] private int _mouthMaterialIndex = 2;

        [Tooltip("mouth 텍스처를 바인딩할 shader property. URP Lit 은 _BaseMap.")]
        [SerializeField] private string _mouthTextureProperty = "_BaseMap";

        [Header("입모양 텍스처")]
        [Tooltip("입 닫음 (talking 종료 시 복귀할 텍스처).")]
        [SerializeField] private Texture2D _closed;
        [SerializeField] private Texture2D _half;
        [SerializeField] private Texture2D _open;

        [Header("사이클 페이스")]
        [Tooltip("프레임 사이 최소 대기 (초). 0.08 ~ 0.15 권장.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _minInterval = 0.08f;

        [Tooltip("프레임 사이 최대 대기 (초). _minInterval 보다 커야 함.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _maxInterval = 0.15f;

        [Tooltip("사이클 시 같은 프레임이 연속해서 나오지 않게 last 와 다른 텍스처를 우선 선택.")]
        [SerializeField] private bool _avoidImmediateRepeat = true;

        // ── 내부 상태 ──────────────────────────────────────────
        private MaterialPropertyBlock _mpb;
        private Coroutine _cycleCoroutine;
        private Texture2D _last;

        public bool IsTalking => _cycleCoroutine != null;

        private void Awake()
        {
            // MPB 는 Awake 에서 안전하게 (생성자에서는 Unity 가 거부).
            _mpb = new MaterialPropertyBlock();

            if (_bodyRenderer == null)
                Debug.LogWarning("[MouthAnimator] _bodyRenderer 미할당 — Inspector 슬롯 확인 필요.", this);
        }

        // ── 공개 API ──────────────────────────────────────────

        /// <summary>입모양 사이클 시작. 이미 talking 중이면 no-op.</summary>
        public void StartTalking()
        {
            if (_cycleCoroutine != null) return;
            if (!enabled || !gameObject.activeInHierarchy) return;
            _cycleCoroutine = StartCoroutine(CycleRoutine());
        }

        /// <summary>입모양 사이클 중단 + closed 텍스처로 복귀.</summary>
        public void StopTalking()
        {
            if (_cycleCoroutine != null)
            {
                StopCoroutine(_cycleCoroutine);
                _cycleCoroutine = null;
            }
            ApplyMouth(_closed);
        }

        /// <summary>외부에서 mouth 텍스처를 강제로 한 번 적용 (테스트/스냅샷 용).</summary>
        public void SetMouth(Texture2D texture) => ApplyMouth(texture);

        // ── 내부 ──────────────────────────────────────────────

        private IEnumerator CycleRoutine()
        {
            while (true)
            {
                ApplyMouth(PickNext());
                float wait = Random.Range(_minInterval, Mathf.Max(_minInterval, _maxInterval));
                yield return new WaitForSeconds(wait);
            }
        }

        private Texture2D PickNext()
        {
            // 비어있는 슬롯은 자동 제외. 후보 집합에서 랜덤.
            // 같은 텍스처 연속을 피하고 싶으면 last 와 다른 후보만 시도.
            Texture2D[] candidates = { _closed, _half, _open };
            int validCount = 0;
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null) validCount++;

            if (validCount == 0) return null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                var pick = candidates[Random.Range(0, candidates.Length)];
                if (pick == null) continue;
                if (_avoidImmediateRepeat && pick == _last && validCount > 1) continue;
                return pick;
            }
            // fallback — 첫 non-null
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null) return candidates[i];
            return null;
        }

        private void ApplyMouth(Texture2D texture)
        {
            if (texture == null || _bodyRenderer == null) return;
            if (string.IsNullOrEmpty(_mouthTextureProperty)) return;

            // submaterial 인덱스 가드 — WardrobeApplier 슬롯과 일치하지 않으면 무시.
            var mats = _bodyRenderer.sharedMaterials;
            if (mats == null || _mouthMaterialIndex < 0 || _mouthMaterialIndex >= mats.Length)
                return;

            _bodyRenderer.GetPropertyBlock(_mpb, _mouthMaterialIndex);
            _mpb.SetTexture(_mouthTextureProperty, texture);
            _bodyRenderer.SetPropertyBlock(_mpb, _mouthMaterialIndex);
            _last = texture;
        }

        private void OnDisable()
        {
            // 비활성화 시 입모양 사이클 정리 — 다음 활성화 시 closed 부터 다시 시작.
            if (_cycleCoroutine != null)
            {
                StopCoroutine(_cycleCoroutine);
                _cycleCoroutine = null;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Test: Start Talking")]
        private void DbgStart() => StartTalking();

        [ContextMenu("Test: Stop Talking")]
        private void DbgStop() => StopTalking();

        [ContextMenu("Test: Apply Closed")]
        private void DbgClosed() => SetMouth(_closed);
#endif
    }
}
