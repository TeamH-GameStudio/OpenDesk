#if UNITY_EDITOR
using System.IO;
using OpenDesk.Characters;
using OpenDesk.Characters.Wardrobe.Expressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenDesk.Cinematic.Editor
{
    // Programmatic scene scaffold for CinematicCaptureScene.
    //
    // Run via:  Tools > OpenDesk > Create Cinematic Capture Scene
    //
    // Builds a clean alpha-rendering scene by reusing AgentPreviewRig.prefab —
    // the same self-contained rig the AgentCreation wizard uses. The rig
    // ships with:
    //   - A Camera (originally rendered to a preview RenderTexture)
    //   - An Area Light
    //   - A CharacterMount housing the wardrobe-equipped SD mannequin
    //     (Animator + WardrobeApplier + CharacterPartSwapper)
    //
    // The builder mutates the rig's camera for cinematic capture (full-screen,
    // SolidColor + alpha 0, MainCamera tag, RT cleared) and adds a fill light
    // plus an optional URP post-FX volume. Everything else stays as-authored.
    //
    // The scene is saved to Assets/01.Scenes/CinematicCaptureScene.unity.
    public static class CinematicSceneBuilder
    {
        private const string ScenePath = "Assets/01.Scenes/CinematicCaptureScene.unity";
        private const string AgentPrefabPath = "Assets/05.Prefabs/Agent/AgentPreviewRig.prefab";

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

            var rig = InstantiateRig();
            if (rig == null) return;

            var animator = rig.GetComponentInChildren<Animator>();
            var partSwapper = rig.GetComponentInChildren<CharacterPartSwapper>();
            var bodyRenderer = ResolveBodyRenderer(partSwapper, rig);

            if (animator == null)
                Debug.LogWarning("[CinematicSceneBuilder] Could not find Animator in AgentPreviewRig — pose clips will be ignored at runtime.");
            if (partSwapper == null)
                Debug.LogWarning("[CinematicSceneBuilder] Could not find CharacterPartSwapper in AgentPreviewRig — wire it manually.");
            if (bodyRenderer == null)
                Debug.LogWarning("[CinematicSceneBuilder] Could not infer body Renderer — wire the BodyRenderer field manually so expression textures land on the right submaterials.");

            ReconfigureRigCamera(rig);    // mutates rig's camera + Area Light into static scene lighting
            BuildFillLight();              // static fill light, not controller-driven
            ConfigureAmbient();
            BuildController(animator, partSwapper, bodyRenderer);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Debug.Log($"[CinematicSceneBuilder] Scene saved at {ScenePath}. Open Unity Recorder (Window > General > Recorder > Recorder Window), add an Image Sequence recorder with PNG + Include Alpha, target the rig's Camera, and press START RECORDING.");
        }

        private static GameObject InstantiateRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgentPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "AgentPreviewRig not found",
                    $"Could not load '{AgentPrefabPath}'. Aborting scene build.",
                    "OK");
                return null;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = Vector3.zero;
            rig.transform.rotation = Quaternion.identity;
            return rig;
        }

        // Mutates the camera child of AgentPreviewRig from "preview RT renderer"
        // into a full-screen alpha capture camera, and re-types the rig's Area
        // Light into a Directional one so the character is actually lit at
        // runtime (Area lights only contribute through baking).
        //
        // NOTE: PrefabUtility.UnpackPrefabInstance is called so subsequent edits
        // don't get reverted by the prefab override system.
        private static void ReconfigureRigCamera(GameObject rig)
        {
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            Camera rigCamera = rig.GetComponentInChildren<Camera>(includeInactive: true);
            if (rigCamera != null)
            {
                rigCamera.gameObject.tag = "MainCamera";
                rigCamera.clearFlags = CameraClearFlags.SolidColor;
                rigCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                rigCamera.targetTexture = null;          // render to screen so Recorder picks it up directly
                rigCamera.cullingMask = ~0;              // Everything — the rig camera was restricted to AgentPreview layer only
                rigCamera.allowHDR = false;
                rigCamera.allowMSAA = true;

                var camData = rigCamera.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = rigCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;

                if (rigCamera.GetComponent<AudioListener>() == null)
                    rigCamera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                Debug.LogWarning("[CinematicSceneBuilder] AgentPreviewRig has no Camera child — add one manually before recording.");
            }

            Light rigLight = rig.GetComponentInChildren<Light>(includeInactive: true);
            if (rigLight != null)
            {
                rigLight.type = LightType.Directional;
                rigLight.gameObject.name = "KeyLight";
                rigLight.shadows = LightShadows.Soft;
                rigLight.intensity = 1.2f;
                rigLight.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private static void BuildFillLight()
        {
            var fillGo = new GameObject("FillLight");
            fillGo.transform.position = new Vector3(0, 3, -3);
            fillGo.transform.rotation = Quaternion.Euler(20f, 160f, 0f);
            var fillLight = fillGo.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.78f, 0.85f, 1f);
            fillLight.intensity = 0.35f;
            fillLight.shadows = LightShadows.None;
        }

        private static void ConfigureAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f, 1f);
        }

        private static void BuildController(Animator animator, CharacterPartSwapper partSwapper, Renderer bodyRenderer)
        {
            var ctlGo = new GameObject("_Cinematic");
            var controller = ctlGo.AddComponent<CinematicCaptureController>();

            var so = new SerializedObject(controller);
            so.FindProperty("_partSwapper").objectReferenceValue = partSwapper;
            so.FindProperty("_bodyRenderer").objectReferenceValue = bodyRenderer;
            so.FindProperty("_eyeExpressionSet").objectReferenceValue = FindFirstEyeExpressionSet();
            so.FindProperty("_animator").objectReferenceValue = animator;
            so.FindProperty("_baseController").objectReferenceValue = CreateOrLoadBaseController();
            so.FindProperty("_totalDuration").floatValue = 8f;
            so.FindProperty("_autoExitPlayMode").boolValue = true;
            so.FindProperty("_logProgress").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Creates (or loads) the base AnimatorController the runtime overrides.
        // Layout:
        //   one layer with two empty states "StateA" and "StateB",
        //   each referencing a placeholder AnimationClip sub-asset whose name
        //   the runtime uses to identify which state it should swap. No
        //   transitions — Animator.CrossFadeInFixedTime drives blends.
        //
        // Idempotent: returns the existing asset when one is already present
        // so re-running the menu doesn't churn GUIDs.
        private const string BaseControllerPath = "Assets/01.Scenes/CinematicCaptureScene_Assets/CinematicBaseController.controller";
        private const string PlaceholderNameA = "_placeholderA";
        private const string PlaceholderNameB = "_placeholderB";

        private static RuntimeAnimatorController CreateOrLoadBaseController()
        {
            var dir = Path.GetDirectoryName(BaseControllerPath);
            Directory.CreateDirectory(dir);

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
            if (existing != null) return existing;

            var controller = AnimatorController.CreateAnimatorControllerAtPath(BaseControllerPath);
            var rootSm = controller.layers[0].stateMachine;

            var placeholderA = new AnimationClip { name = PlaceholderNameA, legacy = false };
            var placeholderB = new AnimationClip { name = PlaceholderNameB, legacy = false };
            // Placeholders MUST be sub-assets of the controller — otherwise the
            // override map loses them when the project re-imports.
            AssetDatabase.AddObjectToAsset(placeholderA, controller);
            AssetDatabase.AddObjectToAsset(placeholderB, controller);

            var stateA = rootSm.AddState("StateA");
            stateA.motion = placeholderA;
            stateA.writeDefaultValues = true;

            var stateB = rootSm.AddState("StateB");
            stateB.motion = placeholderB;
            stateB.writeDefaultValues = true;

            rootSm.defaultState = stateA;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(BaseControllerPath);
            return controller;
        }

        // Pulls the body Renderer out of CharacterPartSwapper's _bodyRenderer
        // SerializeField via SerializedObject. Falls back to the largest
        // SkinnedMeshRenderer in the rig hierarchy when the swapper isn't
        // present or its field is empty. The largest renderer heuristic is
        // unreliable for rigs with multiple equal-sized skinned meshes — when
        // wrong, the user just rewires the BodyRenderer field by hand.
        private static Renderer ResolveBodyRenderer(CharacterPartSwapper partSwapper, GameObject rig)
        {
            if (partSwapper != null)
            {
                var so = new SerializedObject(partSwapper);
                var prop = so.FindProperty("_bodyRenderer");
                if (prop != null && prop.objectReferenceValue is Renderer fromSwapper)
                    return fromSwapper;
            }

            if (rig == null) return null;
            Renderer best = null;
            float bestSize = -1f;
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                var size = smr.bounds.size.sqrMagnitude;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = smr;
                }
            }
            return best;
        }

        private static EyeExpressionSetSO FindFirstEyeExpressionSet()
        {
            var guids = AssetDatabase.FindAssets("t:EyeExpressionSetSO");
            if (guids == null || guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<EyeExpressionSetSO>(path);
        }
    }
}
#endif
