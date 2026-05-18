using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentCreationTest.Models;
using OpenDesk.Characters.Wardrobe;
using UnityEditor;
using UnityEngine;

namespace OpenDesk.Editor.Wardrobe
{
    // One-shot builder that wires the SD mannequin's parts into a usable
    // wardrobe baseline:
    //   - Creates 7 default WardrobePartOptionSO assets (skin/hair/eyes/mouth/top/bottom/shoes)
    //   - Locates the WardrobeCatalog asset (any in the project) and assigns
    //     each slot's DefaultOption
    //
    // Re-runnable: existing assets are updated in place rather than duplicated.
    public static class WardrobeDefaultsBuilder
    {
        private const string PartsRoot     = "Assets/03.Models/SD_Maneqquin 1/Parts";
        private const string OptionsFolder = "Assets/05.Prefabs/Agent/WardrobeOptions/Defaults";

        // A neutral warm-tan default — matches AgentPalette.SkinColors[1] (#EAC9A2).
        private static readonly Color DefaultSkinTint = new Color32(0xEA, 0xC9, 0xA2, 0xFF);

        [MenuItem("Tools/OpenDesk/Wardrobe/Build Default Options")]
        public static void Build()
        {
            EnsureFolder(OptionsFolder);

            // ── Locate prefab references ─────────────────────────────────
            var hairFbx   = LoadFbx($"{PartsRoot}/hair/hair.000.fbx");           // no fallback exists
            var topFbx    = LoadFbx($"{PartsRoot}/top/top.fallback.fbx");
            var bottomFbx = LoadFbx($"{PartsRoot}/bottom/bottom.fallback.fbx");
            var shoesFbx  = LoadFbx($"{PartsRoot}/shoes/shoes.fallback.fbx");

            WarnIfMissing(hairFbx,   "Parts/hair/hair.000.fbx");
            WarnIfMissing(topFbx,    "Parts/top/top.fallback.fbx");
            WarnIfMissing(bottomFbx, "Parts/bottom/bottom.fallback.fbx");
            WarnIfMissing(shoesFbx,  "Parts/shoes/shoes.fallback.fbx");

            // ── Create / update option SO assets ─────────────────────────
            var skinOpt   = UpsertOption("option_skin_default",   "Default Skin",   "skin-default",
                                         WardrobeApplyKind.MaterialTint, tint: DefaultSkinTint);

            var hairOpt   = UpsertOption("option_hair_default",   "Default Hair",   "hair-default",
                                         WardrobeApplyKind.MeshSwap, partPrefab: hairFbx);

            var eyesOpt   = UpsertOption("option_eyes_default",   "Default Eyes",   "eyes-default",
                                         WardrobeApplyKind.MaterialTexture);   // texture filled manually

            var mouthOpt  = UpsertOption("option_mouth_default",  "Default Mouth",  "mouth-default",
                                         WardrobeApplyKind.MaterialTexture);

            var topOpt    = UpsertOption("option_top_default",    "Default Top",    "top-default",
                                         WardrobeApplyKind.MeshSwap, partPrefab: topFbx);

            var bottomOpt = UpsertOption("option_bottom_default", "Default Bottom", "bottom-default",
                                         WardrobeApplyKind.MeshSwap, partPrefab: bottomFbx);

            var shoesOpt  = UpsertOption("option_shoes_default",  "Default Shoes",  "shoes-default",
                                         WardrobeApplyKind.MeshSwap, partPrefab: shoesFbx);

            // ── Wire into catalogue ──────────────────────────────────────
            var catalog = FindCatalog();
            if (catalog == null)
            {
                Debug.LogWarning("[WardrobeDefaultsBuilder] No WardrobeCatalogSO asset found in project. " +
                                 "Create one (Create → OpenDesk → Wardrobe → Catalog) and re-run.");
            }
            else
            {
                catalog.EditorSetDefault(WardrobePart.Skin,   skinOpt);
                catalog.EditorSetDefault(WardrobePart.Hair,   hairOpt);
                catalog.EditorSetDefault(WardrobePart.Eyes,   eyesOpt);
                catalog.EditorSetDefault(WardrobePart.Mouth,  mouthOpt);
                catalog.EditorSetDefault(WardrobePart.Top,    topOpt);
                catalog.EditorSetDefault(WardrobePart.Bottom, bottomOpt);
                catalog.EditorSetDefault(WardrobePart.Shoes,  shoesOpt);
                Debug.Log($"[WardrobeDefaultsBuilder] Wired defaults into '{catalog.name}'.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[WardrobeDefaultsBuilder] Done. ⚠ Eyes/Mouth options have empty Texture — " +
                      "drag a Texture2D into each before running the agent.");
        }

        // ─── Build All ────────────────────────────────────────────────

        [MenuItem("Tools/OpenDesk/Wardrobe/Build All Variants", priority = 110)]
        public static void BuildAllVariants()
        {
            BuildSkinVariants();
            BuildHairVariants();
            BuildTopVariants();
            BuildBottomVariants();
            BuildShoesVariants();
            Debug.Log("[WardrobeDefaultsBuilder] All variant slots populated.");
        }

        // ─── Skin (9 hardcoded warm tones) ────────────────────────────

        [MenuItem("Tools/OpenDesk/Wardrobe/Variants/Skin", priority = 200)]
        public static void BuildSkinVariants()
        {
            EnsureFolder(OptionsFolder);
            var palette = new (string id, string name, Color color)[]
            {
                ("skin-01-porcelain", "Porcelain", Hex("#FAEAD6")),
                ("skin-02-ivory",     "Ivory",     Hex("#F4DBC0")),
                ("skin-03-sand",      "Sand",      Hex("#EAC9A2")),
                ("skin-04-honey",     "Honey",     Hex("#D9B080")),
                ("skin-05-caramel",   "Caramel",   Hex("#C49263")),
                ("skin-06-almond",    "Almond",    Hex("#A87B5C")),
                ("skin-07-bronze",    "Bronze",    Hex("#7E5535")),
                ("skin-08-walnut",    "Walnut",    Hex("#5E3F26")),
                ("skin-09-espresso",  "Espresso",  Hex("#3F2A1A")),
            };

            var options = new List<WardrobePartOptionSO>();
            foreach (var entry in palette)
            {
                var fileName = "option_" + entry.id.Replace("-", "_");
                options.Add(UpsertOption(fileName, entry.name, entry.id,
                    WardrobeApplyKind.MaterialTint, tint: entry.color));
            }

            ReplaceCatalogOptions(WardrobePart.Skin, options);
            FlushAssets();
            Debug.Log($"[WardrobeDefaultsBuilder] Skin variants built ({options.Count}).");
        }

        // ─── Mesh-swap variants (Hair / Top / Bottom) ─────────────────

        [MenuItem("Tools/OpenDesk/Wardrobe/Variants/Hair", priority = 201)]
        public static void BuildHairVariants()
            => BuildMeshVariants(WardrobePart.Hair, $"{PartsRoot}/hair", "hair.", "hair");

        [MenuItem("Tools/OpenDesk/Wardrobe/Variants/Top", priority = 202)]
        public static void BuildTopVariants()
            => BuildMeshVariants(WardrobePart.Top, $"{PartsRoot}/top", "top.", "top");

        [MenuItem("Tools/OpenDesk/Wardrobe/Variants/Bottom", priority = 203)]
        public static void BuildBottomVariants()
            => BuildMeshVariants(WardrobePart.Bottom, $"{PartsRoot}/bottom", "bottom.", "bottom");

        [MenuItem("Tools/OpenDesk/Wardrobe/Variants/Shoes", priority = 204)]
        public static void BuildShoesVariants()
            => BuildMeshVariants(WardrobePart.Shoes, $"{PartsRoot}/shoes", "shoes.", "shoes");

        private static void BuildMeshVariants(WardrobePart part, string folder, string prefix, string idStem)
        {
            EnsureFolder(OptionsFolder);
            var entries = ScanNumberedFbx(folder, prefix);
            if (entries.Count == 0)
            {
                Debug.LogWarning($"[WardrobeDefaultsBuilder] No fbx files found under {folder} with prefix '{prefix}'.");
                return;
            }

            var options = new List<WardrobePartOptionSO>();
            foreach (var (assetPath, suffix) in entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null) continue;
                var fileName = $"option_{idStem}_{suffix}";
                var displayName = $"{Capitalise(idStem)} {suffix}";
                var id = $"{idStem}-{suffix}";
                options.Add(UpsertOption(fileName, displayName, id,
                    WardrobeApplyKind.MeshSwap, partPrefab: prefab));
            }

            ReplaceCatalogOptions(part, options);
            FlushAssets();
            Debug.Log($"[WardrobeDefaultsBuilder] {part} variants built ({options.Count}).");
        }

