#if UNITY_EDITOR
using System.IO;
using OpenDesk.Characters.Wardrobe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenDesk.Cinematic.Editor
{
    // Programmatic scene scaffold for CinematicCaptureScene.
    //
    // Run via:  Tools > OpenDesk > Create Cinematic Capture Scene
    //
    // Builds a clean alpha-rendering scene with:
    //   - Two directional lights (key + fill)
    //   - Model_Agent3D prefab instance (existing wardrobe rig)
    //   - Capture camera (SolidColor, alpha = 0, MainCamera tag)
    //   - Optional URP Volume preconfigured with a Vignette override
    //   - _Cinematic empty with CinematicCaptureController wired up
    //
    // The scene is saved to Assets/01.Scenes/CinematicCaptureScene.unity.
    public static class CinematicSceneBuilder
    {
        private const string ScenePath = "Assets/01.Scenes/CinematicCaptureScene.unity";
        private const string AgentPrefabPath = "Assets/05.Prefabs/Agent/Model_Agent3D.prefab";

        [MenuItem("Tools/OpenDesk/Create Cinematic Capture Scene")]
        public static void Build()
        {
            if (File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog(
                        "Overwrite Cinematic Capture Scene?",
                        $"'{ScenePath}' already exists. Overwriting will erase its contents.",
                        "Overwrite",
                        "Cancel"))
                {
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting(out var keyLight, out var fillLight);
            var agent = BuildAgent(out var animator, out var wardrobeApplier);
            BuildCamera();
            var postVolume = BuildPostFx();
            BuildController(animator, wardrobeApplier, keyLight, fillLight, postVolume);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Debug.Log($"[CinematicSceneBuilder] Scene saved at {ScenePath}. Open Unity Recorder (Window > General > Recorder > Recorder Window), add an Image Sequence recorder with PNG + Include Alpha, target the CaptureCamera, and press START RECORDING.");
        }

        private static void BuildLighting(out Light keyLight, out Light fillLight)
        {
            var keyGo = new GameObject("KeyLight");
            keyGo.transform.position = new Vector3(0, 3, 0);
            keyGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            keyLight = keyGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.2f;
            keyLight.shadows = LightShadows.Soft;

            var fillGo = new GameObject("FillLight");
            fillGo.transform.position = new Vector3(0, 3, -3);
            fillGo.transform.rotation = Quaternion.Euler(20, 160, 0);
            fillLight = fillGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.78f, 0.85f, 1f);
            fillLight.intensity = 0.35f;
            fillLight.shadows = LightShadows.None;

            // Neutral ambient — controller overrides at runtime per keyframe.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        private static GameObject BuildAgent(out Animator animator, out WardrobeApplier wardrobeApplier)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgentPrefabPath);
            GameObject root;
            if (prefab == null)
            {
                Debug.LogWarning($"[CinematicSceneBuilder] Could not find '{AgentPrefabPath}'. Creating empty Cinematic GameObject — wire the character manually.");
                root = new GameObject("Cinematic");
            }
            else
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                root.name = "Cinematic";
            }
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            animator = root.GetComponentInChildren<Animator>();
            wardrobeApplier = root.GetComponentInChildren<WardrobeApplier>();
            return root;
        }

        private static void BuildCamera()
        {
            var camGo = new GameObject("CaptureCamera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 1.5f, 2.2f);
            camGo.transform.rotation = Quaternion.Euler(5, 180, 0);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.allowHDR = false;
            cam.allowMSAA = true;

            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            camGo.AddComponent<AudioListener>();
        }

        private static Volume BuildPostFx()
        {
            var volumeGo = new GameObject("PostFX");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CinematicVolumeProfile";

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.6f;

            var profileDir = "Assets/01.Scenes/CinematicCaptureScene_Assets";
            Directory.CreateDirectory(profileDir);
            var profilePath = $"{profileDir}/CinematicVolumeProfile.asset";
            AssetDatabase.CreateAsset(profile, profilePath);
            AssetDatabase.SaveAssets();

            volume.sharedProfile = profile;
            return volume;
        }

        private static void BuildController(Animator animator, WardrobeApplier wardrobeApplier,
                                            Light keyLight, Light fillLight, Volume postVolume)
        {
            var ctlGo = new GameObject("_Cinematic");
            var controller = ctlGo.AddComponent<CinematicCaptureController>();

            var so = new SerializedObject(controller);
            so.FindProperty("_catalog").objectReferenceValue = FindWardrobeCatalog();
            so.FindProperty("_wardrobeApplier").objectReferenceValue = wardrobeApplier;
            so.FindProperty("_animator").objectReferenceValue = animator;
            so.FindProperty("_keyLight").objectReferenceValue = keyLight;
            so.FindProperty("_fillLight").objectReferenceValue = fillLight;
            so.FindProperty("_postVolume").objectReferenceValue = postVolume;
            so.FindProperty("_useDefaultOutfit").boolValue = true;
            so.FindProperty("_totalDuration").floatValue = 8f;
            so.FindProperty("_autoExitPlayMode").boolValue = true;
            so.FindProperty("_logProgress").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static WardrobeCatalogSO FindWardrobeCatalog()
        {
            var guids = AssetDatabase.FindAssets("t:WardrobeCatalogSO");
            if (guids == null || guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<WardrobeCatalogSO>(path);
        }
    }
}
#endif
