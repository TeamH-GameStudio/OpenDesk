using System;
using AgentCreationTest.Models;
using OpenDesk.Characters;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEngine;
using WardrobeModel = AgentCreationTest.Models.Wardrobe;

namespace OpenDesk.Characters.Wardrobe
{
    // Applies a Wardrobe selection onto a 3D character.
    //
    //   Hair / Top / Bottom / Shoes  → CharacterPartSwapper.EquipPart   (mesh swap, bone-remapped)
    //   Skin                          → MaterialPropertyBlock on body slot   (color tint)
    //   Eyes / Mouth                  → MaterialPropertyBlock on dedicated slots   (texture override)
    //
    // Default layout assumed: a single SkinnedMeshRenderer on the head/body
    // with three submaterials — slot 0 = skin, slot 1 = eyes, slot 2 = mouth.
    // Each slot gets its own MPB write (`SetPropertyBlock(mpb, materialIndex)`)
    // so changing the skin tint does NOT bleed into the eyes/mouth textures.
    //
    // MaterialPropertyBlocks keep the underlying shared material untouched —
    // multiple agents can share materials without colour bleeding into each other.
    public sealed class WardrobeApplier : MonoBehaviour
    {
        [Header("Mesh swap target")]
        [SerializeField] private CharacterPartSwapper _partSwapper;

        [Header("Body renderer (skin/eyes/mouth share this)")]
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private int _bodyMaterialIndex  = 0;
        [SerializeField] private int _eyesMaterialIndex  = 1;
        [SerializeField] private int _mouthMaterialIndex = 2;

        [Header("Skin tint properties")]
        [SerializeField] private string _skinColorProperty = "_BaseColor";
        // Toon shader (UTS) splits skin color across base + two shade tiers.
        // Setting only _BaseColor leaves the shadow tones at their authored
        // values — limbs end up almost black in shadow zones. Real skin
        // shadows trend toward a warm reddish-brown (subsurface scattering),
        // not black. We blend the base color toward `_skinShadowTint` instead
        // of multiplying toward black, which keeps shadows believable across
        // every skin tone in the palette.
        [SerializeField] private string _skin1stShadeProperty = "_1st_ShadeColor";
        [SerializeField] private string _skin2ndShadeProperty = "_2nd_ShadeColor";
        [SerializeField] private Color _skinShadowTint = new Color(0.32f, 0.18f, 0.16f, 1f);
        [SerializeField, Range(0f, 1f)] private float _skin1stShadeBlend = 0.22f;
        [SerializeField, Range(0f, 1f)] private float _skin2ndShadeBlend = 0.45f;

        [Header("Hair tint properties")]
        [Tooltip("Material property used to tint the hair mesh. URP Lit exposes _BaseColor.")]
        [SerializeField] private string _hairColorProperty = "_BaseColor";

        [Tooltip("Default colour seeded onto the hair mesh whenever ApplyDefaults() runs. Keep in sync with the leading preset on the hair-colour rail so the matching swatch highlights at boot.")]
        [SerializeField] private Color _defaultHairColor = new Color(0.333f, 0.333f, 0.333f, 1f); // #555555 — mid grey

        [Header("Face texture properties")]
        // URP Lit (and most stock URP shaders) expose the main albedo texture
        // as `_BaseMap`. The previous defaults (`_EyesTex` / `_MouthTex`) only
        // work with a custom face shader that declares those properties — with
        // stock URP Lit they silently no-op. Override here if you swap in a
        // custom shader later.
        [SerializeField] private string _eyesTextureProperty  = "_BaseMap";
        [SerializeField] private string _mouthTextureProperty = "_BaseMap";

        private const string SlotHair   = "hair";
        private const string SlotTop    = "top";
        private const string SlotBottom = "bottom";
        private const string SlotShoes  = "shoes";

        [Header("Debug")]
        [SerializeField] private bool _verboseLogging = true;

