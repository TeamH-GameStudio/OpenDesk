using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class FbxMaterialExtractor
{
    public static bool HasEmbeddedTextures(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Texture2D>()
            .Any(t => !string.IsNullOrEmpty(t.name) && t.name != "unity_builtin_extra");
    }

    public static bool HasEmbeddedMaterials(string assetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Material>()
            .Any(m => !string.IsNullOrEmpty(m.name));
    }

    // Approximate: cannot match texture names to this FBX's embedded textures because
    // LoadAllAssetsAtPath does not expose them reliably. UI badge only.
    public static bool AreTexturesExtracted(string assetPath)
    {
        var texDir = GetTextureDir(assetPath);
        return AssetDatabase.IsValidFolder(texDir)
            && AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }).Length > 0;
    }

    // Per-material check: every embedded material must have a matching .mat file.
    // Needed because sibling FBXs in the same folder share the Materials/ directory,
    // so "folder has any .mat" is not a valid "already extracted" signal for this FBX.
    public static bool AreMaterialsExtracted(string assetPath)
    {
        var matDir = GetMaterialDir(assetPath);
        if (!AssetDatabase.IsValidFolder(matDir)) return false;

        var materials = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Material>()
            .Where(m => !string.IsNullOrEmpty(m.name))
            .ToList();

        if (materials.Count == 0) return true;

        return materials.All(m =>
            AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/{m.name}.mat") != null);
    }

    // Mirrors Unity Inspector's "Extract Textures..." button exactly:
    //   importer.ExtractTextures -> WriteImportSettingsIfDirty -> ImportAsset(ForceUpdate)
    public static bool ExtractTextures(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer) return false;

        var texDir = GetTextureDir(assetPath);
        EnsureFolder(texDir);

        // Sibling FBXs share texDir, so we do not gate on folder-level state.
        // ExtractTextures itself returns false if this FBX has no new textures to write.
        var success = importer.ExtractTextures(texDir);
        if (!success)
        {
            // Clean up only if we left an empty folder behind (no sibling populated it).
            if (AssetDatabase.IsValidFolder(texDir)
                && AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }).Length == 0)
            {
                AssetDatabase.DeleteAsset(texDir);
            }
            return false;
        }

        AssetDatabase.WriteImportSettingsIfDirty(assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        return true;
    }

    // Mirrors Unity Inspector's "Extract Materials..." button exactly:
    //   AssetDatabase.ExtractAsset per material -> WriteImportSettingsIfDirty -> ImportAsset(ForceUpdate)
    //
    // ExtractAsset both creates the .mat file AND registers a remap entry in the
    // importer's externalObjects map automatically — so no SearchAndRemapMaterials
    // call is needed.
    public static bool ExtractMaterials(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not ModelImporter) return false;

        var matDir = GetMaterialDir(assetPath);
        EnsureFolder(matDir);

        var materials = AssetDatabase.LoadAllAssetsAtPath(assetPath)
            .OfType<Material>()
            .Where(m => !string.IsNullOrEmpty(m.name))
            .ToArray();

        if (materials.Length == 0) return false;

        int extracted = 0;
        foreach (var mat in materials)
        {
            var targetPath = $"{matDir}/{mat.name}.mat";
            // Per-material skip — category folder is shared across sibling FBXs, so
            // folder-level gating would wrongly block siblings with distinct materials.
            if (AssetDatabase.LoadAssetAtPath<Material>(targetPath) != null) continue;

            var error = AssetDatabase.ExtractAsset(mat, targetPath);
            if (string.IsNullOrEmpty(error))
                extracted++;
            else
                Debug.LogWarning($"[FbxMaterialExtractor] ExtractAsset failed for '{mat.name}': {error}");
        }

        if (extracted == 0) return false;

        AssetDatabase.WriteImportSettingsIfDirty(assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        return true;
    }

    public static (bool texturesDone, bool materialsDone) ExtractAll(string assetPath)
    {
        // Textures first: after reimport, embedded materials' texture slots point to
        // the new external texture files. Then ExtractAsset copies those materials
        // with the correct external refs already baked in.
        var tex = ExtractTextures(assetPath);
        var mat = ExtractMaterials(assetPath);
        return (tex, mat);
    }

    public static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        var folderName = Path.GetFileName(folderPath);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName)) return;

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static string GetTextureDir(string assetPath) =>
        Path.GetDirectoryName(assetPath)?.Replace('\\', '/') + "/Textures";

    private static string GetMaterialDir(string assetPath) =>
        Path.GetDirectoryName(assetPath)?.Replace('\\', '/') + "/Materials";
}
