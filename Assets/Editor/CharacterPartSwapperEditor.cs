using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenDesk.Characters;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CharacterPartSwapper))]
public class CharacterPartSwapperEditor : OdinEditor
{
    private const string PARTS_ROOT = "Assets/03.Models/SD_Maneqquin 1/Parts";


    // category -> side -> parts  (side is "" for non-shoes categories)
    private Dictionary<string, Dictionary<string, List<(string name, string path)>>> _partsMap = new();
    private string[] _categories = System.Array.Empty<string>();

    private int _selectedCategoryIdx;
    private int _selectedSideIdx;
    private int _selectedPartIdx;
    private Texture2D _cachedPreview;
    private string _cachedPreviewPath;

    protected override void OnEnable()
    {
        base.OnEnable();
        ScanParts();
    }

    public override void OnInspectorGUI()
    {
        // Odin draws default properties (bodyRenderer etc.)
        base.OnInspectorGUI();

        var swapper = (CharacterPartSwapper)target;

        EditorGUILayout.Space(10);
        SirenixEditorGUI.BeginBox("Parts Selector");

        // -- Refresh --
        if (SirenixEditorGUI.ToolbarButton(EditorIcons.Refresh))
        {
            ScanParts();
        }

        if (_categories.Length == 0)
        {
            EditorGUILayout.HelpBox("Parts folder is empty or not found.\n" + PARTS_ROOT, MessageType.Warning);
            SirenixEditorGUI.EndBox();
            return;
        }

        // -- Category dropdown --
        _selectedCategoryIdx = EditorGUILayout.Popup("Category", _selectedCategoryIdx, _categories);
        _selectedCategoryIdx = Mathf.Clamp(_selectedCategoryIdx, 0, _categories.Length - 1);

        var category = _categories[_selectedCategoryIdx];
        var sidesMap = _partsMap[category];
        var sides = sidesMap.Keys.OrderBy(s => s).ToArray();

        // -- Side dropdown (only if multiple sides exist, e.g. shoes: left/right) --
        bool hasSides = sides.Length > 1 || (sides.Length == 1 && sides[0] != "");
        if (hasSides)
        {
            _selectedSideIdx = Mathf.Clamp(_selectedSideIdx, 0, sides.Length - 1);
            _selectedSideIdx = EditorGUILayout.Popup("Side", _selectedSideIdx, sides);
        }
        else
        {
            _selectedSideIdx = 0;
        }

        var currentSide = sides[Mathf.Clamp(_selectedSideIdx, 0, sides.Length - 1)];
        var parts = sidesMap[currentSide];

        if (parts.Count == 0)
        {
            EditorGUILayout.HelpBox("No parts in this category.", MessageType.Info);
            SirenixEditorGUI.EndBox();
            return;
        }

        // -- Part dropdown --
        var partNames = parts.Select(p => p.name).ToArray();
        _selectedPartIdx = Mathf.Clamp(_selectedPartIdx, 0, parts.Count - 1);
        _selectedPartIdx = EditorGUILayout.Popup("Part", _selectedPartIdx, partNames);

        var selectedEntry = parts[_selectedPartIdx];
        var selectedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(selectedEntry.path);

        // -- Preview --
        EditorGUILayout.Space(4);
        DrawPreview(selectedAsset, selectedEntry.path);

        // -- Equip / Unequip buttons --
        // Slot name includes side for shoes (e.g. "shoes.left", "shoes.right")
        var slotName = hasSides ? $"{category}.{currentSide}" : category;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        bool isPlaying = Application.isPlaying;

        using (new EditorGUI.DisabledScope(!isPlaying || selectedAsset == null))
        {
            if (GUILayout.Button("Equip Test", GUILayout.Height(28)))
            {
                swapper.EquipPart(slotName, selectedAsset);
            }
        }

        using (new EditorGUI.DisabledScope(!isPlaying))
        {
            if (GUILayout.Button("Unequip", GUILayout.Height(28)))
            {
                swapper.UnequipPart(slotName);
            }
        }

        EditorGUILayout.EndHorizontal();

        // -- Equip Both (only for sided categories like shoes) --
        if (hasSides && sides.Length >= 2)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!isPlaying))
            {
                if (GUILayout.Button("Equip Both Sides", GUILayout.Height(24)))
                {
                    foreach (var side in sides)
                    {
                        var sideSlot = $"{category}.{side}";
                        var sideParts = sidesMap[side];
                        // Pick the same index if available, otherwise first
                        var idx = Mathf.Clamp(_selectedPartIdx, 0, sideParts.Count - 1);
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(sideParts[idx].path);
                        if (asset != null)
                            swapper.EquipPart(sideSlot, asset);
                    }
                }

                if (GUILayout.Button("Unequip Both Sides", GUILayout.Height(24)))
                {
                    foreach (var side in sides)
                        swapper.UnequipPart($"{category}.{side}");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        SirenixEditorGUI.EndBox();
    }

    private void DrawPreview(GameObject asset, string assetPath)
    {
        if (asset == null) return;

        // Cache invalidation
        if (_cachedPreviewPath != assetPath)
        {
            _cachedPreview = null;
            _cachedPreviewPath = assetPath;
        }

        var preview = AssetPreview.GetAssetPreview(asset);
        if (preview != null)
        {
            _cachedPreview = preview;
        }

        var tex = _cachedPreview ?? AssetPreview.GetMiniThumbnail(asset);

        if (tex != null)
        {
            var rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
            // Center
            rect.x = (EditorGUIUtility.currentViewWidth - 128) * 0.5f;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
        }

        // Keep repainting while preview is loading
        if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
        {
            Repaint();
        }
    }

    private void ScanParts()
    {
        _partsMap.Clear();
        _selectedPartIdx = 0;
        _selectedSideIdx = 0;

        if (!AssetDatabase.IsValidFolder(PARTS_ROOT))
        {
            _categories = System.Array.Empty<string>();
            return;
        }

        var rootFull = Path.GetFullPath(PARTS_ROOT);
        var subDirs = Directory.GetDirectories(rootFull)
            .OrderBy(d => d)
            .ToArray();

        foreach (var dir in subDirs)
        {
            var catName = Path.GetFileName(dir);
            // side -> entries
            var sidesMap = new Dictionary<string, List<(string name, string path)>>();

            var fbxFiles = Directory.GetFiles(dir, "*.fbx", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToArray();

            foreach (var fbx in fbxFiles)
            {
                var assetPath = PARTS_ROOT + fbx.Substring(rootFull.Length)
                    .Replace(Path.DirectorySeparatorChar, '/');

                var fileName = Path.GetFileNameWithoutExtension(fbx);

                // Parse side from filename: "shoes.left.000" -> side="left", display="000"
                // For non-sided files: "hair.001" -> side="", display="hair.001"
                var side = "";
                var displayName = fileName;

                var dotParts = fileName.Split('.');
                if (dotParts.Length >= 3)
                {
                    // e.g. ["shoes", "left", "000"] -> check if middle part is a side keyword
                    var candidate = dotParts[1].ToLower();
                    if (candidate == "left" || candidate == "right")
                    {
                        side = candidate;
                        // Display: everything except category prefix and side
                        // "shoes.left.000" -> "000"
                        displayName = string.Join(".", dotParts.Skip(2));
                    }
                }

                if (!sidesMap.TryGetValue(side, out var list))
                {
                    list = new List<(string name, string path)>();
                    sidesMap[side] = list;
                }

                list.Add((displayName, assetPath));
            }

            _partsMap[catName] = sidesMap;
        }

        _categories = _partsMap.Keys.OrderBy(k => k).ToArray();
        _selectedCategoryIdx = Mathf.Clamp(_selectedCategoryIdx, 0, Mathf.Max(0, _categories.Length - 1));
    }
}