        private WardrobeCatalogSO _catalog;
        // One MPB per submaterial slot — keeps writes isolated so a skin tint
        // applied to slot 0 cannot stomp the eye/mouth textures on slots 1/2.
        //
        // Lazy-initialized via the SkinMpb/EyesMpb/MouthMpb/HairMpb accessors.
        // Unity 2022.3 forbids `new MaterialPropertyBlock()` inside a MonoBehaviour
        // ctor (Object.Instantiate triggers field initializers, see UnityException
        // "CreateImpl is not allowed to be called from a MonoBehaviour constructor").
        // Lazy init also covers the Awake-late race on nested FBX models where
        // OnEnable-time Apply calls could otherwise hit a null MPB.
        private MaterialPropertyBlock _skinMpb;
        private MaterialPropertyBlock _eyesMpb;
        private MaterialPropertyBlock _mouthMpb;

        private MaterialPropertyBlock SkinMpb  => _skinMpb  ??= new MaterialPropertyBlock();
        private MaterialPropertyBlock EyesMpb  => _eyesMpb  ??= new MaterialPropertyBlock();
        private MaterialPropertyBlock MouthMpb => _mouthMpb ??= new MaterialPropertyBlock();

        // Currently equipped eye option — cached so SetEyeExpression() can look
        // up the expression set without re-resolving through the catalogue.
        private WardrobePartOptionSO _currentEyesOption;
        private AgentExpressionKey _currentExpression = AgentExpressionKey.Default;

        // Fires whenever ApplyEyes() resolves a different option than what was
        // previously equipped. Action-rail UIs subscribe to this so the
        // expression button list mirrors *whichever* PSDs the new eye option
        // actually carries instead of a fixed hardcoded set.
        public event Action<WardrobePartOptionSO> EyesOptionChanged;

        public WardrobePartOptionSO CurrentEyesOption => _currentEyesOption;

        // Hair color survives mesh swaps — EquipPart destroys the old
        // renderer, so the MPB written previously goes with it. We cache the
        // chosen colour and re-write it onto the freshly-equipped hair
        // renderer at the end of every ApplyMesh("hair", …) call.
        private Color? _currentHairColor;
        private MaterialPropertyBlock _hairMpb;
        private MaterialPropertyBlock HairMpb => _hairMpb ??= new MaterialPropertyBlock();

        public Color? CurrentHairColor => _currentHairColor;

        private void Awake()
        {
            // MPB 들은 필드 초기화로 옮김 — Awake 타이밍 race 방지.
            if (_partSwapper == null)
                Debug.LogWarning("[WardrobeApplier] Part Swapper not assigned — hair/top/bottom/shoes will not change.", this);
            if (_bodyRenderer == null)
                Debug.LogWarning("[WardrobeApplier] Body Renderer not assigned — skin/eyes/mouth will not change.", this);
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

            var eyesOption = _catalog.Resolve(WardrobePart.Eyes, wardrobe.Eyes);
            ApplyEyes(eyesOption);

            // If the eye option's expression set carries paired mouth PSDs,
            // ApplyEyes already wrote the matching mouth texture for the
            // current expression — skipping the catalogue Mouth option keeps
            // it from overwriting that. Otherwise fall back to the static
            // Mouth pick from the wardrobe catalogue.
            if (!ExpressionOwnsMouth(eyesOption))
                ApplyMouth(_catalog.Resolve(WardrobePart.Mouth, wardrobe.Mouth));

            ApplyMesh(SlotHair,   _catalog.Resolve(WardrobePart.Hair,   wardrobe.Hair));
            ApplyMesh(SlotTop,    _catalog.Resolve(WardrobePart.Top,    wardrobe.Top));
            ApplyMesh(SlotBottom, _catalog.Resolve(WardrobePart.Bottom, wardrobe.Bottom));
            ApplyMesh(SlotShoes,  _catalog.Resolve(WardrobePart.Shoes,  wardrobe.Shoes));
        }

