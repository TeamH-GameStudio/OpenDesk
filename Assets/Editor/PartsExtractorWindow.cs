using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

public class PartsExtractorWindow : OdinEditorWindow
{
    private const string PARTS_ROOT = "Assets/03.Models/SD_Maneqquin 1/Parts";

    private List<FbxEntry> _entries = new();
    private Vector2 _scroll;
    private bool _selectAll = true;

    [MenuItem("Tools/OpenDesk/Parts Material Extractor")]
    private static void Open()
    {
        GetWindow<PartsExtractorWindow>("Parts Extractor").Show();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ScanFbxFiles();
    }

    protected override void OnImGUI()
    {
        // -- Header --
        SirenixEditorGUI.BeginBox("FBX Parts - Extract Textures & Materials");
        EditorGUILayout.LabelField("Path", PARTS_ROOT, EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            ScanFbxFiles();

        var newSelectAll = EditorGUILayout.ToggleLeft("Select All", _selectAll, GUILayout.Width(80));
        if (newSelectAll != _selectAll)
        {
            _selectAll = newSelectAll;
            foreach (var e in _entries) e.selected = _selectAll;
        }
        EditorGUILayout.EndHorizontal();

        SirenixEditorGUI.EndBox();

        if (_entries.Count == 0)
        {
            EditorGUILayout.HelpBox("No FBX files found in " + PARTS_ROOT, MessageType.Warning);
            return;
        }

        // -- File list --
        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        string currentCategory = null;
        foreach (var entry in _entries)
        {
            // Category header
            if (entry.category != currentCategory)
            {
                currentCategory = entry.category;
                EditorGUILayout.Space(2);
                SirenixEditorGUI.BeginBox(currentCategory.ToUpper());
            }

            EditorGUILayout.BeginHorizontal();
            entry.selected = EditorGUILayout.Toggle(entry.selected, GUILayout.Width(18));

            // Icon + name
            var icon = AssetDatabase.GetCachedIcon(entry.assetPath);
            if (icon != null)
                GUILayout.Label(new GUIContent(icon), GUILayout.Width(18), GUILayout.Height(18));
            EditorGUILayout.LabelField(entry.displayName, GUILayout.MinWidth(120));

            // Status badges
            DrawBadge("Tex", entry.hasEmbeddedTextures, entry.texturesExtracted);
            DrawBadge("Mat", true, entry.materialsExtracted);

            EditorGUILayout.EndHorizontal();

            // Close category box if next is different or last
            if (entry == _entries.Last() || _entries[_entries.IndexOf(entry) + 1].category != currentCategory)
                SirenixEditorGUI.EndBox();
        }

        EditorGUILayout.EndScrollView();

        // -- Action buttons --
        EditorGUILayout.Space(8);
        var selectedCount = _entries.Count(e => e.selected);
        EditorGUI.BeginDisabledGroup(selectedCount == 0);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button($"Extract Textures ({selectedCount})", GUILayout.Height(30)))
            ExtractTextures();

        if (GUILayout.Button($"Extract Materials ({selectedCount})", GUILayout.Height(30)))
            ExtractMaterials();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        if (GUILayout.Button($"Extract All ({selectedCount})", GUILayout.Height(30)))
        {
            ExtractTextures();
            ExtractMaterials();
        }

        EditorGUI.EndDisabledGroup();
    }

    private void DrawBadge(string label, bool applicable, bool done)
    {
        if (!applicable)
        {
            GUILayout.Label("--", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(40));
            return;
        }

        var prev = GUI.backgroundColor;
        GUI.backgroundColor = done ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.9f, 0.6f, 0.2f);
        GUILayout.Label(done ? $"{label} [OK]" : $"{label} [--]",
            EditorStyles.miniButton, GUILayout.Width(60));
        GUI.backgroundColor = prev;
    }

    private void ExtractTextures() => RunExtraction("Extracting Textures");
    private void ExtractMaterials() => RunExtraction("Extracting Materials");

    // All three buttons (Textures / Materials / All) route through ExtractAll because
    // partial extraction leaves the model in a broken state: extracted materials need
    // remap + reimport to rewire texture/material bindings on the model prefab.
    private void RunExtraction(string title)
    {
        var selected = _entries.Where(e => e.selected).ToList();
        int texDone = 0, matDone = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            var entry = selected[i];
            EditorUtility.DisplayProgressBar(title,
                entry.displayName, (float)i / selected.Count);

            var (tex, mat) = FbxMaterialExtractor.ExtractAll(entry.assetPath);
            if (tex) texDone++;
            if (mat) matDone++;
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        ScanFbxFiles();
        Debug.Log($"[PartsExtractor] tex={texDone}, mat={matDone} of {selected.Count} files.");
    }

    private void ScanFbxFiles()
    {
        _entries.Clear();

        if (!AssetDatabase.IsValidFolder(PARTS_ROOT)) return;

        var guids = AssetDatabase.FindAssets("t:Model", new[] { PARTS_ROOT });

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

            // Derive category from subfolder
            var relative = path.Substring(PARTS_ROOT.Length + 1); // e.g. "hair/hair.001.fbx"
            var parts = relative.Split('/');
            var category = parts.Length > 1 ? parts[0] : "uncategorized";

            _entries.Add(new FbxEntry
            {
                assetPath = path,
                displayName = Path.GetFileNameWithoutExtension(path),
                category = category,
                selected = _selectAll,
                hasEmbeddedTextures = FbxMaterialExtractor.HasEmbeddedTextures(path),
                texturesExtracted = FbxMaterialExtractor.AreTexturesExtracted(path),
                materialsExtracted = FbxMaterialExtractor.AreMaterialsExtracted(path),
            });
        }

        _entries = _entries.OrderBy(e => e.category).ThenBy(e => e.displayName).ToList();
    }

    private class FbxEntry
    {
        public string assetPath;
        public string displayName;
        public string category;
        public bool selected;
        public bool hasEmbeddedTextures;
        public bool texturesExtracted;
        public bool materialsExtracted;
    }
}
