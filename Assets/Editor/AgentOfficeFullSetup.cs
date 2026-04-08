#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Office 씬 원클릭 전체 셋업.
/// 1. Animator Controller 생성 (6 State)
/// 2. SD_ManeequinPrefab 컴포넌트 설정 (FaceSwap + Animator + Collider)
/// 3. 씬에 AgentFocusCameraController + CinemachineBrain 배치
/// 4. AgentOfficeBootstrapper/AgentSpawner에 SD_Maneqquin 프리팹 자동 연결
///
/// 메뉴: Tools > OpenDesk > Full Office Setup (원클릭)
/// </summary>
public static class AgentOfficeFullSetup
{
    private const string SDPrefabPath = "Assets/03.Models/SD_Maneqquin/SD_ManeequinPrefab.prefab";

    [MenuItem("Tools/OpenDesk/Full Office Setup (원클릭)", false, 100)]
    public static void Run()
    {
        // 현재 씬 저장 확인
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var log = new System.Text.StringBuilder();
        log.AppendLine("=== OpenDesk Full Office Setup ===\n");

        // ── Step 1: Animator Controller ─────────────────────────
        log.AppendLine("[1/4] Animator Controller 생성...");
        AgentAnimatorBuilder.Build();
        log.AppendLine("  -> 완료\n");

        // ── Step 2: SD_Maneqquin 프리팹 설정 ────────────────────
        log.AppendLine("[2/4] SD_ManeequinPrefab 컴포넌트 설정...");
        SDManeqqunPrefabBuilder.Build();
        log.AppendLine("  -> 완료\n");

        // ── Step 3: 씬 오브젝트 배치 ────────────────────────────
        log.AppendLine("[3/4] 씬 오브젝트 배치...");
        SetupSceneObjects(log);
        log.AppendLine("");

        // ── Step 4: 프리팹 참조 연결 ────────────────────────────
        log.AppendLine("[4/4] 프리팹 참조 연결...");
        LinkPrefabReferences(log);
        log.AppendLine("");

        // 씬 저장
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        var result = log.ToString();
        Debug.Log(result);
        EditorUtility.DisplayDialog("Full Office Setup 완료", result, "OK");
    }

    private static void SetupSceneObjects(System.Text.StringBuilder log)
    {
        // ── Main Camera Perspective 설정 ────────────────────────
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.orthographic = false;
            mainCam.fieldOfView = 60f;
            log.AppendLine("  -> Main Camera: Perspective 모드 (FOV=60)");
        }

        // ── AgentFocusCameraController ──────────────────────────
        var focusCtrl = Object.FindFirstObjectByType<OpenDesk.Presentation.Camera.AgentFocusCameraController>();
        if (focusCtrl == null)
        {
            var go = new GameObject("AgentFocusCameraController");
            focusCtrl = go.AddComponent<OpenDesk.Presentation.Camera.AgentFocusCameraController>();
            log.AppendLine("  -> AgentFocusCameraController 오브젝트 생성");
        }
        else
        {
            log.AppendLine("  -> AgentFocusCameraController 이미 존재");
        }

        // ── WallFadeController (벽 투명화) ─────────────────────
        var wallFade = Object.FindFirstObjectByType<OpenDesk.Presentation.Camera.WallFadeController>();
        if (wallFade == null)
        {
            // Main Camera에 부착
            if (mainCam != null)
            {
                wallFade = mainCam.gameObject.AddComponent<OpenDesk.Presentation.Camera.WallFadeController>();
                log.AppendLine("  -> WallFadeController를 Main Camera에 추가");
            }
            else
            {
                var go = new GameObject("WallFadeController");
                wallFade = go.AddComponent<OpenDesk.Presentation.Camera.WallFadeController>();
                log.AppendLine("  -> WallFadeController 오브젝트 생성");
            }
        }
        else
        {
            log.AppendLine("  -> WallFadeController 이미 존재");
        }

