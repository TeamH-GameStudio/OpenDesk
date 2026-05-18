using System;
using System.Collections.Generic;
using System.Threading;
using AgentCreationTest.Models;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using WardrobeModel = AgentCreationTest.Models.Wardrobe;

namespace OpenDesk.Characters.Wardrobe
{
    // Catalogue of wardrobe options keyed by WardrobePart.
    //
    // Each slot carries a list of selectable options AND a default option that
    // is used as a fallback when:
    //   - the user has not picked anything yet (initial agent appearance)
    //   - the catalogue has missing entries (in-development data)
    //
    // Resolved through Addressables under the key `WardrobeCatalog`.
    [CreateAssetMenu(fileName = "WardrobeCatalog", menuName = "OpenDesk/Wardrobe/Catalog")]
    public sealed class WardrobeCatalogSO : ScriptableObject
    {
        public const string AddressableKey = "WardrobeCatalog";

        [Serializable]
        public sealed class SlotEntry
        {
            public WardrobePart Part;
            [Tooltip("Always-on fallback. Applied when an option is null OR before the user has selected anything.")]
            public WardrobePartOptionSO DefaultOption;
            // Inline init so the inspector's "+" works the moment a SlotEntry is added.
            // Without this, Unity serialises the nested List as null and Odin/UnityUI
            // refuse to grow it.
            public List<WardrobePartOptionSO> Options = new List<WardrobePartOptionSO>();
        }

        [SerializeField] private List<SlotEntry> _slots = new List<SlotEntry>();

        [ContextMenu("Initialize 7 Slots")]
        private void InitializeSlots()
        {
            if (_slots == null) _slots = new List<SlotEntry>();
            _slots.Clear();
            foreach (WardrobePart part in Enum.GetValues(typeof(WardrobePart)))
            {
                _slots.Add(new SlotEntry { Part = part });
            }
            MarkDirty();
        }

        // Workaround for Odin Inspector intercepting the nested-list "+" button.
        // Click these in the SO inspector's ⋮ menu to materialise empty option
        // slots, then drag WardrobePartOptionSO assets into the populated rows.
        [ContextMenu("Fill 9 Empty Options Per Slot")]
        private void FillNineEmptyOptionsPerSlot()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                if (slot.Options == null) slot.Options = new List<WardrobePartOptionSO>();
                while (slot.Options.Count < 9) slot.Options.Add(null);
            }
            MarkDirty();
        }

        [ContextMenu("Add One Empty Option to Each Slot")]
        private void AddOneEmptyOptionToEachSlot()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                if (slot.Options == null) slot.Options = new List<WardrobePartOptionSO>();
                slot.Options.Add(null);
            }
            MarkDirty();
        }

        [ContextMenu("Clear All Options")]
        private void ClearAllOptions()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                slot.Options?.Clear();
            }
            MarkDirty();
        }

        // Walks every option and forces its Kind to match the slot it lives in.
        // Useful after introducing the Kind field on an existing catalogue.
        [ContextMenu("Auto-Set Option Kinds From Slot")]
        private void AutoSetOptionKinds()
        {
#if UNITY_EDITOR
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                var expected = ExpectedKindFor(slot.Part);
                if (slot.DefaultOption != null && slot.DefaultOption.Kind != expected)
                {
                    slot.DefaultOption.Kind = expected;
                    UnityEditor.EditorUtility.SetDirty(slot.DefaultOption);
                }
                if (slot.Options == null) continue;
                foreach (var opt in slot.Options)
                {
                    if (opt != null && opt.Kind != expected)
                    {
                        opt.Kind = expected;
                        UnityEditor.EditorUtility.SetDirty(opt);
                    }
                }
            }
            MarkDirty();
