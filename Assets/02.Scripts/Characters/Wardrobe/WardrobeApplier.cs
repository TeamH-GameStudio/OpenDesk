using AgentCreationTest.Models;
using OpenDesk.Characters;
using UnityEngine;
using WardrobeModel = AgentCreationTest.Models.Wardrobe;

namespace OpenDesk.Characters.Wardrobe
{
    // Applies a Wardrobe selection onto a 3D character.
    //
    //   Hair / Top / Bottom / Shoes  → CharacterPartSwapper.EquipPart  (mesh swap, bone-remapped)
    //   Skin                          → MaterialPropertyBlock on body  (color tint)
    //   Eyes / Mouth                  → MaterialPropertyBlock on face  (texture override)
    //
    // MaterialPropertyBlocks keep the underlying shared material untouched —
    // multiple agents can share materials without colour bleeding into each other.
    public sealed class WardrobeApplier : MonoBehaviour
    {
        [Header("Mesh swap target")]
        [SerializeField] private CharacterPartSwapper _partSwapper;

        [Header("Skin tint")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private string _skinColorProperty = "_BaseColor";

        [Header("Face textures (Eyes / Mouth)")]
        [SerializeField] private Renderer _faceRenderer;
        [SerializeField] private string _eyesTextureProperty = "_EyesTex";
        [SerializeField] private string _mouthTextureProperty = "_MouthTex";

        private const string SlotHair   = "hair";
        private const string SlotTop    = "top";
        private const string SlotBottom = "bottom";
        private const string SlotShoes  = "shoes";

        [Header("Debug")]
        [SerializeField] private bool _verboseLogging = true;

        private WardrobeCatalogSO _catalog;
        private MaterialPropertyBlock _bodyMpb;
        private MaterialPropertyBlock _faceMpb;

        private void Awake()
        {
            _bodyMpb = new MaterialPropertyBlock();
            _faceMpb = new MaterialPropertyBlock();

            if (_partSwapper == null)
                Debug.LogWarning("[WardrobeApplier] Part Swapper not assigned — hair/top/bottom/shoes will not change.", this);
            if (_bodyRenderer == null)
                Debug.LogWarning("[WardrobeApplier] Body Renderer not assigned — skin tint will not apply.", this);
            if (_faceRenderer == null)
                Debug.LogWarning("[WardrobeApplier] Face Renderer not assigned — eyes/mouth textures will not apply.", this);
        }

        public void SetCatalog(WardrobeCatalogSO catalog)
        {
            _catalog = catalog;
        }

        public void Apply(WardrobeModel wardrobe)
        {
            if (_catalog == null)
            {
                Debug.LogWarning("[WardrobeApplier] Apply skipped — Catalog not set. Did SetCatalog() run?", this);
                return;
            }
            if (wardrobe == null) return;

            if (_verboseLogging)
                Debug.Log($"[WardrobeApplier] Apply skin={wardrobe.Skin} hair={wardrobe.Hair} eyes={wardrobe.Eyes} mouth={wardrobe.Mouth} top={wardrobe.Top} bottom={wardrobe.Bottom} shoes={wardrobe.Shoes}", this);

            ApplySkin(_catalog.Resolve(WardrobePart.Skin, wardrobe.Skin));
            ApplyEyes(_catalog.Resolve(WardrobePart.Eyes, wardrobe.Eyes));
            ApplyMouth(_catalog.Resolve(WardrobePart.Mouth, wardrobe.Mouth));

            ApplyMesh(SlotHair,   _catalog.Resolve(WardrobePart.Hair,   wardrobe.Hair));
            ApplyMesh(SlotTop,    _catalog.Resolve(WardrobePart.Top,    wardrobe.Top));
            ApplyMesh(SlotBottom, _catalog.Resolve(WardrobePart.Bottom, wardrobe.Bottom));
            ApplyMesh(SlotShoes,  _catalog.Resolve(WardrobePart.Shoes,  wardrobe.Shoes));
        }

        // Convenience: apply the catalogue's default outfit. Used at boot before
        // a ViewModel exists, so the agent never appears with a missing mesh.
        public void ApplyDefaults()
        {
            if (_catalog == null) return;
            Apply(_catalog.CreateDefaultWardrobe());
        }

        private void ApplySkin(WardrobePartOptionSO option)
        {
            if (option == null || _bodyRenderer == null) return;
            _bodyRenderer.GetPropertyBlock(_bodyMpb);
            _bodyMpb.SetColor(_skinColorProperty, option.Tint);
            _bodyRenderer.SetPropertyBlock(_bodyMpb);
        }

        private void ApplyEyes(WardrobePartOptionSO option)  => ApplyFaceTexture(option, _eyesTextureProperty);
        private void ApplyMouth(WardrobePartOptionSO option) => ApplyFaceTexture(option, _mouthTextureProperty);

        private void ApplyFaceTexture(WardrobePartOptionSO option, string propertyName)
        {
            if (option == null || option.Texture == null || _faceRenderer == null) return;
            if (string.IsNullOrEmpty(propertyName)) return;
            _faceRenderer.GetPropertyBlock(_faceMpb);
            _faceMpb.SetTexture(propertyName, option.Texture);
            _faceRenderer.SetPropertyBlock(_faceMpb);
        }

        private void ApplyMesh(string slotName, WardrobePartOptionSO option)
        {
            if (_partSwapper == null) return;
            if (option == null)
            {
                if (_verboseLogging) Debug.Log($"[WardrobeApplier] {slotName}: catalog returned null option → unequip.", this);
                _partSwapper.UnequipPart(slotName);
                return;
            }
            if (option.PartPrefab == null)
            {
                if (_verboseLogging) Debug.LogWarning($"[WardrobeApplier] {slotName}: option '{option.name}' has no PartPrefab.", this);
                _partSwapper.UnequipPart(slotName);
                return;
            }
            if (_verboseLogging) Debug.Log($"[WardrobeApplier] {slotName}: equipping '{option.PartPrefab.name}'.", this);
            _partSwapper.EquipPart(slotName, option.PartPrefab);
        }
    }
}
