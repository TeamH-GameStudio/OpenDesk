#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// SD_ManeequinPrefab.prefab 에 컴포넌트를 부착하는 에디터 스크립트.
/// 기존 프리팹(SD_Maneqquin/SD_ManeequinPrefab.prefab)을 직접 편집하여
/// Animator Controller + FaceSwapController + Collider + AgentCharacterController를 설정.
///
/// model_sd 자식 오브젝트의 SkinnedMeshRenderer material[1] = mtl_face.
///
/// 메뉴: Tools > OpenDesk > Setup SD Maneqquin Prefab
/// </summary>
public static class SDManeqqunPrefabBuilder
{
    private const string PrefabPath = "Assets/03.Models/SD_Maneqquin/SD_ManeequinPrefab.prefab";
    private const string ControllerPath = "Assets/05.Prefabs/Agent/AgentAnimatorController.controller";
    private const string MatDir = "Assets/03.Models/SD_Maneqquin/Materials";

    // model_sd의 mtl_face는 material index 1 (확인됨)
    private const int FaceMaterialIndex = 1;

    [MenuItem("Tools/OpenDesk/Setup SD Maneqquin Prefab", false, 126)]
    public static void Build()
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabAsset == null)
        {
            EditorUtility.DisplayDialog("오류",
                $"프리팹을 찾을 수 없습니다.\n{PrefabPath}", "OK");
            return;
        }

        // 프리팹 편집 모드
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(prefabRoot);

        // ── Animator Controller 할당 ────────────────────────────
        var animator = prefabRoot.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = prefabRoot.AddComponent<Animator>();

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            Debug.Log("[SDPrefabBuilder] Animator Controller 할당 완료");
        }
        else
        {
            Debug.LogWarning("[SDPrefabBuilder] AgentAnimatorController 없음 -- 먼저 Build Agent Animator Controller 실행");
        }

        // ── Collider (클릭 감지용) ──────────────────────────────
        if (prefabRoot.GetComponentInChildren<Collider>() == null)
        {
            var col = prefabRoot.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.6f, 0);
            col.radius = 0.25f;
            col.height = 1.2f;
            Debug.Log("[SDPrefabBuilder] CapsuleCollider 추가");
        }

        // ── AgentCharacterController ────────────────────────────
        if (prefabRoot.GetComponent<OpenDesk.Presentation.Character.AgentCharacterController>() == null)
        {
            prefabRoot.AddComponent<OpenDesk.Presentation.Character.AgentCharacterController>();
            Debug.Log("[SDPrefabBuilder] AgentCharacterController 추가");
        }

        // ── FaceSwapController ──────────────────────────────────
        var faceSwap = prefabRoot.GetComponent<OpenDesk.Presentation.Character.FaceSwapController>();
        if (faceSwap == null)
            faceSwap = prefabRoot.AddComponent<OpenDesk.Presentation.Character.FaceSwapController>();

        // model_sd의 SkinnedMeshRenderer 찾기
        var modelSD = prefabRoot.transform.Find("model_sd");
        if (modelSD == null)
        {
            // 재귀 탐색
            foreach (var t in prefabRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "model_sd")
                {
                    modelSD = t;
                    break;
                }
            }
        }

        var so = new SerializedObject(faceSwap);

        if (modelSD != null)
        {
            var smr = modelSD.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                so.FindProperty("_targetRenderer").objectReferenceValue = smr;
                so.FindProperty("_faceMaterialIndex").intValue = FaceMaterialIndex;
                Debug.Log($"[SDPrefabBuilder] model_sd SkinnedMeshRenderer 연결 (material slot {FaceMaterialIndex} = mtl_face)");
            }
            else
            {
                Debug.LogWarning("[SDPrefabBuilder] model_sd에 SkinnedMeshRenderer 없음");
            }
        }
        else
        {
            Debug.LogWarning("[SDPrefabBuilder] model_sd 오브젝트를 찾을 수 없음 -- Inspector에서 수동 할당 필요");
        }

        // 표정 텍스처 할당
        SetTextureField(so, "_faceSmile", $"{MatDir}/facetest_Smile.psd");
        SetTextureField(so, "_faceError", $"{MatDir}/facetest_error.psd");
        SetTextureField(so, "_faceSad", $"{MatDir}/facetest_sad.psd");
        SetTextureField(so, "_faceSleeping", $"{MatDir}/facetest_sleeping.psd");
        // _faceDefault는 런타임에 mtl_face의 현재 BaseMap에서 자동 캡처됨 (FaceSwapController.Awake)

        so.ApplyModifiedPropertiesWithoutUndo();

        // ── 저장 ────────────────────────────────────────────────
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);
        AssetDatabase.Refresh();

        Debug.Log("[SDPrefabBuilder] SD_ManeequinPrefab 설정 완료");
        EditorUtility.DisplayDialog("완료",
            "SD_ManeequinPrefab.prefab 설정 완료!\n\n" +
            "추가된 컴포넌트:\n" +
            "  - Animator Controller (6 State)\n" +
            "  - FaceSwapController (model_sd material[1] = mtl_face)\n" +
            "  - CapsuleCollider\n" +
            "  - AgentCharacterController\n\n" +
            "표정 텍스처 4종 자동 할당 (default는 런타임 자동 캡처)\n\n" +
            $"위치: {PrefabPath}",
            "OK");
    }

    private static void SetTextureField(SerializedObject so, string fieldName, string assetPath)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null) return;

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (tex != null)
        {
            prop.objectReferenceValue = tex;
            Debug.Log($"[SDPrefabBuilder] {fieldName} <- {assetPath}");
        }
        else
        {
            Debug.LogWarning($"[SDPrefabBuilder] 텍스처 없음: {assetPath}");
        }
    }
}
#endif
