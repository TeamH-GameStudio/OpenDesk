using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Handles model import and scene placement
    /// </summary>
    public class ModelImporter
    {
        public event Action<float> OnProgressUpdate;

        private const string IMPORT_FOLDER = "Assets/TripoModels";
        private const int MAX_CLEANUP_RETRIES = 5;

        /// <summary>
        /// Import model from file data
        /// </summary>
        public bool ImportModel(string fileId, string fileName, string fileType, byte[] fileData)
        {
            try
            {
                LogHelper.Log("Starting import process...");
                OnProgressUpdate?.Invoke(0f);

                // Determine if ZIP or direct model file
                bool isZip = fileType.ToLower() == "zip" || fileName.ToLower().EndsWith(".zip");
                
                string tempPath = Path.Combine(UnityEngine.Application.temporaryCachePath, fileId);
                Directory.CreateDirectory(tempPath);

                string modelPath;
                
                if (isZip)
                {
                    // Handle ZIP file
                    modelPath = ProcessZipFile(fileId, fileName, fileData, tempPath);
                }
                else
                {
                    // Handle direct FBX/OBJ file
                    modelPath = ProcessDirectFile(fileId, fileName, fileType, fileData, tempPath);
                }

                if (string.IsNullOrEmpty(modelPath))
                {
                    LogHelper.Log("Error: Could not locate model file");
                    CleanupTempDirectory(tempPath);
                    return false;
                }

                OnProgressUpdate?.Invoke(0.5f);

                // Determine unique model name (used for folder, fbx file, and scene instance)
                string modelName = GetCleanModelName(fileName);
                string uniqueModelName = GetUniqueModelName(modelName);

                // Rename the model file in temp to the unique model name
                string modelExt = Path.GetExtension(modelPath);
                string modelDir = Path.GetDirectoryName(modelPath);
                string originalBaseName = Path.GetFileNameWithoutExtension(modelPath);
                string renamedModelPath = Path.Combine(modelDir, uniqueModelName + modelExt);
                if (!string.Equals(modelPath, renamedModelPath, StringComparison.OrdinalIgnoreCase))
                    File.Move(modelPath, renamedModelPath);
                modelPath = renamedModelPath;

                // Also rename the associated .fbm texture folder if it exists
                string fbmFolderOld = Path.Combine(modelDir, originalBaseName + ".fbm");
                string fbmFolderNew = Path.Combine(modelDir, uniqueModelName + ".fbm");
                if (Directory.Exists(fbmFolderOld) && !string.Equals(fbmFolderOld, fbmFolderNew, StringComparison.OrdinalIgnoreCase))
                    Directory.Move(fbmFolderOld, fbmFolderNew);

                // Move to Assets folder
                string assetPath = MoveToAssetsFolder(uniqueModelName, tempPath);
                if (string.IsNullOrEmpty(assetPath))
                {
                    LogHelper.Log("Error: Failed to move files to Assets folder");
                    CleanupTempDirectory(tempPath);
                    return false;
                }

                OnProgressUpdate?.Invoke(0.7f);

                // Import asset
                bool imported = ImportAsset(assetPath, Path.GetFileName(modelPath));
                if (!imported)
                {
                    LogHelper.Log("Error: Asset import failed");
                    return false;
                }

                OnProgressUpdate?.Invoke(0.9f);

                // Add to scene
                bool addedToScene = AddToScene(assetPath, Path.GetFileName(modelPath), uniqueModelName);
                
                OnProgressUpdate?.Invoke(1f);

                // Cleanup temp directory
                CleanupTempDirectory(tempPath);

                return addedToScene;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Import error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Process ZIP file - extract and find model
        /// </summary>
        private string ProcessZipFile(string fileId, string fileName, byte[] fileData, string tempPath)
        {
            try
            {
                LogHelper.Log("Extracting ZIP file...");
                
                // Save ZIP to temp
                string zipPath = Path.Combine(tempPath, "archive.zip");
                File.WriteAllBytes(zipPath, fileData);

                // Extract ZIP
                string extractPath = Path.Combine(tempPath, "extracted");
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                File.Delete(zipPath);

                // Find FBX or OBJ file
                string modelFile = FindModelFile(extractPath);
                
                if (string.IsNullOrEmpty(modelFile))
                {
                    LogHelper.Log("Error: No FBX or OBJ file found in ZIP");
                    return null;
                }

                LogHelper.Log($"Found model: {Path.GetFileName(modelFile)}");
                
                // Move extracted files to root temp path
                MoveDirectory(extractPath, tempPath);
                Directory.Delete(extractPath, true);

                return Path.Combine(tempPath, Path.GetFileName(modelFile));
            }
            catch (Exception ex)
            {
                LogHelper.Error($"ZIP extraction error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process direct model file (FBX/OBJ)
        /// </summary>
        private string ProcessDirectFile(string fileId, string fileName, string fileType, byte[] fileData, string tempPath)
        {
            try
            {
                LogHelper.Log($"Processing {fileType.ToUpper()} file...");
                
                string extension = fileType.StartsWith(".") ? fileType : $".{fileType}";
                string modelPath = Path.Combine(tempPath, $"{fileName}{extension}");
                
                File.WriteAllBytes(modelPath, fileData);
                
                return modelPath;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"File save error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find FBX or OBJ file in directory (recursively)
        /// </summary>
        private string FindModelFile(string directory)
        {
            // Search for FBX first
            var fbxFiles = Directory.GetFiles(directory, "*.fbx", SearchOption.AllDirectories);
            if (fbxFiles.Length > 0)
                return fbxFiles[0];

            // Then search for OBJ
            var objFiles = Directory.GetFiles(directory, "*.obj", SearchOption.AllDirectories);
            if (objFiles.Length > 0)
                return objFiles[0];

            return null;
        }

        /// <summary>
        /// Move directory contents
        /// </summary>
        private void MoveDirectory(string source, string target)
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(source.Length + 1);
                string targetPath = Path.Combine(target, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.Move(file, targetPath);
            }
        }

        /// <summary>
        /// Strip known model/archive extensions to get a clean display name
        /// </summary>
        private static string GetCleanModelName(string fileName)
        {
            string name = fileName ?? "model";
            // Strip known extensions
            foreach (var ext in new[] { ".zip", ".fbx", ".obj", ".glb", ".gltf" })
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - ext.Length);
                    break;
                }
            }
            return string.IsNullOrWhiteSpace(name) ? "model" : name.Replace(' ', '_');
        }

        /// <summary>
        /// Return a unique model name by appending a number if the Assets folder already exists
        /// </summary>
        private static string GetUniqueModelName(string baseName)
        {
            string candidate = baseName;
            string folderPath = Path.Combine(IMPORT_FOLDER, candidate);
            if (!Directory.Exists(folderPath))
                return candidate;

            int index = 1;
            while (true)
            {
                candidate = $"{baseName}_{index}";
                folderPath = Path.Combine(IMPORT_FOLDER, candidate);
                if (!Directory.Exists(folderPath))
                    return candidate;
                index++;
            }
        }

        private string MoveToAssetsFolder(string modelName, string tempPath)
        {
            try
            {
                LogHelper.Log("Moving to Assets folder...");
                
                // Create import folder if needed
                if (!Directory.Exists(IMPORT_FOLDER))
                {
                    Directory.CreateDirectory(IMPORT_FOLDER);
                }

                // Create subfolder named after the model
                string assetFolder = Path.Combine(IMPORT_FOLDER, modelName);
                Directory.CreateDirectory(assetFolder);

                // Copy all files
                foreach (var file in Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(tempPath.Length + 1);
                    string targetPath = Path.Combine(assetFolder, relativePath);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    File.Copy(file, targetPath, true);
                }

                AssetDatabase.Refresh();
                
                return assetFolder;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Move error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Import asset with Unity's AssetDatabase
        /// </summary>
        private bool ImportAsset(string assetPath, string modelFileName)
        {
            try
            {
                LogHelper.Log("Importing asset...");
                
                string modelAssetPath = Path.Combine(assetPath, modelFileName).Replace("\\", "/");
                
                // Fix normal maps BEFORE importing materials (to avoid warnings)
                FixNormalMaps(assetPath);
                
                // Combine Metallic and Roughness textures into Unity format
                CombineMetallicRoughnessTextures(assetPath);
                
                AssetDatabase.StartAssetEditing();
                try
                {
                    AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceUpdate);
                    
                    // Configure model importer
                    var importer = AssetImporter.GetAtPath(modelAssetPath) as UnityEditor.ModelImporter;
                    if (importer != null)
                    {
                        importer.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.ImportStandard;
                        importer.materialLocation = UnityEditor.ModelImporterMaterialLocation.External;
                        importer.materialSearch = UnityEditor.ModelImporterMaterialSearch.RecursiveUp;
                        importer.SaveAndReimport();
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                
                AssetDatabase.Refresh();
                
                // Apply textures to materials AFTER import
                ApplyTexturesToMaterials(assetPath);
                
                LogHelper.Log("Asset imported successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Import error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Combine Metallic and Roughness textures into Unity's Metallic/Smoothness format
        /// Unity uses: R channel = Metallic, A channel = Smoothness (1 - Roughness)
        /// Supports multi-part models with separate textures per part
        /// </summary>
        private void CombineMetallicRoughnessTextures(string assetPath)
        {
            try
            {
                LogHelper.Log("Checking for Metallic/Roughness textures to combine...");
                
                // Find all textures in the import folder
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                
                // Group textures by part name
                var metallicTextures = new System.Collections.Generic.Dictionary<string, string>();
                var roughnessTextures = new System.Collections.Generic.Dictionary<string, string>();
                
                // Find and categorize metallic and roughness textures
                foreach (string guid in textureGuids)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileName(texturePath).ToLower();
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(texturePath).ToLower();
                    
                    // Check for metallic texture
                    if (fileName.Contains("metallic"))
                    {
                        string partKey = ExtractPartKey(fileNameWithoutExt, new[] { "metallic" });
                        metallicTextures[partKey] = texturePath;
                    }
                    
                    // Check for roughness texture
                    if (fileName.Contains("roughness"))
                    {
                        string partKey = ExtractPartKey(fileNameWithoutExt, new[] { "roughness" });
                        roughnessTextures[partKey] = texturePath;
                    }
                }
                
                // Combine matching pairs
                int combinedCount = 0;
                foreach (var metallicEntry in metallicTextures)
                {
                    string partKey = metallicEntry.Key;
                    string metallicPath = metallicEntry.Value;
                    
                    if (roughnessTextures.ContainsKey(partKey))
                    {
                        string roughnessPath = roughnessTextures[partKey];
                        
                        LogHelper.Log($"Combining textures for part: {partKey}");
                        
                        // Load textures as readable
                        Texture2D metallicTex = LoadTextureReadable(metallicPath);
                        Texture2D roughnessTex = LoadTextureReadable(roughnessPath);
                        
                        if (metallicTex != null && roughnessTex != null)
                        {
                            // Create combined texture
                            int width = Mathf.Max(metallicTex.width, roughnessTex.width);
                            int height = Mathf.Max(metallicTex.height, roughnessTex.height);
                            Texture2D combined = new Texture2D(width, height, TextureFormat.RGBA32, true);
                            
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    // Sample textures (with UV scaling if sizes differ)
                                    float u = (float)x / width;
                                    float v = (float)y / height;
                                    
                                    Color metallic = metallicTex.GetPixelBilinear(u, v);
                                    Color roughness = roughnessTex.GetPixelBilinear(u, v);
                                    
                                    // Unity format: R = Metallic, G & B = 0, A = Smoothness (1 - Roughness)
                                    float metallicValue = metallic.r;  // Usually grayscale
                                    float smoothness = 1f - roughness.r;  // Convert roughness to smoothness
                                    
                                    combined.SetPixel(x, y, new Color(metallicValue, metallicValue, metallicValue, smoothness));
                                }
                            }
                            
                            combined.Apply();
                            
                            // Save combined texture with original part name
                            string baseFileName = Path.GetFileNameWithoutExtension(metallicPath);
                            string combinedPath = Path.Combine(Path.GetDirectoryName(metallicPath), 
                                baseFileName + "_Smoothness.png");
                            byte[] pngData = combined.EncodeToPNG();
                            File.WriteAllBytes(combinedPath, pngData);
                            
                            LogHelper.Log($"  Created: {Path.GetFileName(combinedPath)}");
                            combinedCount++;
                            
                            // Clean up
                            UnityEngine.Object.DestroyImmediate(combined);
                        }
                    }
                }
                
                if (combinedCount > 0)
                {
                    AssetDatabase.Refresh();
                    LogHelper.Log($"Successfully combined {combinedCount} Metallic/Smoothness texture pair(s)");
                }
                else
                {
                    LogHelper.Log("No matching Metallic/Roughness texture pairs found");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Metallic/Roughness combination error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract part key from texture filename by removing texture type suffix
        /// </summary>
        private string ExtractPartKey(string fileName, string[] suffixes)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            string key = fileName;
            foreach (string suffix in suffixes)
            {
                if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    key = key.Substring(0, key.Length - suffix.Length);
                    break;
                }
            }
            return key;
        }

        /// <summary>
        /// Normalize part key by removing known suffixes and fixing "part_new" naming.
        /// </summary>
        private string NormalizePartKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            string normalized = key.ToLower();

            // Remove base color naming
            normalized = ExtractPartKey(normalized, new[] { "_basecolor", "basecolor", "_albedo", "albedo", "_diffuse", "diffuse" });

            // Remove metallic/smoothness/roughness naming
            normalized = ExtractPartKey(normalized, new[] { "_smoothness", "smoothness", "_metallic", "metallic", "_roughness", "roughness" });

            // If normalization resulted in empty string, fall back to original key
            if (string.IsNullOrEmpty(normalized))
                return key.ToLower();

            return normalized;
        }
        
        /// <summary>
        /// Find texture that best matches the material name
        /// </summary>
        private string FindMatchingTexture(string materialName, System.Collections.Generic.Dictionary<string, string> textures)
        {
            // Only exact match
            if (textures.ContainsKey(materialName))
            {
                return textures[materialName];
            }
            
            // If only one texture exists, use it (single-material models)
            if (textures.Count == 1)
            {
                using (var enumerator = textures.Values.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        return enumerator.Current;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Load a texture as readable (required for GetPixel/SetPixel operations)
        /// </summary>
        private Texture2D LoadTextureReadable(string texturePath)
        {
            try
            {
                // Make texture readable
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer != null)
                {
                    bool wasReadable = importer.isReadable;
                    
                    if (!wasReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                    
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    return texture;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error loading texture {Path.GetFileName(texturePath)}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Apply textures to materials after import
        /// Supports multi-part models with separate textures per material
        /// </summary>
        private void ApplyTexturesToMaterials(string assetPath)
        {
            try
            {
                LogHelper.Log("Applying textures to materials...");

                // Detect render pipeline and select correct shader / property names
                var pipelineType = CurrentPipelineType;
                LogHelper.Log($"Using render pipeline branch: {pipelineType}");

                string shaderName, albedoProp, colorProp, metallicProp, normalProp, glossProp, metallicKeyword;
                switch (pipelineType)
                {
                    case RenderPipelineType.URP:
                        shaderName      = "Universal Render Pipeline/Lit";
                        albedoProp      = "_BaseMap";
                        colorProp       = "_BaseColor";
                        metallicProp    = "_MetallicGlossMap";
                        normalProp      = "_BumpMap";
                        glossProp       = "_Smoothness";
                        metallicKeyword = "_METALLICSPECGLOSSMAP";
                        break;
                    case RenderPipelineType.HDRP:
                        shaderName      = "HDRP/Lit";
                        albedoProp      = "_BaseColorMap";
                        colorProp       = "_BaseColor";
                        metallicProp    = "_MaskMap";
                        normalProp      = "_NormalMap";
                        glossProp       = null;
                        metallicKeyword = null;
                        break;
                    default: // Standard
                        shaderName      = "Standard";
                        albedoProp      = "_MainTex";
                        colorProp       = "_Color";
                        metallicProp    = "_MetallicGlossMap";
                        normalProp      = "_BumpMap";
                        glossProp       = "_Glossiness";
                        metallicKeyword = "_METALLICGLOSSMAP";
                        break;
                }
                
                // Find all materials in the import folder
                string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { assetPath });
                
                if (materialGuids.Length == 0)
                {
                    LogHelper.Log("No materials found to configure");
                    return;
                }
                
                // Find and categorize all textures by part
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                
                var baseColorTextures = new System.Collections.Generic.Dictionary<string, string>();
                var metallicSmoothnessTextures = new System.Collections.Generic.Dictionary<string, string>();
                var normalTextures = new System.Collections.Generic.Dictionary<string, string>();
                var seenMaterialKeys = new System.Collections.Generic.Dictionary<string, string>();
                
                foreach (string guid in textureGuids)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileName(texturePath).ToLower();
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(texturePath).ToLower();
                    
                    // Base Color / Albedo
                    if (fileName.Contains("basecolor") || 
                        fileName.Contains("albedo") || 
                        fileName.Contains("diffuse"))
                    {
                        string partKey = ExtractPartKey(fileNameWithoutExt, new[] { "basecolor", "albedo", "diffuse" });
                        partKey = NormalizePartKey(partKey);
                        baseColorTextures[partKey] = texturePath;
                    }
                    
                    // Metallic Smoothness (combined texture we created)
                    if (fileName.Contains("_smoothness"))
                    {
                        string partKey = ExtractPartKey(fileNameWithoutExt, new[] { "_smoothness" });
                        partKey = NormalizePartKey(partKey);
                        metallicSmoothnessTextures[partKey] = texturePath;
                    }
                    
                    // Normal map
                    if (fileName.Contains("normal"))
                    {
                        string partKey = ExtractPartKey(fileNameWithoutExt, new[] { "normal" });
                        partKey = NormalizePartKey(partKey);
                        normalTextures[partKey] = texturePath;
                    }
                }
                
                // Apply textures to each material
                int configuredCount = 0;
                foreach (string guid in materialGuids)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    
                    if (material != null)
                    {
                        // Extract material name to match with texture parts
                        string rawMaterialName = Path.GetFileNameWithoutExtension(materialPath).ToLower();
                        string materialName = NormalizePartKey(rawMaterialName);

                        if (seenMaterialKeys.TryGetValue(materialName, out string existingPath))
                        {
                            LogHelper.Warning($"Duplicate material detected. Keeping existing: {existingPath}");
                            continue;
                        }
                        else
                        {
                            seenMaterialKeys[materialName] = materialPath;
                        }

                        // TODO: 暂时禁用材质重命名，避免跨Unity版本重导入时生成同名重复.mat文件
                        // 原因：插件在Unity6用RenameAsset将xxx_basecolor.mat改名为xxx.mat，
                        // 但FBX内嵌材质名仍为xxx_basecolor，切换Unity2022重导入时找不到匹配材质，
                        // 导致Unity重新生成xxx_basecolor.mat，形成重复文件。
                        // if (materialName != rawMaterialName)
                        // {
                        //     // Avoid renaming to empty/short names that can cause collisions
                        //     if (!string.IsNullOrEmpty(materialName) && materialName.Length >= 3)
                        //     {
                        //         string renameError = AssetDatabase.RenameAsset(materialPath, materialName);
                        //         if (!string.IsNullOrEmpty(renameError))
                        //         {
                        //             LogHelper.Warning($"Rename material failed ({materialPath}): {renameError}");
                        //         }
                        //         else
                        //         {
                        //             materialPath = AssetDatabase.GUIDToAssetPath(guid);
                        //             material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        //             if (material == null)
                        //             {
                        //                 continue;
                        //             }
                        //         }
                        //     }
                        // }
                        
                        // Set to the correct shader for the active render pipeline
                        if (material.shader.name != shaderName)
                        {
                            Shader targetShader = Shader.Find(shaderName);
                            if (targetShader != null)
                                material.shader = targetShader;
                            else
                                LogHelper.Warning($"Shader not found: {shaderName}. Material may render incorrectly.");
                        }

                        // Set albedo color to white (FFFFFF) to display textures correctly
                        material.SetColor(colorProp, Color.white);
                        
                        // Find matching textures for this material (only search types that exist)
                        string baseColorPath = baseColorTextures.Count > 0 ? FindMatchingTexture(materialName, baseColorTextures) : null;
                        string metallicSmoothnessPath = metallicSmoothnessTextures.Count > 0 ? FindMatchingTexture(materialName, metallicSmoothnessTextures) : null;
                        string normalPath = normalTextures.Count > 0 ? FindMatchingTexture(materialName, normalTextures) : null;
                        
                        // Skip material if no textures matched at all
                        if (string.IsNullOrEmpty(baseColorPath) && string.IsNullOrEmpty(metallicSmoothnessPath) && string.IsNullOrEmpty(normalPath))
                        {
                            LogHelper.Log($"Skipping material (no matching textures): {material.name}");
                            continue;
                        }
                        
                        LogHelper.Log($"Configuring material: {material.name}");
                        
                        // Apply Base Color
                        if (!string.IsNullOrEmpty(baseColorPath))
                        {
                            Texture2D baseColorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath);
                            if (baseColorTex != null)
                            {
                                material.SetTexture(albedoProp, baseColorTex);
                                LogHelper.Log($"  - Applied Base Color: {Path.GetFileName(baseColorPath)}");
                            }
                        }

                        // Apply Metallic/Smoothness
                        if (!string.IsNullOrEmpty(metallicSmoothnessPath))
                        {
                            Texture2D metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicSmoothnessPath);
                            if (metallicTex != null)
                            {
                                material.SetTexture(metallicProp, metallicTex);
                                material.SetFloat("_Metallic", 1.0f);
                                if (!string.IsNullOrEmpty(glossProp))
                                    material.SetFloat(glossProp, 1.0f);
                                if (!string.IsNullOrEmpty(metallicKeyword))
                                    material.EnableKeyword(metallicKeyword);
                                LogHelper.Log($"  - Applied Metallic/Smoothness: {Path.GetFileName(metallicSmoothnessPath)}");
                            }
                        }

                        // Apply Normal Map
                        if (!string.IsNullOrEmpty(normalPath))
                        {
                            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                            if (normalTex != null)
                            {
                                material.SetTexture(normalProp, normalTex);
                                material.SetFloat("_BumpScale", 1.0f);
                                material.EnableKeyword("_NORMALMAP");
                                LogHelper.Log($"  - Applied Normal Map: {Path.GetFileName(normalPath)}");
                            }
                        }
                        
                        EditorUtility.SetDirty(material);
                        configuredCount++;
                    }
                }
                
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                if (configuredCount > 0)
                {
                    LogHelper.Log($"Configured {configuredCount} material(s) with PBR textures");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Material configuration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Automatically fix normal map settings for imported textures
        /// </summary>
        private void FixNormalMaps(string assetPath)
        {
            try
            {
                LogHelper.Log("Checking for normal maps...");
                
                int fixedCount = 0;
                
                // Find all textures in the import folder
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                
                foreach (string guid in textureGuids)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileName(texturePath).ToLower();
                    
                    // Check if filename contains "normal"
                    if (fileName.Contains("normal"))
                    {
                        TextureImporter texImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                        
                        if (texImporter != null && texImporter.textureType != TextureImporterType.NormalMap)
                        {
                            texImporter.textureType = TextureImporterType.NormalMap;
                            texImporter.SaveAndReimport();
                            fixedCount++;
                            LogHelper.Log($"Fixed normal map: {Path.GetFileName(texturePath)}");
                        }
                    }
                }
                
                if (fixedCount > 0)
                {
                    LogHelper.Log($"Fixed {fixedCount} normal map(s)");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Normal map fix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Add imported model to current scene
        /// </summary>
        private bool AddToScene(string assetPath, string modelFileName, string displayName)
        {
            try
            {
                LogHelper.Log("Adding to scene...");
                
                string modelAssetPath = Path.Combine(assetPath, modelFileName).Replace("\\", "/");
                
                // Load model
                GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
                if (modelPrefab == null)
                {
                    LogHelper.Log("Error: Could not load model prefab");
                    return false;
                }

                // Get active scene
                var scene = EditorSceneManager.GetActiveScene();
                
                // Instantiate in scene
                GameObject instance = PrefabUtility.InstantiatePrefab(modelPrefab, scene) as GameObject;
                if (instance == null)
                {
                    LogHelper.Log("Error: Could not instantiate prefab");
                    return false;
                }

                // Set position and name
                instance.transform.position = Vector3.zero;
                instance.name = displayName;
                
                // Select object
                Selection.activeGameObject = instance;
                
                // Mark scene dirty
                EditorSceneManager.MarkSceneDirty(scene);
                
                LogHelper.Log($"Model added to scene: {displayName}");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Scene add error: {ex.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Render pipeline detection
        // ---------------------------------------------------------------

        public enum RenderPipelineType { Standard, URP, HDRP }

        public static RenderPipelineType CurrentPipelineType { get; set; } = RenderPipelineType.Standard;

        public static void DetectAndSetRenderPipeline()
        {
            var rpa = GraphicsSettings.defaultRenderPipeline;
            if (rpa == null) 
            {
                CurrentPipelineType = RenderPipelineType.Standard;
                return;
            }
            string typeName = rpa.GetType().Name;
            if (typeName.Contains("Universal")) 
                CurrentPipelineType = RenderPipelineType.URP;
            else if (typeName.Contains("HD")) 
                CurrentPipelineType = RenderPipelineType.HDRP;
            else 
                CurrentPipelineType = RenderPipelineType.Standard;
        }

        /// <summary>
        /// Cleanup temporary directory with retry
        /// </summary>
        private void CleanupTempDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            for (int i = 0; i < MAX_CLEANUP_RETRIES; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    LogHelper.Log("Temporary files cleaned up");
                    return;
                }
                catch (IOException)
                {
                    if (i < MAX_CLEANUP_RETRIES - 1)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"Cleanup warning: {ex.Message}");
                    return;
                }
            }
            
            LogHelper.Warning($"Warning: Could not delete temporary directory: {path}");
        }
    }
}
