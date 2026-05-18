using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenDesk.Characters.Wardrobe.Expressions
{
    // Per-eye-style facial expression library.
    //
    // Authored as a list of (key, eye-texture, mouth-texture) entries. One
    // expression key carries BOTH the eye PSD and the matching mouth PSD so
    // that an emotion lives as a single coherent face — picking "happy" swaps
    // eyebrows AND smile in one click. Mouth texture is optional: leave it
    // null and the catalogue's static Mouth option keeps showing through.
    //
    // One asset per eye style. Reference it from a WardrobePartOptionSO whose
    // Kind = MaterialTexture. WardrobeApplier writes Texture / MouthTexture to
    // the eyes (submaterial 1) / mouth (submaterial 2) slots respectively.
    [CreateAssetMenu(
        fileName = "EyeExpressionSet",
        menuName = "OpenDesk/Wardrobe/Eye Expression Set")]
    public sealed class EyeExpressionSetSO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AgentExpressionKey Key;
            [Tooltip("Eye PSD — written to the eyes submaterial slot (1).")]
            public Texture2D Texture;
            [Tooltip("Optional. Mouth PSD that pairs with this eye — written to the mouth submaterial slot (2). Leave null to keep the catalogue's static mouth option visible for this expression.")]
            public Texture2D MouthTexture;
        }

        [SerializeField]
        [Tooltip("One entry per expression PSD. Authors MUST provide a Default entry; everything else is optional and falls back to Default at lookup time.")]
        private List<Entry> _expressions = new List<Entry>();

        public IReadOnlyList<Entry> Expressions => _expressions;

        // Returns the texture mapped to `key`, or the Default texture if `key`
        // is missing. Returns null only when the set is completely empty.
        public Texture2D Get(AgentExpressionKey key)
        {
            if (_expressions == null || _expressions.Count == 0) return null;

            Texture2D fallback = null;
            for (int i = 0; i < _expressions.Count; i++)
            {
                var entry = _expressions[i];
                if (entry.Texture == null) continue;
                if (entry.Key == key) return entry.Texture;
                if (entry.Key == AgentExpressionKey.Default) fallback = entry.Texture;
            }
            return fallback;
        }

        public Texture2D Default => Get(AgentExpressionKey.Default);

        public bool Has(AgentExpressionKey key)
        {
            if (_expressions == null) return false;
            for (int i = 0; i < _expressions.Count; i++)
            {
                if (_expressions[i].Key == key && _expressions[i].Texture != null) return true;
            }
            return false;
        }

        // Mouth-side counterpart to Get(). Mirrors the same fallback rule:
        // exact key wins, Default's mouth is the fallback, null only when
        // the set has no mouth texture authored for any key. Returns null
        // when only eye textures are authored — caller (WardrobeApplier)
        // then falls back to the catalogue's static Mouth option.
        public Texture2D GetMouth(AgentExpressionKey key)
        {
            if (_expressions == null || _expressions.Count == 0) return null;

            Texture2D fallback = null;
            for (int i = 0; i < _expressions.Count; i++)
            {
                var entry = _expressions[i];
                if (entry.MouthTexture == null) continue;
                if (entry.Key == key) return entry.MouthTexture;
                if (entry.Key == AgentExpressionKey.Default) fallback = entry.MouthTexture;
            }
            return fallback;
        }

        // True when the set carries a mouth texture for ANY key — used by the
        // applier to decide whether the expression system owns the mouth slot
        // or whether the catalogue's static Mouth option still drives it.
        public bool HasAnyMouth()
        {
            if (_expressions == null) return false;
            for (int i = 0; i < _expressions.Count; i++)
            {
                if (_expressions[i].MouthTexture != null) return true;
            }
            return false;
        }

        // Catalogue authors will inevitably forget the Default entry — surface
        // it in the editor instead of failing silently at runtime.
        private void OnValidate()
        {
            if (_expressions == null || _expressions.Count == 0) return;
            if (!Has(AgentExpressionKey.Default))
            {
                Debug.LogWarning(
                    $"[EyeExpressionSet] '{name}' is missing a Default entry — runtime lookups will return null.",
                    this);
            }
        }
    }
}
