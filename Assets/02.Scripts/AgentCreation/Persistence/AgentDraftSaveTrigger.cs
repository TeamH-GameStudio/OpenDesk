using System;
using AgentCreationTest.Models;
using AgentCreationTest.Views;
using Cysharp.Threading.Tasks;
using OpenDesk.Characters.Wardrobe;
using UnityEngine;

namespace OpenDesk.AgentCreation.Persistence
{
    // Listens to AgentCreationView.AgentCreated and writes the draft to disk.
    //
    // The save is fire-and-forget — the wizard completes regardless of write
    // outcome — but errors are logged so a missing catalogue or read-only disk
    // surfaces clearly during development.
    public sealed class AgentDraftSaveTrigger : MonoBehaviour
    {
        [SerializeField] private AgentCreationView _view;
        [Tooltip("Override save root. Leave empty to use Application.persistentDataPath/agents.")]
        [SerializeField] private string _rootDirectoryOverride;

        public event Action<AgentDraftRecord, string> Saved;
        public event Action<AgentDraft, Exception> SaveFailed;

        private AgentDraftJsonStore _store;
        private WardrobeCatalogSO _catalog;
        private bool _catalogReady;

        private async void OnEnable()
        {
            if (_view == null)
            {
                Debug.LogError("[AgentDraftSaveTrigger] AgentCreationView reference missing.");
                return;
            }

            _store = new AgentDraftJsonStore(string.IsNullOrEmpty(_rootDirectoryOverride) ? null : _rootDirectoryOverride);

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
                var record = AgentDraftRecord.FromDraft(draft, _catalog);
                var path = _store.Save(record);
                Debug.Log($"[AgentDraftSaveTrigger] Saved {record.id} → {path}");
                Saved?.Invoke(record, path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentDraftSaveTrigger] Save failed: {ex.Message}");
                SaveFailed?.Invoke(draft, ex);
            }
        }
    }
}