        // ── AgentClickHandler에 FocusCamera 참조 연결 ───────────
        var clickHandler = Object.FindFirstObjectByType<OpenDesk.Presentation.Character.AgentClickHandler>();
        if (clickHandler != null && focusCtrl != null)
        {
            var so = new SerializedObject(clickHandler);
            var prop = so.FindProperty("_focusCamera");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = focusCtrl;
                so.ApplyModifiedProperties();
                log.AppendLine("  -> AgentClickHandler._focusCamera 연결 완료");
            }
            else
            {
                log.AppendLine("  -> AgentClickHandler._focusCamera 이미 연결됨");
            }
        }
        else
        {
            log.AppendLine("  [!] AgentClickHandler를 씬에서 찾을 수 없음");
        }

        // ── ChatPanelController 패치 (dismiss 버튼 + sessionList) ──
        PatchChatPanel(log);
    }

    private static void PatchChatPanel(System.Text.StringBuilder log)
    {
        var chatCtrl = Object.FindFirstObjectByType<OpenDesk.Presentation.UI.Session.ChatPanelController>(FindObjectsInactive.Include);
        if (chatCtrl == null)
        {
            log.AppendLine("  [!] ChatPanelController를 씬에서 찾을 수 없음");
            return;
        }

        var cpSO = new SerializedObject(chatCtrl);

        // _dismissButton이 비어있으면 생성
        var dismissProp = cpSO.FindProperty("_dismissButton");
        if (dismissProp != null && dismissProp.objectReferenceValue == null)
        {
            // 입력 영역 찾기 (SendBtn의 부모)
            var sendProp = cpSO.FindProperty("_sendButton");
            Transform inputArea = null;
            if (sendProp?.objectReferenceValue is UnityEngine.UI.Button sendBtn)
                inputArea = sendBtn.transform.parent;

            if (inputArea == null)
                inputArea = chatCtrl.transform;

            // "작업 완료" 버튼 생성
            var dismissGo = new GameObject("DismissBtn");
            dismissGo.transform.SetParent(inputArea, false);

            var dismissImg = dismissGo.AddComponent<UnityEngine.UI.Image>();
            dismissImg.color = new Color32(60, 180, 100, 255);

            var dismissBtn = dismissGo.AddComponent<UnityEngine.UI.Button>();
            dismissBtn.targetGraphic = dismissImg;

            var dismissRT = dismissGo.GetComponent<RectTransform>();
            dismissRT.anchorMin = new Vector2(1, 0);
            dismissRT.anchorMax = new Vector2(1, 1);
            dismissRT.pivot = new Vector2(1, 0.5f);
            dismissRT.offsetMin = new Vector2(-140, 8);
            dismissRT.offsetMax = new Vector2(-64, -8);

            // 텍스트
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(dismissGo.transform, false);
            var txt = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
            txt.text = "작업 완료";
            txt.fontSize = 14;
            txt.color = Color.white;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            var txtRT = txtGo.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            dismissGo.SetActive(false);

            dismissProp.objectReferenceValue = dismissBtn;
            log.AppendLine("  -> ChatPanel: DismissBtn 생성 + 연결");
        }
        else
        {
            log.AppendLine("  -> ChatPanel: DismissBtn 이미 존재");
        }

        // _sessionList 연결
        var slProp = cpSO.FindProperty("_sessionList");
        if (slProp != null && slProp.objectReferenceValue == null)
        {
            var sl = Object.FindFirstObjectByType<OpenDesk.Presentation.UI.Session.SessionListController>(FindObjectsInactive.Include);
            if (sl != null)
            {
                slProp.objectReferenceValue = sl;
                log.AppendLine("  -> ChatPanel: _sessionList 연결 완료");
            }
        }

        cpSO.ApplyModifiedProperties();
    }

    private static void LinkPrefabReferences(System.Text.StringBuilder log)
    {
        var sdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SDPrefabPath);
        if (sdPrefab == null)
        {
            log.AppendLine("  [!] SD_ManeequinPrefab 없음: " + SDPrefabPath);
            return;
        }

        // ── AgentSpawner._defaultModelPrefab ────────────────────
        var spawner = Object.FindFirstObjectByType<OpenDesk.Presentation.Character.AgentSpawner>();
        if (spawner != null)
        {
            var so = new SerializedObject(spawner);
            var prop = so.FindProperty("_defaultModelPrefab");
            if (prop != null)
            {
                prop.objectReferenceValue = sdPrefab;
                so.ApplyModifiedProperties();
                log.AppendLine("  -> AgentSpawner._defaultModelPrefab = SD_ManeequinPrefab");
            }
        }
        else
        {
            log.AppendLine("  [!] AgentSpawner를 씬에서 찾을 수 없음");
        }

        // ── AgentOfficeBootstrapper._modelPrefabs 배열 갱신 ─────
        var bootstrapper = Object.FindFirstObjectByType<OpenDesk.Presentation.Character.AgentOfficeBootstrapper>();
        if (bootstrapper != null)
        {
            var so = new SerializedObject(bootstrapper);
            var arrayProp = so.FindProperty("_modelPrefabs");
            if (arrayProp != null && arrayProp.isArray)
            {
                // 기존 배열의 모든 엔트리 Prefab을 SD_Maneqquin으로 교체
                bool updated = false;
                for (int i = 0; i < arrayProp.arraySize; i++)
                {
                    var element = arrayProp.GetArrayElementAtIndex(i);
                    var prefabProp = element.FindPropertyRelative("Prefab");
                    if (prefabProp != null && prefabProp.objectReferenceValue != sdPrefab)
                    {
                        prefabProp.objectReferenceValue = sdPrefab;
                        updated = true;
                    }
                }

                // 배열이 비었으면 엔트리 하나 추가
                if (arrayProp.arraySize == 0)
                {
                    arrayProp.InsertArrayElementAtIndex(0);
                    var element = arrayProp.GetArrayElementAtIndex(0);
                    element.FindPropertyRelative("PrefabName").stringValue = "SD_Maneqquin";
                    element.FindPropertyRelative("Prefab").objectReferenceValue = sdPrefab;
                    updated = true;
                }

                if (updated)
                {
                    so.ApplyModifiedProperties();
                    log.AppendLine("  -> AgentOfficeBootstrapper._modelPrefabs 갱신 완료");
                }
                else
                {
                    log.AppendLine("  -> AgentOfficeBootstrapper._modelPrefabs 이미 최신");
                }
            }
        }
        else
        {
            log.AppendLine("  [!] AgentOfficeBootstrapper를 씬에서 찾을 수 없음");
        }

        // ── AgentCreationBridge._defaultModelPrefab ─────────────
        var bridge = Object.FindFirstObjectByType<OpenDesk.Presentation.Character.AgentCreationBridge>();
        if (bridge != null)
        {
            var so = new SerializedObject(bridge);
            var prop = so.FindProperty("_defaultModelPrefab");
            if (prop != null)
            {
                prop.objectReferenceValue = sdPrefab;
                so.ApplyModifiedProperties();
                log.AppendLine("  -> AgentCreationBridge._defaultModelPrefab = SD_ManeequinPrefab");
            }
        }
    }
}
#endif