#endif
        }

        // Slot → expected payload kind. Skin slot expects a tint, Eyes/Mouth
        // expect texture overrides, everything else is mesh-swap.
        public static WardrobeApplyKind ExpectedKindFor(WardrobePart part)
        {
            switch (part)
            {
                case WardrobePart.Skin:  return WardrobeApplyKind.MaterialTint;
                case WardrobePart.Eyes:
                case WardrobePart.Mouth: return WardrobeApplyKind.MaterialTexture;
                default:                 return WardrobeApplyKind.MeshSwap;
            }
        }

        // Catalogue authors will inevitably drag the wrong option type into a
        // slot. Surface those mismatches as a warning rather than a silent
        // visual bug at runtime.
        private void OnValidate()
        {
            if (_slots == null) return;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                var expected = ExpectedKindFor(slot.Part);
                CheckOption(slot.Part, slot.DefaultOption, expected, "DefaultOption");
                if (slot.Options == null) continue;
                for (int i = 0; i < slot.Options.Count; i++)
                {
                    CheckOption(slot.Part, slot.Options[i], expected, $"Options[{i}]");
                }
            }
        }

        private void CheckOption(WardrobePart part, WardrobePartOptionSO option, WardrobeApplyKind expected, string where)
        {
            if (option == null) return;
            if (option.Kind != expected)
            {
                Debug.LogWarning(
                    $"[WardrobeCatalog] {part}.{where} = '{option.name}' has Kind={option.Kind} but slot expects {expected}.",
                    this);
            }
        }

        private void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        // Editor-only entry point used by build scripts to wire DefaultOption
        // and Options without poking through SerializedObject. Creates a slot
        // entry on demand if the catalogue hasn't been initialised yet.
        public void EditorSetDefault(WardrobePart part, WardrobePartOptionSO option)
        {
            var entry = EditorEnsureSlot(part);
            entry.DefaultOption = option;
            MarkDirty();
        }

        public void EditorAddOption(WardrobePart part, WardrobePartOptionSO option)
        {
            var entry = EditorEnsureSlot(part);
            if (entry.Options == null) entry.Options = new List<WardrobePartOptionSO>();
            entry.Options.Add(option);
            MarkDirty();
        }

        public void EditorReplaceOptions(WardrobePart part, IEnumerable<WardrobePartOptionSO> options)
        {
            var entry = EditorEnsureSlot(part);
            entry.Options = options != null
                ? new List<WardrobePartOptionSO>(options)
                : new List<WardrobePartOptionSO>();
            MarkDirty();
        }

        private SlotEntry EditorEnsureSlot(WardrobePart part)
        {
            if (_slots == null) _slots = new List<SlotEntry>();
            var entry = FindSlot(part);
            if (entry == null)
            {
                entry = new SlotEntry { Part = part };
                _slots.Add(entry);
            }
            return entry;
        }
#endif

        public WardrobePartOptionSO Resolve(WardrobePart part, int index)
        {
            // -1 is the explicit "none / unequip" sentinel. Skip the slot
            // entirely so callers see a null option and can short-circuit
            // (e.g. WardrobeApplier clears the MPB or unequips the prefab).
            if (index < 0) return null;
            var entry = FindSlot(part);
            if (entry == null) return null;
            if (entry.Options != null && index < entry.Options.Count)
            {
                var picked = entry.Options[index];
                if (picked != null) return picked;
            }
            return entry.DefaultOption;
        }

        public WardrobePartOptionSO GetDefault(WardrobePart part)
        {
            return FindSlot(part)?.DefaultOption;
        }

        public IReadOnlyList<WardrobePartOptionSO> GetOptions(WardrobePart part)
        {
            return FindSlot(part)?.Options;
        }

        // Yields the index of the default option inside the Options list (or 0 if missing).
        // Useful for initialising a Wardrobe model that visually matches the default outfit.
        public int IndexOfDefault(WardrobePart part)
        {
            var entry = FindSlot(part);
            if (entry == null || entry.DefaultOption == null || entry.Options == null) return 0;
            int idx = entry.Options.IndexOf(entry.DefaultOption);
            return idx >= 0 ? idx : 0;
        }

        public WardrobeModel CreateDefaultWardrobe()
        {
            return new WardrobeModel(
                skin:   IndexOfDefault(WardrobePart.Skin),
                hair:   IndexOfDefault(WardrobePart.Hair),
                eyes:   IndexOfDefault(WardrobePart.Eyes),
                mouth:  IndexOfDefault(WardrobePart.Mouth),
                top:    IndexOfDefault(WardrobePart.Top),
                bottom: IndexOfDefault(WardrobePart.Bottom),
                shoes:  IndexOfDefault(WardrobePart.Shoes));
        }

        private SlotEntry FindSlot(WardrobePart part)
        {
            if (_slots == null) return null;
            for (int i = 0; i < _slots.Count; i++)
            {
                var entry = _slots[i];
                if (entry != null && entry.Part == part) return entry;
            }
            return null;
        }
    }

    // Cached Addressables loader. Both the preview binder and the save trigger
    // need the catalogue; the static `_cached` reference avoids re-resolving
    // once any caller has finished loading.
    //
    // Note: We deliberately do NOT share an in-flight UniTask between callers.
    // UniTask is a single-await primitive — a second caller awaiting the same
    // UniTask throws "Already continuation registered". Two near-simultaneous
    // callers each issue their own Addressables.LoadAssetAsync, which is cheap
    // because Addressables internally deduplicates the underlying load.
    public static class WardrobeCatalogService
    {
        private static WardrobeCatalogSO _cached;

        public static async UniTask<WardrobeCatalogSO> GetAsync(CancellationToken cancellationToken = default)
        {
            if (_cached != null) return _cached;

            var handle = Addressables.LoadAssetAsync<WardrobeCatalogSO>(WardrobeCatalogSO.AddressableKey);
            await handle.ToUniTask(cancellationToken: cancellationToken);

            // First completed caller wins; subsequent assignments overwrite with
            // the same instance (Addressables returns the cached asset).
            _cached = handle.Result;
            return _cached;
        }

#if UNITY_EDITOR
        // Domain reload sometimes preserves statics — clear when entering play mode.
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode() { _cached = null; }
#endif
    }
}
