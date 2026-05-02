using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal sealed class FbxAutoExtractPostprocessor : AssetPostprocessor
{
    private const string AUTO_EXTRACT_ROOT = "Assets/03.Models/";
    private const string PREF_KEY = "OpenDesk.AutoExtractFbxMaterials";
    private const string MENU_PATH = "Tools/OpenDesk/Auto Extract FBX Materials";

    // Paths currently queued or being processed. Prevents reentrancy when our own
    // ImportAsset(ForceUpdate) calls fire OnPostprocessModel again mid-extraction.
    private static readonly HashSet<string> InFlight = new();

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PREF_KEY, true);
        set => EditorPrefs.SetBool(PREF_KEY, value);
    }

    [MenuItem(MENU_PATH)]
    private static void ToggleEnabled()
    {
        Enabled = !Enabled;
        Debug.Log($"[AutoExtract] Auto FBX material extraction: {(Enabled ? "ENABLED" : "DISABLED")}");
    }

    [MenuItem(MENU_PATH, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MENU_PATH, Enabled);
        return true;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!Enabled) return;

        var path = assetPath;
        if (string.IsNullOrEmpty(path)) return;
        if (!path.StartsWith(AUTO_EXTRACT_ROOT, StringComparison.OrdinalIgnoreCase)) return;
        if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;

        // Skip if this path is already queued/processing OR if both extractions are
        // already complete. Prevents re-queuing delayCalls on every ImportAsset.
        if (InFlight.Contains(path)) return;
        if (FbxMaterialExtractor.AreTexturesExtracted(path)
            && FbxMaterialExtractor.AreMaterialsExtracted(path))
            return;

        InFlight.Add(path);
        // Defer to next editor tick so our ImportAsset calls don't run inside the
        // import callback stack.
        EditorApplication.delayCall += () => SafeExtract(path);
    }

    private static void SafeExtract(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var (tex, mat) = FbxMaterialExtractor.ExtractAll(path);
            if (tex || mat)
                Debug.Log($"[AutoExtract] {Path.GetFileName(path)} - tex={tex}, mat={mat}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AutoExtract] Failed for {path}: {ex.Message}");
        }
        finally
        {
            InFlight.Remove(path);
        }
    }
}
