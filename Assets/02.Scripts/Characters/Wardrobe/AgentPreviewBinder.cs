using System;
using System.ComponentModel;
using AgentCreationTest.ViewModels;
using AgentCreationTest.Views;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace OpenDesk.Characters.Wardrobe
{
    // Bridges AgentCreationView (UI Toolkit) ⇄ 3D character (WardrobeApplier).
    //
    //   1. Loads the WardrobeCatalog via Addressables on enable
    //   2. Applies the catalogue's default outfit immediately (so the agent
    //      never appears with missing meshes during the brief window before
    //      the wizard's first wardrobe interaction)
    //   3. Subscribes to the View's ViewModel.PropertyChanged and re-applies
    //      whenever Wardrobe changes
    //   4. Pushes the configured RenderTexture into the View so the 3D
    //      preview shows in the .avatar-frame area
    public sealed class AgentPreviewBinder : MonoBehaviour
    {
        [SerializeField] private AgentCreationView _view;
        [SerializeField] private WardrobeApplier _applier;
        [SerializeField] private RenderTexture _previewTexture;

        private AgentCreationViewModel _viewModel;
        private WardrobeCatalogSO _catalog;

        private async void OnEnable()
        {
            if (_view == null || _applier == null)
            {
                Debug.LogError("[AgentPreviewBinder] View or applier reference missing.");
                return;
            }

            try
            {
                _catalog = await WardrobeCatalogService.GetAsync(this.GetCancellationTokenOnDestroy());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentPreviewBinder] Failed to load WardrobeCatalog: {ex.Message}");
                return;
            }

            if (_catalog == null)
            {
                Debug.LogError("[AgentPreviewBinder] WardrobeCatalog asset returned null.");
                return;
            }

            Debug.Log($"[AgentPreviewBinder] Catalog loaded: {_catalog.name}", this);
            _applier.SetCatalog(_catalog);
            _applier.ApplyDefaults();

            if (_previewTexture != null) _view.SetPreviewTexture(_previewTexture);

            _view.ViewModelReady += OnViewModelReady;
            if (_view.ViewModel != null) OnViewModelReady(_view.ViewModel);
        }

        private void OnDisable()
        {
            if (_view != null) _view.ViewModelReady -= OnViewModelReady;
            UnbindViewModel();
        }

        private void OnViewModelReady(AgentCreationViewModel viewModel)
        {
            UnbindViewModel();
            _viewModel = viewModel;
            if (_viewModel == null) return;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            // Tell the View how many cells to render per slot, based on catalog truth.
            if (_catalog != null)
            {
                _viewModel.SetOptionCountResolver(part =>
                {
                    var opts = _catalog.GetOptions(part);
                    return opts != null ? opts.Count : 0;
                });
            }
            ApplyCurrent();
        }

        private void UnbindViewModel()
        {
            if (_viewModel == null) return;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AgentCreationViewModel.Wardrobe))
            {
                ApplyCurrent();
            }
        }

        private void ApplyCurrent()
        {
            if (_viewModel == null || _applier == null) return;
            _applier.Apply(_viewModel.Wardrobe);
        }
    }
}
