using System;
using AgentCreationTest.Models;
using AgentCreationTest.Views;
using Cysharp.Threading.Tasks;
using OpenDesk.Characters.Wardrobe;
using UnityEngine;
using VContainer;

namespace OpenDesk.AgentCreation.Persistence
{
    // Listens to AgentCreationView.AgentCreated and writes the draft to disk.
    //
    // The save is fire-and-forget — the wizard completes regardless of write
    // outcome — but errors are logged so a missing catalogue or read-only disk
    // surfaces clearly during development.
    //
    // RequireComponent: Relay 가 같은 GameObject 에 자동 부착되도록 강제 — 위저드 완료 후
    // Saved 이벤트 → 씬 전환/Bridge 발행을 누락 없이 처리한다.
    [RequireComponent(typeof(AgentCreationCompletionRelay))]
    public sealed class AgentDraftSaveTrigger : MonoBehaviour
    {
        [SerializeField] private AgentCreationView _view;

        // Saved 는 후방 호환만 — 신규 구독자는 IAgentRepository.OnChanged 를 쓰세요.
        // Repository 가 Save 직후 동일한 record 를 OnChanged 로 발행하므로 정보 손실 없음.
        [Obsolete("Subscribe to IAgentRepository.OnChanged instead. Kept for backwards compatibility.")]
        public event Action<AgentDraftRecord, string> Saved;
        public event Action<AgentDraft, Exception> SaveFailed;

        private IAgentRepository _repository;
        private WardrobeCatalogSO _catalog;
        private bool _catalogReady;

        [Inject]
        public void Construct(IAgentRepository repository)
        {
            _repository = repository;
        }

        private async void OnEnable()
        {
            if (_view == null)
            {
                Debug.LogError("[AgentDraftSaveTrigger] AgentCreationView reference missing.");
                return;
            }
            if (_repository == null)
            {
                // DI 미주입(테스트 씬 등) 안전망 — 기본 경로로 폴백.
                Debug.LogWarning("[AgentDraftSaveTrigger] IAgentRepository 미주입 — 기본 경로로 폴백.");
                _repository = new AgentDraftJsonStore();
            }

            try
            {
                _catalog = await WardrobeCatalogService.GetAsync(this.GetCancellationTokenOnDestroy());
                _catalogReady = _catalog != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentDraftSaveTrigger] Catalog load failed: {ex.Message}");
            }

            _view.AgentCreated += OnAgentCreated;
        }

        private void OnDisable()
        {
            if (_view != null) _view.AgentCreated -= OnAgentCreated;
        }

        private void OnAgentCreated(AgentDraft draft)
        {
            try
            {
                if (!_catalogReady)
                {
                    Debug.LogWarning("[AgentDraftSaveTrigger] Saving without catalog — wardrobe IDs will be null.");
                }
                // hairColor 는 AgentDraft.Wardrobe.HairColor → FromDraft 가 정식 경로로 매핑하므로
                // 이전의 FindFirstObjectByType(WardrobeApplier) 우회는 제거됨.
                var record = AgentDraftRecord.FromDraft(draft, _catalog);

                var path = _repository.Save(record);
                Debug.Log($"[AgentDraftSaveTrigger] Saved {record.id} → {path} (hairColor={record.wardrobe?.hairColor ?? "(none)"})");
#pragma warning disable CS0618 // Saved 는 후방 호환용
                Saved?.Invoke(record, path);
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentDraftSaveTrigger] Save failed: {ex.Message}");
                SaveFailed?.Invoke(draft, ex);
            }
        }
    }
}
