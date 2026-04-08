using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Presentation.Camera
{
    /// <summary>
    /// OfficeWall 레이어의 벽이 에이전트를 가리면 반투명으로 만든다.
    ///
    /// 방식: 카메라 기준으로 벽과 에이전트의 depth를 비교.
    /// 벽이 에이전트보다 카메라에 가까우면 (= 가리면) 반투명 처리.
    /// Orthographic/Perspective 모두 동작.
    /// </summary>
    public class WallFadeController : MonoBehaviour
    {
        [Header("설정")]
        [Tooltip("벽 투명도 (0=완전투명, 1=불투명)")]
        [SerializeField] private float _fadeAlpha = 0.15f;

        [Tooltip("투명/복원 전환 속도")]
        [SerializeField] private float _fadeSpeed = 8f;

        [Tooltip("에이전트 주변 감지 범위")]
        [SerializeField] private float _agentCheckRadius = 3f;

        [Tooltip("depth 비교 여유값 (벽과 에이전트 depth가 비슷할때 blocking 판정)")]
        [SerializeField] private float _depthMargin = 0.5f;

        private UnityEngine.Camera _mainCamera;
        private int _wallLayer;
        private Renderer[] _wallRenderers;
        private readonly Dictionary<Renderer, FadeState> _fadingWalls = new();
        private readonly List<Renderer> _removeList = new();
        private bool _wallsCached;
        private bool _debugLogged;

        /// <summary>디버그 로그 다시 출력 (포커스 등 상태 변경 시 호출)</summary>
        public void DebugNextFrame() => _debugLogged = false;

        private struct FadeState
        {
            public float TargetAlpha;
            public float CurrentAlpha;
            public Material[] OriginalMaterials;
            public Material[] FadeMaterials;
        }

        private void Start()
        {
            _mainCamera = UnityEngine.Camera.main;
            _wallLayer = LayerMask.NameToLayer("OfficeWall");

            if (_wallLayer < 0)
                Debug.LogWarning("[WallFade] OfficeWall 레이어를 찾을 수 없음");
        }

        private void CacheWallRenderers()
        {
            if (_wallsCached) return;
            _wallsCached = true;

            // 씬의 모든 Renderer를 검색 — 본인 또는 부모가 OfficeWall 레이어면 포함
            var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var list = new List<Renderer>();
            foreach (var r in allRenderers)
            {
                if (IsInWallLayer(r.transform))
                    list.Add(r);
            }
            _wallRenderers = list.ToArray();
            Debug.Log($"[WallFade] OfficeWall Renderer {_wallRenderers.Length}개 캐싱");
        }

        /// <summary>본인 또는 부모 중 OfficeWall 레이어가 있는지 확인</summary>
        private bool IsInWallLayer(Transform t)
        {
            var current = t;
            while (current != null)
            {
                if (current.gameObject.layer == _wallLayer)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (_mainCamera == null || _wallLayer < 0) return;

            CacheWallRenderers();
            if (_wallRenderers == null || _wallRenderers.Length == 0) return;

            var agents = FindObjectsByType<Character.AgentCharacterController>(FindObjectsSortMode.None);
            if (agents.Length == 0) return;

            var camTransform = _mainCamera.transform;
            var camForward = camTransform.forward;
            var camPos = camTransform.position;

            // 에이전트들의 카메라 기준 depth 수집
            var agentPositions = new List<Vector3>();
            foreach (var agent in agents)
                agentPositions.Add(agent.transform.position + Vector3.up * 0.5f);

            // 이번 프레임에 가리는 벽
            var blockingThisFrame = new HashSet<Renderer>();

            foreach (var wallRenderer in _wallRenderers)
            {
                if (wallRenderer == null) continue;

                var wallBounds = wallRenderer.bounds;

                foreach (var agentPos in agentPositions)
                {
                    // 카메라 forward 방향 기준 depth 비교
                    float wallDepth = Vector3.Dot(wallBounds.ClosestPoint(agentPos) - camPos, camForward);
                    float agentDepth = Vector3.Dot(agentPos - camPos, camForward);

                    // 벽이 에이전트보다 카메라에 가깝거나 비슷하면 = 가리고 있음
                    bool isBlocking = wallDepth < agentDepth + _depthMargin;

                    // XZ 거리도 미리 계산하여 디버그
                    var closestOnWall = wallBounds.ClosestPoint(agentPos);
                    var xzDist = Vector2.Distance(
                        new Vector2(closestOnWall.x, closestOnWall.z),
                        new Vector2(agentPos.x, agentPos.z));

                    if (!_debugLogged)
                    {
                        Debug.Log($"[WallFade] 벽={wallRenderer.name} wallDepth={wallDepth:F2} agentDepth={agentDepth:F2} blocking={isBlocking} " +
                                  $"xzDist={xzDist:F2} radius={_agentCheckRadius} finalBlock={isBlocking && xzDist < _agentCheckRadius}");
                    }

                    if (isBlocking && xzDist < _agentCheckRadius)
                    {
                        blockingThisFrame.Add(wallRenderer);
                        break;
                    }
                }
            }

            // Fade 처리
            foreach (var r in blockingThisFrame)
            {
                if (!_fadingWalls.ContainsKey(r))
                {
                    var origMats = r.sharedMaterials;
                    var fadeMats = new Material[origMats.Length];
                    for (int i = 0; i < origMats.Length; i++)
                    {
                        fadeMats[i] = new Material(origMats[i]);
                        SetMaterialTransparent(fadeMats[i]);
                    }
                    r.materials = fadeMats;

                    _fadingWalls[r] = new FadeState
                    {
                        TargetAlpha = _fadeAlpha,
                        CurrentAlpha = 1f,
                        OriginalMaterials = origMats,
                        FadeMaterials = fadeMats,
                    };
                }
                else
                {
                    var state = _fadingWalls[r];
                    state.TargetAlpha = _fadeAlpha;
                    _fadingWalls[r] = state;
                }
            }

            // 알파 보간 + 복원
            _removeList.Clear();
            var keys = new List<Renderer>(_fadingWalls.Keys);
            foreach (var r in keys)
            {
                var state = _fadingWalls[r];

                if (!blockingThisFrame.Contains(r))
                    state.TargetAlpha = 1f;

                state.CurrentAlpha = Mathf.MoveTowards(
                    state.CurrentAlpha, state.TargetAlpha, _fadeSpeed * Time.deltaTime);

                if (state.FadeMaterials != null)
                {
                    foreach (var mat in state.FadeMaterials)
                    {
                        if (mat == null) continue;
                        // URP: _BaseColor, Standard: _Color
                        if (mat.HasProperty("_BaseColor"))
                        {
                            var c = mat.GetColor("_BaseColor");
                            c.a = state.CurrentAlpha;
                            mat.SetColor("_BaseColor", c);
                        }
                        else
                        {
                            var c = mat.color;
                            c.a = state.CurrentAlpha;
                            mat.color = c;
                        }
                    }
                }

                _fadingWalls[r] = state;

                // 완전 복원 시 원래 머티리얼로 교체
                if (state.CurrentAlpha >= 0.99f && state.TargetAlpha >= 1f)
                {
                    if (r != null)
                        r.sharedMaterials = state.OriginalMaterials;

                    if (state.FadeMaterials != null)
                        foreach (var mat in state.FadeMaterials)
                            if (mat != null) Destroy(mat);

                    _removeList.Add(r);
                }
            }

            foreach (var r in _removeList)
                _fadingWalls.Remove(r);

            _debugLogged = true;
        }

        /// <summary>머티리얼을 Transparent 모드로 전환 (URP Lit)</summary>
        private static void SetMaterialTransparent(Material mat)
        {
            // URP Lit Surface Type
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent

            // Alpha Blending
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f); // 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply

            // ZWrite off
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            // SrcBlend / DstBlend for alpha transparency
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");

            // 셰이더 패스 갱신을 위해 SetPass 호출
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }

        /// <summary>벽 캐시 갱신 (런타임에 벽이 추가/제거된 경우)</summary>
        public void RefreshWallCache()
        {
            _wallsCached = false;
        }

        private void OnDestroy()
        {
            foreach (var kv in _fadingWalls)
            {
                if (kv.Key != null)
                    kv.Key.sharedMaterials = kv.Value.OriginalMaterials;
                if (kv.Value.FadeMaterials != null)
                    foreach (var mat in kv.Value.FadeMaterials)
                        if (mat != null) Destroy(mat);
            }
            _fadingWalls.Clear();
        }
    }
}
