using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// Face Swap 컨트롤러 — Material의 BaseMap 텍스처를 런타임에 교체하여 표정 변경.
    /// SD_Maneqquin 모델의 mtl_face 머티리얼 슬롯을 대상으로 한다.
    /// </summary>
    public class FaceSwapController : MonoBehaviour, Context.IExpressionController
    {
        [Header("Target")]
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private int _faceMaterialIndex;

        [Header("Face Textures")]
        [SerializeField] private Texture2D _faceDefault;
        [SerializeField] private Texture2D _faceSmile;
        [SerializeField] private Texture2D _faceError;
        [SerializeField] private Texture2D _faceSad;
        [SerializeField] private Texture2D _faceSleeping;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private Dictionary<string, Texture2D> _faceMap;
        private string _currentFaceId = "face_default";
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();

            _faceMap = new Dictionary<string, Texture2D>
            {
                { "face_default", _faceDefault },
                { "face_smile", _faceSmile },
                { "face_error", _faceError },
                { "face_sad", _faceSad },
                { "face_sleeping", _faceSleeping },
            };

            // Inspector에서 default 미지정 시 현재 머티리얼의 텍스처를 기본값으로 저장
            if (_faceDefault == null && _targetRenderer != null)
            {
                var mats = _targetRenderer.sharedMaterials;
                if (_faceMaterialIndex < mats.Length && mats[_faceMaterialIndex] != null)
                {
                    _faceDefault = mats[_faceMaterialIndex].GetTexture(BaseMapId) as Texture2D;
                    _faceMap["face_default"] = _faceDefault;
                }
            }
        }

        /// <summary>표정 변경. faceId: face_default, face_smile, face_error, face_sad, face_sleeping</summary>
        public void SetFace(string faceId)
        {
            if (string.IsNullOrEmpty(faceId)) return;
            if (_targetRenderer == null)
            {
                Debug.LogWarning("[FaceSwap] targetRenderer is null");
                return;
            }

            if (!_faceMap.TryGetValue(faceId, out var tex))
            {
                Debug.LogWarning($"[FaceSwap] Unknown faceId: {faceId} -- keeping current face");
                return;
            }

            if (tex == null)
            {
                Debug.LogWarning($"[FaceSwap] Texture not assigned for faceId: {faceId}");
                return;
            }

            _currentFaceId = faceId;

            // MaterialPropertyBlock 사용으로 머티리얼 인스턴스 생성 방지 (GC 절약)
            _targetRenderer.GetPropertyBlock(_propBlock, _faceMaterialIndex);
            _propBlock.SetTexture(BaseMapId, tex);
            _targetRenderer.SetPropertyBlock(_propBlock, _faceMaterialIndex);
        }

        /// <summary>기본 표정으로 복원</summary>
        public void ResetToDefault() => SetFace("face_default");

        public string CurrentFaceId => _currentFaceId;

        // ── IExpressionController 구현 ──────────────────────────────────────
        // 기존 State 코드와의 호환성 유지

        void Context.IExpressionController.SetExpression(string expressionName)
        {
            // 기존 State에서 호출하는 expressionName → faceId 매핑
            switch (expressionName)
            {
                case "Happy":
                    SetFace("face_smile");
                    break;
                case "Sad":
                    SetFace("face_sad");
                    break;
                case "Error":
                    SetFace("face_error");
                    break;
                case "Sleeping":
                    SetFace("face_sleeping");
                    break;
                case "Neutral":
                case "Focused":
                case "Puzzled":
                    SetFace("face_default");
                    break;
                default:
                    SetFace("face_default");
                    break;
            }
        }

        void Context.IExpressionController.PlayEffect(string effectName)
        {
            // 향후 VFX 연동용 (현재 미구현)
        }

#if UNITY_EDITOR
        [ContextMenu("Test: Default")]
        private void DbgDefault() => SetFace("face_default");
        [ContextMenu("Test: Smile")]
        private void DbgSmile() => SetFace("face_smile");
        [ContextMenu("Test: Error")]
        private void DbgError() => SetFace("face_error");
        [ContextMenu("Test: Sad")]
        private void DbgSad() => SetFace("face_sad");
        [ContextMenu("Test: Sleeping")]
        private void DbgSleeping() => SetFace("face_sleeping");
#endif
    }
}
