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

        // Optional rotation pivot. When unset, defaults to applier.transform.parent
        // (typically CharacterMount) so the model rig itself stays at identity —
        // any Animator on the model can update without fighting the drag.
        [SerializeField] private Transform _rotationPivot;
        [SerializeField] private float _rotationSensitivity = 0.35f;

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
            _view.PreviewDragged += OnPreviewDragged;
            if (_view.ViewModel != null) OnViewModelReady(_view.ViewModel);
        }

        private void OnDisable()
        {
            if (_view != null)
            {
                _view.ViewModelReady -= OnViewModelReady;
                _view.PreviewDragged -= OnPreviewDragged;
            }
            UnbindViewModel();
        }

        private void OnPreviewDragged(Vector2 delta)
        {
            var target = ResolveRotationTarget();
            if (target == null) return;
            // Drag right (delta.x > 0) → character rotates so its right side
            // turns away from the camera. Inspector can flip via negative
            // sensitivity if the preview camera faces the rig from -Z instead.
            target.Rotate(0f, -delta.x * _rotationSensitivity, 0f, Space.World);
        }

        // Pivot priority: explicit serialized pivot > applier's parent (Mount) >
        // applier itself. Rotating the Mount keeps the model's local transform
        // at identity, so any Animator on the model doesn't fight the drag.
        private Transform ResolveRotationTarget()
        {
            if (_rotationPivot != null) return _rotationPivot;
            if (_applier == null) return null;
            return _applier.transform.parent != null
                ? _applier.transform.parent
                : _applier.transform;
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