        // ─── helpers ────────────────────────────────────────────────────

        private static WardrobePartOptionSO UpsertOption(
            string fileName, string displayName, string id, WardrobeApplyKind kind,
            GameObject partPrefab = null, Color? tint = null, Texture2D texture = null)
        {
            var path = $"{OptionsFolder}/{fileName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<WardrobePartOptionSO>(path);
            bool created = false;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<WardrobePartOptionSO>();
                AssetDatabase.CreateAsset(asset, path);
                created = true;
            }
            asset.EditorSet(id, displayName, kind, partPrefab, tint, texture);
            Debug.Log($"[WardrobeDefaultsBuilder] {(created ? "Created" : "Updated")}: {path}");
            return asset;
        }

        private static GameObject LoadFbx(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private static WardrobeCatalogSO FindCatalog()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(WardrobeCatalogSO));
            if (guids == null || guids.Length == 0) return null;
            if (guids.Length > 1)
            {
                Debug.LogWarning($"[WardrobeDefaultsBuilder] Multiple WardrobeCatalog assets found. Using the first: " +
                                 AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            return AssetDatabase.LoadAssetAtPath<WardrobeCatalogSO>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void WarnIfMissing(Object obj, string relPath)
        {
            if (obj == null) Debug.LogWarning($"[WardrobeDefaultsBuilder] Missing source asset: {relPath}");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            var parts = folderPath.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        // Walks an asset folder looking for FBX files matching `<prefix>NNN.fbx`.
        // Skips any file whose name contains "fallback" (those are wired as
        // DefaultOption by Build Default Options). Returns sorted (path, suffix).
        private static List<(string assetPath, string suffix)> ScanNumberedFbx(string assetFolder, string prefix)
        {
            var results = new List<(string, string)>();
            if (!AssetDatabase.IsValidFolder(assetFolder)) return results;

            var guids = AssetDatabase.FindAssets("t:Model", new[] { assetFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith(prefix)) continue;
                if (fileName.IndexOf("fallback", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                var suffix = fileName.Substring(prefix.Length);
                results.Add((path, suffix));
            }
            return results.OrderBy(r => r.Item2, System.StringComparer.Ordinal).ToList();
        }

        private static void ReplaceCatalogOptions(WardrobePart part, IEnumerable<WardrobePartOptionSO> options)
        {
            var catalog = FindCatalog();
            if (catalog == null)
            {
                Debug.LogWarning("[WardrobeDefaultsBuilder] WardrobeCatalog asset not found — options created but not wired.");
                return;
            }
            catalog.EditorReplaceOptions(part, options);
        }

        private static void FlushAssets()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }

        private static string Capitalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
