using System;
using System.Threading;
using AgentCreationTest.Models;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Persistence;
using UnityEngine;
using UnityEngine.UIElements;
using WardrobeModel = AgentCreationTest.Models.Wardrobe;

namespace OpenDesk.Characters.Wardrobe
{
    /// <summary>
    /// Loadout 모달 전용 경량 프리뷰 바인더.
    /// AgentCreationView/ViewModel 의존성을 제거한 버전.
    ///
    /// 책임:
    ///   1) WardrobeCatalog 로드 (Addressables 캐시 공유)
    ///   2) AgentDraftRecord 에서 저장된 Wardrobe(ID 기반) → WardrobeModel(index 기반) 변환
    ///   3) WardrobeApplier.Apply 호출 → RenderTexture 에 캐릭터 렌더
    ///   4) 모달의 preview-frame VisualElement 에 RenderTexture 배경 설정
    ///
    /// Inspector 작업:
    ///   - _applier: 별도 preview rig 의 WardrobeApplier 참조 (캐릭터 + 카메라 한 세트)
    ///   - _previewTexture: 위 카메라가 출력하는 RenderTexture
    /// </summary>
    public sealed class AgentLoadoutPreviewBinder : MonoBehaviour
    {
        [SerializeField] private WardrobeApplier _applier;
        [SerializeField] private RenderTexture _previewTexture;
        [SerializeField] private bool _verbose;

        private IAgentRepository _repository;
        private WardrobeCatalogSO _catalog;
        private bool _catalogLoading;

        /// <summary>호출 측이 AgentDraftJsonStore(IAgentRepository) 를 보유한 경우 주입.</summary>
        public void SetRepository(IAgentRepository repository)
        {
            _repository = repository;
        }

        /// <summary>참조한 RenderTexture (null 일 수 있음).</summary>
        public RenderTexture PreviewTexture => _previewTexture;

        /// <summary>
        /// 주어진 VisualElement 에 RenderTexture 를 배경으로 설정.
        /// _previewTexture 가 null 이면 아무 일도 하지 않는다.
        /// </summary>
        public void AttachToFrame(VisualElement frame)
        {
            if (frame == null || _previewTexture == null) return;
            frame.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_previewTexture));
        }

        /// <summary>
        /// agentId 로 저장된 AgentDraftRecord 를 조회해 Wardrobe 적용.
        /// 실패해도 throw 하지 않고 로그만 남긴다 — preview 는 부가 기능이므로 모달 전체를 막지 않는다.
        /// </summary>
        public async UniTask BindAsync(string agentId, CancellationToken ct)
        {
            if (_applier == null)
            {
                if (_verbose) Debug.LogWarning("[AgentLoadoutPreviewBinder] WardrobeApplier 미할당 — preview 비활성");
                return;
            }

            await EnsureCatalogAsync(ct);
            if (_catalog == null)
            {
                Debug.LogWarning("[AgentLoadoutPreviewBinder] WardrobeCatalog 로드 실패 — 기본 외형 유지");
                return;
            }

            _applier.SetCatalog(_catalog);

            var record = TryLoadDraftRecord(agentId);
            if (record?.wardrobe == null)
            {
                _applier.ApplyDefaults();
                if (_verbose) Debug.Log($"[AgentLoadoutPreviewBinder] '{agentId}' record/wardrobe 없음 → defaults 적용");
                return;
            }

            var wardrobe = BuildWardrobeFromRecord(_catalog, record.wardrobe);
            _applier.Apply(wardrobe);
            if (_verbose) Debug.Log($"[AgentLoadoutPreviewBinder] '{agentId}' wardrobe 적용 완료");
        }

        private async UniTask EnsureCatalogAsync(CancellationToken ct)
        {
            if (_catalog != null || _catalogLoading) return;

            try
            {
                _catalogLoading = true;
                _catalog = await WardrobeCatalogService.GetAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentLoadoutPreviewBinder] Catalog 로드 실패: {ex.Message}");
                _catalog = null;
            }
            finally
            {
                _catalogLoading = false;
            }
        }

        private AgentDraftRecord TryLoadDraftRecord(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return null;

            try
            {
                IAgentRepository repo = _repository ?? new AgentDraftJsonStore();
                return repo.Get(agentId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AgentLoadoutPreviewBinder] AgentDraftJsonStore.Get 실패 ({agentId}): {ex.Message}");
                return null;
            }
        }

        // 저장된 WardrobeRecord(ID 기반) → WardrobeModel(index 기반) 으로 역변환.
        // 일치하는 ID 가 없으면 기본 index 로 폴백.
        private static WardrobeModel BuildWardrobeFromRecord(
            WardrobeCatalogSO catalog,
            AgentDraftRecord.WardrobeRecord record)
        {
            return new WardrobeModel(
                skin:   ResolveIndex(catalog, WardrobePart.Skin,   record.skin),
                hair:   ResolveIndex(catalog, WardrobePart.Hair,   record.hair),
                eyes:   ResolveIndex(catalog, WardrobePart.Eyes,   record.eyes),
                mouth:  ResolveIndex(catalog, WardrobePart.Mouth,  record.mouth),
                top:    ResolveIndex(catalog, WardrobePart.Top,    record.top),
                bottom: ResolveIndex(catalog, WardrobePart.Bottom, record.bottom),
                shoes:  ResolveIndex(catalog, WardrobePart.Shoes,  record.shoes),
                hairColor: record.hairColor);
        }

        private static int ResolveIndex(WardrobeCatalogSO catalog, WardrobePart part, string id)
        {
            if (string.IsNullOrEmpty(id)) return catalog.IndexOfDefault(part);
            var options = catalog.GetOptions(part);
            if (options == null) return catalog.IndexOfDefault(part);
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                if (opt != null && opt.Id == id) return i;
            }
            return catalog.IndexOfDefault(part);
        }
    }
}