        // Convenience: apply the catalogue's default outfit. Used at boot before
        // a ViewModel exists, so the agent never appears with a missing mesh.
        // Seeds the default hair colour first so Apply() picks it up when the
        // hair mesh is equipped — without this the prefab's authored colour
        // would flash for one frame before the user clicks a swatch.
        public void ApplyDefaults()
        {
            if (_catalog == null) return;
            if (_currentHairColor == null) _currentHairColor = _defaultHairColor;
            Apply(_catalog.CreateDefaultWardrobe());
        }

        private void ApplySkin(WardrobePartOptionSO option)
        {
            if (_bodyRenderer == null) return;
            var skinMpb = SkinMpb;
            _bodyRenderer.GetPropertyBlock(skinMpb, _bodyMaterialIndex);
            if (option == null)
            {
                // "None" — strip our tint overrides so the material's authored
                // colours come back. Clear() removes every property previously
                // written into this MPB; SetPropertyBlock with the empty block
                // applies the reset to the renderer slot.
                skinMpb.Clear();
            }
            else
            {
                var baseColor = option.Tint;
                skinMpb.SetColor(_skinColorProperty, baseColor);
                if (!string.IsNullOrEmpty(_skin1stShadeProperty))
                    skinMpb.SetColor(_skin1stShadeProperty, BlendShade(baseColor, _skin1stShadeBlend));
                if (!string.IsNullOrEmpty(_skin2ndShadeProperty))
                    skinMpb.SetColor(_skin2ndShadeProperty, BlendShade(baseColor, _skin2ndShadeBlend));
            }
            _bodyRenderer.SetPropertyBlock(skinMpb, _bodyMaterialIndex);
        }

        // Lerp from baseColor toward _skinShadowTint by `t`. Mimics how real
        // skin shadows pick up warmth from subsurface scattering instead of
        // collapsing to black, so the result reads as natural skin across the
        // whole palette (porcelain → espresso).
        private Color BlendShade(Color baseCol, float t)
        {
            var s = Color.Lerp(baseCol, _skinShadowTint, t);
            s.a = baseCol.a;
            return s;
        }

        private void ApplyEyes(WardrobePartOptionSO option)
        {
            // Cache the option so SetEyeExpression() can swap textures later
            // without having to walk the catalogue again.
            bool changed = !ReferenceEquals(_currentEyesOption, option);
            _currentEyesOption = option;

            var eyeTex = option != null
                ? option.ResolveExpressionTexture(_currentExpression)
                : null;
            ApplyFaceTexture(eyeTex, _eyesTextureProperty, EyesMpb, _eyesMaterialIndex);

            // Paired mouth — only when the expression set ships a mouth
            // texture for this key (or its Default fallback). If null, the
            // caller (Apply / SetEyeExpression) leaves the mouth slot to the
            // catalogue's static Mouth option instead.
            var mouthTex = ResolveExpressionMouth(option, _currentExpression);
            if (mouthTex != null)
                ApplyFaceTexture(mouthTex, _mouthTextureProperty, MouthMpb, _mouthMaterialIndex);

            if (changed) EyesOptionChanged?.Invoke(option);
        }

        private void ApplyMouth(WardrobePartOptionSO option)
        {
            // Mouth keeps its single-texture model — no expression set.
            ApplyFaceTexture(option?.Texture, _mouthTextureProperty, MouthMpb, _mouthMaterialIndex);
        }

        // Swap the eye texture (and paired mouth, if authored) to the PSDs
        // mapped to `key` on the currently equipped eye option. No-op when no
        // eye option is equipped yet. Legacy single-texture eye assets keep
        // their static look. Falls back to the set's Default entry when `key`
        // isn't authored.
        public void SetEyeExpression(AgentExpressionKey key)
        {
            _currentExpression = key;
            if (_currentEyesOption == null || _bodyRenderer == null) return;

            var eyeTex = _currentEyesOption.ResolveExpressionTexture(key);
            if (eyeTex != null)
                ApplyFaceTexture(eyeTex, _eyesTextureProperty, EyesMpb, _eyesMaterialIndex);

            var mouthTex = ResolveExpressionMouth(_currentEyesOption, key);
            if (mouthTex != null)
                ApplyFaceTexture(mouthTex, _mouthTextureProperty, MouthMpb, _mouthMaterialIndex);
        }

