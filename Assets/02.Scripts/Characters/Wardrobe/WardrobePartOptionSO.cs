using OpenDesk.Characters.Wardrobe.Expressions;
using Sirenix.OdinInspector;
using UnityEngine;

namespace OpenDesk.Characters.Wardrobe
{
    // What kind of payload an option carries. The slot itself determines which
    // kind is appropriate (Skin → Tint, Hair/Top/Bottom/Shoes → MeshSwap,
    // Eyes/Mouth → Texture). The Kind field on the option is the catalogue
    // author's declaration — WardrobeCatalogSO.OnValidate flags mismatches.
    public enum WardrobeApplyKind
    {
        MeshSwap,
        MaterialTint,
        MaterialTexture,
    }

    [CreateAssetMenu(fileName = "NewWardrobePartOption", menuName = "OpenDesk/Wardrobe/Part Option")]
    public sealed class WardrobePartOptionSO : ScriptableObject
    {
        [Title("Identity")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private Sprite _previewIcon;

        [Title("Payload")]
        [EnumToggleButtons, OnValueChanged(nameof(OnKindChanged))]
        [SerializeField] private WardrobeApplyKind _kind = WardrobeApplyKind.MeshSwap;

        [ShowIf(nameof(_kind), WardrobeApplyKind.MeshSwap)]
        [SerializeField] private GameObject _partPrefab;

        [ShowIf(nameof(_kind), WardrobeApplyKind.MaterialTint)]
        [SerializeField] private Color _tint = Color.white;

        [ShowIf(nameof(_kind), WardrobeApplyKind.MaterialTexture)]
        [SerializeField] private Texture2D _texture;

        // Optional companion to _texture, used for the Eyes slot to carry one
        // texture per AgentExpressionKey (neutral / happy / angry / closed …).
        // When set, the expression set's Default takes priority over _texture
        // at initial apply; runtime expression swaps read from the set only.
        // Mouth options can leave this null — they keep their single _texture.
        [ShowIf(nameof(_kind), WardrobeApplyKind.MaterialTexture)]
        [Tooltip("Eye options: list of expression textures (one PSD per emotion). Leave null for mouth/face overlays that don't animate.")]
        [SerializeField] private EyeExpressionSetSO _expressionSet;

        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite PreviewIcon => _previewIcon;
        public WardrobeApplyKind Kind { get => _kind; set => _kind = value; }
        public GameObject PartPrefab => _partPrefab;
        public Color Tint => _tint;
        public Texture2D Texture => _texture;
        public EyeExpressionSetSO ExpressionSet => _expressionSet;

        // Resolved texture for a given expression. Prefers the expression set
        // (so the Default PSD wins over the legacy _texture field), falls back
        // to _texture when no set is wired — keeps pre-expression eye assets
        // working without migration.
        public Texture2D ResolveExpressionTexture(AgentExpressionKey key)
        {
            if (_expressionSet != null)
            {
                var fromSet = _expressionSet.Get(key);
                if (fromSet != null) return fromSet;
            }
            return _texture;
        }

        // When the author flips Kind, zero out the now-irrelevant payload so a
        // stray Skin tint doesn't leak into a re-classified MeshSwap option.
        private void OnKindChanged()
        {
            if (_kind != WardrobeApplyKind.MeshSwap)        _partPrefab = null;
            if (_kind != WardrobeApplyKind.MaterialTint)    _tint       = Color.white;
            if (_kind != WardrobeApplyKind.MaterialTexture) { _texture = null; _expressionSet = null; }
        }

#if UNITY_EDITOR
        // Editor-only setter used by build scripts. Bypasses the OnValueChanged
        // hook because the caller already supplies a complete, consistent state.
        public void EditorSet(string id, string displayName, WardrobeApplyKind kind,
            GameObject partPrefab = null, Color? tint = null, Texture2D texture = null, Sprite icon = null,
            EyeExpressionSetSO expressionSet = null)
        {
            _id = id;
            _displayName = displayName;
            _kind = kind;
            _partPrefab = kind == WardrobeApplyKind.MeshSwap ? partPrefab : null;
            _tint = kind == WardrobeApplyKind.MaterialTint ? (tint ?? Color.white) : Color.white;
            _texture = kind == WardrobeApplyKind.MaterialTexture ? texture : null;
            _expressionSet = kind == WardrobeApplyKind.MaterialTexture ? expressionSet : null;
            _previewIcon = icon;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