        // True when the expression set on this eye option authors at least one
        // mouth texture across any key — used by Apply() to decide whether the
        // expression system owns the mouth slot.
        private static bool ExpressionOwnsMouth(WardrobePartOptionSO eyesOption)
        {
            return eyesOption != null
                && eyesOption.ExpressionSet != null
                && eyesOption.ExpressionSet.HasAnyMouth();
        }

        private static Texture2D ResolveExpressionMouth(WardrobePartOptionSO eyesOption, AgentExpressionKey key)
        {
            if (eyesOption == null || eyesOption.ExpressionSet == null) return null;
            return eyesOption.ExpressionSet.GetMouth(key);
        }

        public AgentExpressionKey CurrentExpression => _currentExpression;

        private void ApplyFaceTexture(Texture2D texture, string propertyName,
                                      MaterialPropertyBlock mpb, int materialIndex)
        {
            if (texture == null || _bodyRenderer == null) return;
            if (string.IsNullOrEmpty(propertyName)) return;
            _bodyRenderer.GetPropertyBlock(mpb, materialIndex);
            mpb.SetTexture(propertyName, texture);
            _bodyRenderer.SetPropertyBlock(mpb, materialIndex);
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

            // Re-apply cached overrides that were lost when the old renderer
            // was destroyed. Hair colour is the only one for now — other
            // slots can grow into the same pattern as they pick up tints.
            if (slotName == SlotHair) ApplyHairColor();
        }

        // Recolours every submaterial on the currently equipped hair renderer.
        // No-op until SetHairColor has been called at least once — that keeps
        // the prefab's authored material colour intact for users who never
        // touch the hair-color rail.
        public void SetHairColor(Color color)
        {
            _currentHairColor = color;
            ApplyHairColor();
        }

        // Clears the override so the next hair mesh swap (or render frame)
        // reverts to the prefab's authored material colour. Useful when the
        // UI exposes a "기본 색상" reset pill.
        public void ClearHairColor()
        {
            _currentHairColor = null;
            if (_partSwapper == null) return;
            if (!_partSwapper.TryGetEquippedRenderer(SlotHair, out var hairRenderer)) return;
            if (hairRenderer == null) return;
            // Writing a fresh empty MPB to every submaterial drops the prior
            // _BaseColor override, letting the authored material colour come
            // through again.
            var hairMpb = HairMpb;
            for (int i = 0; i < hairRenderer.sharedMaterials.Length; i++)
            {
                hairMpb.Clear();
                hairRenderer.SetPropertyBlock(hairMpb, i);
            }
        }

        private void ApplyHairColor()
        {
            if (_currentHairColor == null) return;
            if (_partSwapper == null) return;
            if (string.IsNullOrEmpty(_hairColorProperty)) return;
            if (!_partSwapper.TryGetEquippedRenderer(SlotHair, out var hairRenderer)) return;
            if (hairRenderer == null) return;

            // Hair prefabs occasionally split into multiple submaterials (e.g.
            // strand + tie). Tint every slot so the colour doesn't end up half-
            // applied. MPBs are per-renderer-per-slot, so writes don't bleed.
            var color = _currentHairColor.Value;
            var materials = hairRenderer.sharedMaterials;
            int count = materials != null ? materials.Length : 1;
            var hairMpb = HairMpb;
            for (int i = 0; i < count; i++)
            {
                hairRenderer.GetPropertyBlock(hairMpb, i);
                hairMpb.SetColor(_hairColorProperty, color);
                hairRenderer.SetPropertyBlock(hairMpb, i);
            }
        }
    }
}
