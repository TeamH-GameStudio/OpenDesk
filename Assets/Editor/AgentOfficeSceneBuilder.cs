using OpenDesk.AgentCreation.Installers;
using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.UI.AgentCreation;
using OpenDesk.Presentation.UI.Session;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Agent Office 씬 자동 생성 — Office_35 프리팹 + SpawnPoints + 위저드 UI + HUD 통합.
/// 메뉴: Tools → OpenDesk → Build Agent Office Scene
///
/// 사전 조건: Tools → OpenDesk → Build Agent Prefabs 먼저 실행
/// </summary>
public static class AgentOfficeSceneBuilder
{
    private const string OfficePrefabPath   = "Assets/Mnostva_Art/Prefabs/Rooms/Office_35.prefab";
    private const string CubePrefabPath    = "Assets/05.Prefabs/Agent/AgentCube_Placeholder.prefab";
    private const string Agent3DPrefabPath = "Assets/05.Prefabs/Agent/Model_Agent3D.prefab";
    private const string HudPrefabPath     = "Assets/05.Prefabs/Agent/AgentHUD.prefab";
    private const string FontAssetPath     = "Assets/NotoSansKR-VariableFont_wght.asset";

    private static TMP_FontAsset _font;

    // 컬러
    private static readonly Color32 ColBg         = new(20, 22, 30, 255);
    private static readonly Color32 ColPanel      = new(30, 32, 45, 255);
    private static readonly Color32 ColBtnPrimary = new(80, 140, 255, 255);
    private static readonly Color32 ColBtnSec     = new(60, 62, 80, 255);
    private static readonly Color32 ColWhite      = new(255, 255, 255, 255);
    private static readonly Color32 ColTextSub    = new(160, 160, 180, 255);

    [MenuItem("Tools/OpenDesk/Build Agent Office Scene", false, 130)]
    public static void BuildScene()
    {
        // 프리팹 확인
        var officePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OfficePrefabPath);
        var cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CubePrefabPath);
        var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);

        if (officePrefab == null)
        {
            EditorUtility.DisplayDialog("오류",
                $"Office_35 프리팹을 찾을 수 없습니다.\n{OfficePrefabPath}", "OK");
            return;
        }

        if (cubePrefab == null || hudPrefab == null)
        {
            if (EditorUtility.DisplayDialog("프리팹 없음",
                "AgentCube/HUD 프리팹이 없습니다.\n먼저 'Build Agent Prefabs'를 실행할까요?",
                "실행", "취소"))
            {
                AgentPrefabBuilder.BuildAll();
                cubePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CubePrefabPath);
                hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            }
            else return;
        }

        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        // ── 새 씬 ────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "AgentOfficeScene";

        // ── 카메라 ───────────────────────────────────────────
        var camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        var cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 50;
        // Office 내부를 정면에서 바라보는 위치 (앞쪽에서 안쪽을 봄)
        camObj.transform.position = new Vector3(1.35f, 5.1f, 6.1f);
        camObj.transform.rotation = Quaternion.Euler(31.86f, -162f, 0f);
        camObj.AddComponent<AudioListener>();

        // ── 라이트 ───────────────────────────────────────────
        var lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.2f;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 포인트 라이트 (실내 보조)
        var pointLight = new GameObject("Point Light");
        var pLight = pointLight.AddComponent<Light>();
        pLight.type = LightType.Point;
        pLight.color = new Color(1f, 0.95f, 0.85f);
        pLight.intensity = 1.5f;
        pLight.range = 15f;
        pointLight.transform.position = new Vector3(4f, 4f, 2f);

        // ── Office_35 프리팹 배치 ────────────────────────────
        var officeInstance = (GameObject)PrefabUtility.InstantiatePrefab(officePrefab);
        officeInstance.name = "Office_35";
        officeInstance.transform.position = Vector3.zero;

        // Office 바닥에 static Navigation 설정 (NavMesh 베이크용)
        SetStaticRecursive(officeInstance, true);

        // NavMeshSurface 컴포넌트 추가 (바닥 + 벽 + 사물 인식)
        var navSurface = officeInstance.AddComponent<NavMeshSurface>();
        navSurface.collectObjects = CollectObjects.Children;
        navSurface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        navSurface.defaultArea = 0; // Walkable
        navSurface.agentTypeID = 0; // Humanoid
        navSurface.BuildNavMesh();
        Debug.Log("[AgentOfficeSceneBuilder] NavMesh 베이크 완료");

        // ── SpawnPoints (Office 바닥 높이 감지) ──────────────
        var spawnRoot = new GameObject("SpawnPoints");
        spawnRoot.transform.position = Vector3.zero;

        // 바닥 높이 자동 감지 (Floor 메쉬 기준)
        float floorY = DetectFloorHeight(officeInstance);

        var spawnPositions = new Vector3[]
        {
            new(1.5f, floorY, 1.5f),
            new(4f,   floorY, 1.5f),
            new(6.5f, floorY, 1.5f),
            new(4f,   floorY, 4f),
        };

        var spawnTransforms = new Transform[spawnPositions.Length];
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var sp = new GameObject($"SpawnPoint_{i}");
            sp.transform.SetParent(spawnRoot.transform, false);
            sp.transform.localPosition = spawnPositions[i];
            sp.transform.localRotation = Quaternion.Euler(0, 180, 0); // 카메라를 향함

            // 에디터에서 위치 확인용 아이콘
            var icon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            icon.name = "Marker";
            icon.transform.SetParent(sp.transform, false);
            icon.transform.localPosition = new Vector3(0, 0.02f, 0);
            icon.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            var renderer = icon.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.3f, 0.8f, 1f, 0.3f);
            renderer.sharedMaterial = mat;
            Object.DestroyImmediate(icon.GetComponent<CapsuleCollider>());

            spawnTransforms[i] = sp.transform;
        }

        // ── AgentSpawner ─────────────────────────────────────
        var spawnerObj = new GameObject("AgentSpawner");
        var spawner = spawnerObj.AddComponent<AgentSpawner>();

        var spawnerSO = new SerializedObject(spawner);
        // SpawnPoints 배열
        var spawnPointsProp = spawnerSO.FindProperty("_spawnPoints");
        spawnPointsProp.arraySize = spawnTransforms.Length;
        for (int i = 0; i < spawnTransforms.Length; i++)
            spawnPointsProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnTransforms[i];

        // Model_Agent3D가 있으면 우선 사용, 없으면 큐브 폴백
        var agent3DPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Agent3DPrefabPath);
        spawnerSO.FindProperty("_defaultModelPrefab").objectReferenceValue = agent3DPrefab != null ? agent3DPrefab : cubePrefab;
        spawnerSO.FindProperty("_hudPrefab").objectReferenceValue = hudPrefab;
        spawnerSO.FindProperty("_hudHeight").floatValue = 2.2f; // 3D 모델 높이에 맞춤
        spawnerSO.ApplyModifiedPropertiesWithoutUndo();

        // ── OrbitCamera ──────────────────────────────────────
        camObj.AddComponent<OrbitCamera>();
        var orbitSO = new SerializedObject(camObj.GetComponent<OrbitCamera>());
        orbitSO.FindProperty("_targetPoint").vector3Value = new Vector3(3f, 1f, 3f);
        orbitSO.ApplyModifiedPropertiesWithoutUndo();

        // ── Bootstrapper (PlayerPrefs → 자동 소환) ──────────
        var bootObj = new GameObject("AgentOfficeBootstrapper");
        var boot = bootObj.AddComponent<AgentOfficeBootstrapper>();

        var bootSO = new SerializedObject(boot);
        bootSO.FindProperty("_spawner").objectReferenceValue = spawner;

        // 모델 프리팹 매핑 등록
        var prefabsProp = bootSO.FindProperty("_modelPrefabs");
        prefabsProp.arraySize = agent3DPrefab != null ? 2 : 1;
        var entry0 = prefabsProp.GetArrayElementAtIndex(0);
        entry0.FindPropertyRelative("PrefabName").stringValue = "Model_Agent3D";
        entry0.FindPropertyRelative("Prefab").objectReferenceValue = agent3DPrefab != null ? agent3DPrefab : cubePrefab;
        if (agent3DPrefab != null)
        {
            var entry1 = prefabsProp.GetArrayElementAtIndex(1);
            entry1.FindPropertyRelative("PrefabName").stringValue = "AgentCube_Placeholder";
            entry1.FindPropertyRelative("Prefab").objectReferenceValue = cubePrefab;
        }
        bootSO.ApplyModifiedPropertiesWithoutUndo();

        // ── 세션 리스트 UI (Screen Space Overlay) ────────────
        var sessionItemPrefab = BuildSessionItemPrefab();

        var uiCanvasObj = new GameObject("UICanvas");
        var uiCanvas = uiCanvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiCanvas.sortingOrder = 5;
        var uiScaler = uiCanvasObj.AddComponent<CanvasScaler>();
        uiScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        uiScaler.referenceResolution = new Vector2(1280, 720);
        uiScaler.matchWidthOrHeight = 0.5f;
        uiCanvasObj.AddComponent<GraphicRaycaster>();
        var uiCanvasRT = uiCanvasObj.GetComponent<RectTransform>();

        // 세션 패널 (우측 슬라이드인 — 기본 숨김)
        var sessionPanel = new GameObject("SessionPanel");
        sessionPanel.transform.SetParent(uiCanvasRT, false);
        var sessionPanelRT = sessionPanel.AddComponent<RectTransform>();
        sessionPanelRT.anchorMin = new Vector2(0.65f, 0);
        sessionPanelRT.anchorMax = Vector2.one;
        sessionPanelRT.offsetMin = Vector2.zero;
        sessionPanelRT.offsetMax = Vector2.zero;
        var sessionPanelImg = sessionPanel.AddComponent<Image>();
        sessionPanelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);

        // 헤더 영역
        var sessionHeader = new GameObject("Header");
        sessionHeader.transform.SetParent(sessionPanel.transform, false);
        var sessionHeaderRT = sessionHeader.AddComponent<RectTransform>();
        sessionHeaderRT.anchorMin = new Vector2(0, 1);
        sessionHeaderRT.anchorMax = Vector2.one;
        sessionHeaderRT.offsetMin = new Vector2(0, -80);
        sessionHeaderRT.offsetMax = Vector2.zero;
        var headerBg = sessionHeader.AddComponent<Image>();
        headerBg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        // 에이전트 이름
        var agentNameRT = CreateTMP("AgentName", sessionHeaderRT, "에이전트", 20, ColWhite);
        agentNameRT.anchorMin = new Vector2(0, 0.5f);
        agentNameRT.anchorMax = new Vector2(0.6f, 1);
        agentNameRT.offsetMin = new Vector2(15, 0);
        agentNameRT.offsetMax = Vector2.zero;
        agentNameRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
        agentNameRT.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;

        // 역할 텍스트
        var agentRoleRT = CreateTMP("AgentRole", sessionHeaderRT, "역할", 13, ColTextSub);
        agentRoleRT.anchorMin = new Vector2(0, 0);
        agentRoleRT.anchorMax = new Vector2(0.6f, 0.5f);
        agentRoleRT.offsetMin = new Vector2(15, 0);
        agentRoleRT.offsetMax = Vector2.zero;
        agentRoleRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // 닫기 버튼
        var closeBtnObj = CreateStyledButton("CloseBtn", sessionHeaderRT, "X", new Color32(80, 80, 100, 255), 16);
        var closeBtnRT = closeBtnObj.GetComponent<RectTransform>();
        closeBtnRT.anchorMin = new Vector2(1, 0.5f);
        closeBtnRT.anchorMax = new Vector2(1, 0.5f);
        closeBtnRT.pivot = new Vector2(1, 0.5f);
        closeBtnRT.anchoredPosition = new Vector2(-10, 0);
        closeBtnRT.sizeDelta = new Vector2(36, 36);

        // 새 대화 버튼
        var newSessBtnObj = CreateStyledButton("NewSessionBtn", sessionHeaderRT,
            "+ 새 대화", new Color32(80, 140, 255, 255), 13);
        var newSessBtnRT = newSessBtnObj.GetComponent<RectTransform>();
        newSessBtnRT.anchorMin = new Vector2(0.6f, 0.15f);
        newSessBtnRT.anchorMax = new Vector2(0.88f, 0.85f);
        newSessBtnRT.offsetMin = Vector2.zero;
        newSessBtnRT.offsetMax = Vector2.zero;

        // 스크롤 리스트
        var scrollObj = new GameObject("ScrollView");
        scrollObj.transform.SetParent(sessionPanel.transform, false);
        var scrollObjRT = scrollObj.AddComponent<RectTransform>();
        scrollObjRT.anchorMin = Vector2.zero;
        scrollObjRT.anchorMax = new Vector2(1, 1);
        scrollObjRT.offsetMin = new Vector2(0, 0);
        scrollObjRT.offsetMax = new Vector2(0, -80);
        scrollObj.AddComponent<Image>().color = Color.clear;
        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollObj.transform, false);
        var viewportRT = viewport.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        viewport.AddComponent<RectMask2D>();

        var listContent = new GameObject("Content");
        listContent.transform.SetParent(viewport.transform, false);
        var listContentRT = listContent.AddComponent<RectTransform>();
        listContentRT.anchorMin = new Vector2(0, 1);
        listContentRT.anchorMax = Vector2.one;
        listContentRT.pivot = new Vector2(0.5f, 1);
        listContentRT.offsetMin = Vector2.zero;
        listContentRT.offsetMax = Vector2.zero;

        var vlg = listContent.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var csf = listContent.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = listContentRT;

        // 빈 상태 메시지
        var emptyState = new GameObject("EmptyState");
        emptyState.transform.SetParent(sessionPanel.transform, false);
        var emptyRT = emptyState.AddComponent<RectTransform>();
        emptyRT.anchorMin = new Vector2(0.1f, 0.3f);
        emptyRT.anchorMax = new Vector2(0.9f, 0.6f);
        emptyRT.offsetMin = Vector2.zero;
        emptyRT.offsetMax = Vector2.zero;
        var emptyTMP = emptyState.AddComponent<TextMeshProUGUI>();
        emptyTMP.text = "아직 대화가 없습니다.\n'+ 새 대화'를 눌러보세요.";
        emptyTMP.fontSize = 15;
        emptyTMP.color = new Color(0.5f, 0.5f, 0.6f);
        emptyTMP.alignment = TextAlignmentOptions.Center;
        if (_font != null) emptyTMP.font = _font;

        // SessionListController 부착 + 바인딩
        var sessionListCtrl = sessionPanel.AddComponent<SessionListController>();
        var slSO = new SerializedObject(sessionListCtrl);
        slSO.FindProperty("_panelRoot").objectReferenceValue = sessionPanel;
        slSO.FindProperty("_closeButton").objectReferenceValue = closeBtnObj.GetComponent<Button>();
        slSO.FindProperty("_headerAgentName").objectReferenceValue = agentNameRT.GetComponent<TMP_Text>();
        slSO.FindProperty("_headerAgentRole").objectReferenceValue = agentRoleRT.GetComponent<TMP_Text>();
        slSO.FindProperty("_newSessionButton").objectReferenceValue = newSessBtnObj.GetComponent<Button>();
        slSO.FindProperty("_scrollRect").objectReferenceValue = scrollRect;
        slSO.FindProperty("_listContent").objectReferenceValue = listContentRT;
        slSO.FindProperty("_sessionItemPrefab").objectReferenceValue = sessionItemPrefab;
        slSO.FindProperty("_emptyState").objectReferenceValue = emptyState;
        // _chatPanel, _claudeClient는 아래에서 생성 후 바인딩

        // ── 채팅 패널 UI ────────────────────────────────────
        var chatBubbles = BuildChatBubblePrefabs();

        var chatPanel = new GameObject("ChatPanel");
        chatPanel.transform.SetParent(uiCanvasRT, false);
        var chatPanelRT = chatPanel.AddComponent<RectTransform>();
        chatPanelRT.anchorMin = new Vector2(0.25f, 0);
        chatPanelRT.anchorMax = new Vector2(0.65f, 1);
        chatPanelRT.offsetMin = Vector2.zero;
        chatPanelRT.offsetMax = Vector2.zero;
        var chatPanelImg = chatPanel.AddComponent<Image>();
        chatPanelImg.color = new Color(0.1f, 0.1f, 0.14f, 0.97f);

        // 채팅 헤더
        var chatHeader = new GameObject("ChatHeader");
        chatHeader.transform.SetParent(chatPanel.transform, false);
        var chatHeaderRT = chatHeader.AddComponent<RectTransform>();
        chatHeaderRT.anchorMin = new Vector2(0, 1);
        chatHeaderRT.anchorMax = Vector2.one;
        chatHeaderRT.offsetMin = new Vector2(0, -55);
        chatHeaderRT.offsetMax = Vector2.zero;
        chatHeader.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.18f, 1f);

        var chatTitleRT = CreateTMP("ChatTitle", chatHeaderRT, "에이전트", 17, ColWhite);
        chatTitleRT.anchorMin = new Vector2(0, 0);
        chatTitleRT.anchorMax = new Vector2(0.7f, 1);
        chatTitleRT.offsetMin = new Vector2(12, 0);
        chatTitleRT.offsetMax = Vector2.zero;
        chatTitleRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
        chatTitleRT.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;

        var chatSubRT = CreateTMP("ChatSubtitle", chatHeaderRT, "대화 중", 11, ColTextSub);
        chatSubRT.anchorMin = new Vector2(0.7f, 0);
        chatSubRT.anchorMax = Vector2.one;
        chatSubRT.offsetMin = Vector2.zero;
        chatSubRT.offsetMax = new Vector2(-8, 0);
        chatSubRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineRight;

        var chatBackObj = CreateStyledButton("BackBtn", chatHeaderRT, "←", new Color32(70, 70, 90, 255), 18);
        var chatBackRT = chatBackObj.GetComponent<RectTransform>();
        chatBackRT.anchorMin = new Vector2(1, 0.5f);
        chatBackRT.anchorMax = new Vector2(1, 0.5f);
        chatBackRT.pivot = new Vector2(1, 0.5f);
        chatBackRT.anchoredPosition = new Vector2(-5, 0);
        chatBackRT.sizeDelta = new Vector2(36, 36);

        // 메시지 스크롤 영역
        var chatScrollObj = new GameObject("ChatScrollView");
        chatScrollObj.transform.SetParent(chatPanel.transform, false);
        var chatScrollRT = chatScrollObj.AddComponent<RectTransform>();
        chatScrollRT.anchorMin = Vector2.zero;
        chatScrollRT.anchorMax = Vector2.one;
        chatScrollRT.offsetMin = new Vector2(0, 55); // 하단 입력 영역
        chatScrollRT.offsetMax = new Vector2(0, -55); // 상단 헤더
        chatScrollObj.AddComponent<Image>().color = Color.clear;
        var chatScroll = chatScrollObj.AddComponent<ScrollRect>();
        chatScroll.horizontal = false;
        chatScroll.movementType = ScrollRect.MovementType.Clamped;

        var chatViewport = new GameObject("Viewport");
        chatViewport.transform.SetParent(chatScrollObj.transform, false);
        var chatViewportRT = chatViewport.AddComponent<RectTransform>();
        chatViewportRT.anchorMin = Vector2.zero;
        chatViewportRT.anchorMax = Vector2.one;
        chatViewportRT.offsetMin = Vector2.zero;
        chatViewportRT.offsetMax = Vector2.zero;
        chatViewport.AddComponent<RectMask2D>();

        var chatContent = new GameObject("Content");
        chatContent.transform.SetParent(chatViewport.transform, false);
        var chatContentRT = chatContent.AddComponent<RectTransform>();
        chatContentRT.anchorMin = new Vector2(0, 1);
        chatContentRT.anchorMax = Vector2.one;
        chatContentRT.pivot = new Vector2(0.5f, 1);
        chatContentRT.offsetMin = Vector2.zero;
        chatContentRT.offsetMax = Vector2.zero;

        var chatVlg = chatContent.AddComponent<VerticalLayoutGroup>();
        chatVlg.spacing = 6;
        chatVlg.padding = new RectOffset(10, 10, 8, 8);
        chatVlg.childForceExpandWidth = true;
        chatVlg.childForceExpandHeight = false;
        chatVlg.childControlWidth = true;
        chatVlg.childControlHeight = true;

        var chatCsf = chatContent.AddComponent<ContentSizeFitter>();
        chatCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        chatScroll.viewport = chatViewportRT;
        chatScroll.content = chatContentRT;

        // 빈 힌트
        var chatEmptyHint = new GameObject("EmptyHint");
        chatEmptyHint.transform.SetParent(chatPanel.transform, false);
        var chatEmptyRT = chatEmptyHint.AddComponent<RectTransform>();
        chatEmptyRT.anchorMin = new Vector2(0.1f, 0.35f);
        chatEmptyRT.anchorMax = new Vector2(0.9f, 0.65f);
        chatEmptyRT.offsetMin = Vector2.zero;
        chatEmptyRT.offsetMax = Vector2.zero;
        var chatEmptyTMP = chatEmptyHint.AddComponent<TextMeshProUGUI>();
        chatEmptyTMP.text = "메시지를 보내 대화를 시작하세요.";
        chatEmptyTMP.fontSize = 14;
        chatEmptyTMP.color = new Color(0.45f, 0.45f, 0.55f);
        chatEmptyTMP.alignment = TextAlignmentOptions.Center;
        if (_font != null) chatEmptyTMP.font = _font;

        // 입력 영역
        var chatInputArea = new GameObject("InputArea");
        chatInputArea.transform.SetParent(chatPanel.transform, false);
        var chatInputAreaRT = chatInputArea.AddComponent<RectTransform>();
        chatInputAreaRT.anchorMin = Vector2.zero;
        chatInputAreaRT.anchorMax = new Vector2(1, 0);
        chatInputAreaRT.offsetMin = Vector2.zero;
        chatInputAreaRT.offsetMax = new Vector2(0, 55);
        chatInputArea.AddComponent<Image>().color = new Color(0.13f, 0.13f, 0.18f, 1f);

        // InputField
        var chatInputBg = new GameObject("InputBg");
        chatInputBg.transform.SetParent(chatInputArea.transform, false);
        var chatInputBgRT = chatInputBg.AddComponent<RectTransform>();
        chatInputBgRT.anchorMin = Vector2.zero;
        chatInputBgRT.anchorMax = Vector2.one;
        chatInputBgRT.offsetMin = new Vector2(10, 8);
        chatInputBgRT.offsetMax = new Vector2(-65, -8);
        chatInputBg.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.24f);

        var chatInputFieldObj = new GameObject("InputField");
        chatInputFieldObj.transform.SetParent(chatInputBg.transform, false);
        var chatInputFieldRT = chatInputFieldObj.AddComponent<RectTransform>();
        chatInputFieldRT.anchorMin = Vector2.zero;
        chatInputFieldRT.anchorMax = Vector2.one;
        chatInputFieldRT.offsetMin = new Vector2(10, 2);
        chatInputFieldRT.offsetMax = new Vector2(-10, -2);

        var chatTextArea = new GameObject("Text Area");
        chatTextArea.transform.SetParent(chatInputFieldObj.transform, false);
        var chatTextAreaRT = chatTextArea.AddComponent<RectTransform>();
        chatTextAreaRT.anchorMin = Vector2.zero;
        chatTextAreaRT.anchorMax = Vector2.one;
        chatTextAreaRT.offsetMin = Vector2.zero;
        chatTextAreaRT.offsetMax = Vector2.zero;
        chatTextArea.AddComponent<RectMask2D>();

        var chatPlaceholder = new GameObject("Placeholder");
        chatPlaceholder.transform.SetParent(chatTextArea.transform, false);
        var chatPlaceholderRT = chatPlaceholder.AddComponent<RectTransform>();
        chatPlaceholderRT.anchorMin = Vector2.zero;
        chatPlaceholderRT.anchorMax = Vector2.one;
        chatPlaceholderRT.offsetMin = Vector2.zero;
        chatPlaceholderRT.offsetMax = Vector2.zero;
        var chatPlaceholderTMP = chatPlaceholder.AddComponent<TextMeshProUGUI>();
        chatPlaceholderTMP.text = "메시지를 입력하세요...";
        chatPlaceholderTMP.fontSize = 14;
        chatPlaceholderTMP.color = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        if (_font != null) chatPlaceholderTMP.font = _font;

        var chatTextObj = new GameObject("Text");
        chatTextObj.transform.SetParent(chatTextArea.transform, false);
        var chatTextObjRT = chatTextObj.AddComponent<RectTransform>();
        chatTextObjRT.anchorMin = Vector2.zero;
        chatTextObjRT.anchorMax = Vector2.one;
        chatTextObjRT.offsetMin = Vector2.zero;
        chatTextObjRT.offsetMax = Vector2.zero;
        var chatTextTMP = chatTextObj.AddComponent<TextMeshProUGUI>();
        chatTextTMP.fontSize = 14;
        chatTextTMP.color = Color.white;
        chatTextTMP.enableWordWrapping = true;
        if (_font != null) chatTextTMP.font = _font;

        var chatInputField = chatInputFieldObj.AddComponent<TMP_InputField>();
        chatInputField.textViewport = chatTextAreaRT;
        chatInputField.textComponent = chatTextTMP;
        chatInputField.placeholder = chatPlaceholderTMP;
        chatInputField.lineType = TMP_InputField.LineType.SingleLine;

        // 전송 버튼
        var chatSendObj = CreateStyledButton("SendBtn", chatInputAreaRT, "→", new Color32(80, 140, 255, 255), 20);
        var chatSendRT = chatSendObj.GetComponent<RectTransform>();
        chatSendRT.anchorMin = new Vector2(1, 0);
        chatSendRT.anchorMax = new Vector2(1, 1);
        chatSendRT.pivot = new Vector2(1, 0.5f);
        chatSendRT.offsetMin = new Vector2(-58, 8);
        chatSendRT.offsetMax = new Vector2(-8, -8);

        // Claude 미들웨어 컴포넌트 (ChatPanel + SessionList 공유)
        var claudeObj = new GameObject("ClaudeMiddleware");
        var claudeClient = claudeObj.AddComponent<OpenDesk.Claude.ClaudeWebSocketClient>();
        claudeObj.AddComponent<OpenDesk.Claude.MiddlewareLauncher>();

        // ChatPanelController 부착 + 바인딩
        var chatCtrl = chatPanel.AddComponent<ChatPanelController>();
        var cpSO = new SerializedObject(chatCtrl);
        cpSO.FindProperty("_panelRoot").objectReferenceValue = chatPanel;
        cpSO.FindProperty("_backButton").objectReferenceValue = chatBackObj.GetComponent<Button>();
        cpSO.FindProperty("_headerTitle").objectReferenceValue = chatTitleRT.GetComponent<TMP_Text>();
        cpSO.FindProperty("_headerSubtitle").objectReferenceValue = chatSubRT.GetComponent<TMP_Text>();
        cpSO.FindProperty("_scrollRect").objectReferenceValue = chatScroll;
        cpSO.FindProperty("_messageContent").objectReferenceValue = chatContentRT;
        cpSO.FindProperty("_userBubblePrefab").objectReferenceValue = chatBubbles.user;
        cpSO.FindProperty("_agentBubblePrefab").objectReferenceValue = chatBubbles.agent;
        cpSO.FindProperty("_systemBubblePrefab").objectReferenceValue = chatBubbles.system;
        cpSO.FindProperty("_inputField").objectReferenceValue = chatInputField;
        cpSO.FindProperty("_sendButton").objectReferenceValue = chatSendObj.GetComponent<Button>();
        cpSO.FindProperty("_emptyHint").objectReferenceValue = chatEmptyHint;
        cpSO.FindProperty("_claudeClient").objectReferenceValue = claudeClient;
        cpSO.ApplyModifiedPropertiesWithoutUndo();

        chatPanel.SetActive(false); // 기본 숨김

        // SessionListController에 chatPanel + claudeClient 바인딩
        slSO.FindProperty("_chatPanel").objectReferenceValue = chatCtrl;
        slSO.FindProperty("_claudeClient").objectReferenceValue = claudeClient;
        slSO.ApplyModifiedPropertiesWithoutUndo();

        // ── AgentClickHandler ───────────────────────────────
        var clickHandlerObj = new GameObject("AgentClickHandler");
        var clickHandler = clickHandlerObj.AddComponent<AgentClickHandler>();
        var chSO = new SerializedObject(clickHandler);
        chSO.FindProperty("_spawner").objectReferenceValue = spawner;
        chSO.FindProperty("_sessionList").objectReferenceValue = sessionListCtrl;
        chSO.FindProperty("_mainCamera").objectReferenceValue = cam;
        chSO.ApplyModifiedPropertiesWithoutUndo();

        // ── VContainer ──────────────────────────────────────
        var vcObj = new GameObject("[VContainer] AgentOfficeInstaller");
        vcObj.AddComponent<AgentOfficeInstaller>();

        // ── EventSystem ─────────────────────────────────────
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ── .env 파일 확인 ───────────────────────────────────
        EnsureEnvFile();

        // ── 씬 저장 ─────────────────────────────────────────
        var scenePath = "Assets/01.Scenes/AgentProtocolTestScene.unity";
        EnsureFolder("Assets/01.Scenes");
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[AgentOfficeSceneBuilder] 씬 생성 완료: {scenePath}");
        EditorUtility.DisplayDialog("완료",
            "AgentProtocolTestScene이 생성되었습니다.\n\n" +
            "• Office_35 + NavMesh + SpawnPoint 4개\n" +
            "• AgentSpawner + HUD 프리팹\n" +
            "• ClaudeWebSocketClient + MiddlewareLauncher\n" +
            "• SessionListController + ChatPanelController\n" +
            "• AgentClickHandler (에이전트 클릭 -> 세션 패널)\n\n" +
            "사용법:\n" +
            "1. Middleware/.env에 ANTHROPIC_API_KEY 설정\n" +
            "2. Unity Play (서버 자동 실행)\n" +
            "3. 에이전트 클릭 -> 세션 -> 채팅",
            "OK");
    }

    /// <summary>.env 파일이 없으면 템플릿 생성</summary>
    private static void EnsureEnvFile()
    {
        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, ".."));
        var envPath = System.IO.Path.Combine(projectRoot, "Middleware", ".env");

        if (System.IO.File.Exists(envPath))
        {
            Debug.Log($"[AgentOfficeSceneBuilder] .env 존재: {envPath}");
            return;
        }

        System.IO.File.WriteAllText(envPath,
            "# OpenDesk Middleware\n" +
            "ANTHROPIC_API_KEY=your-api-key-here\n" +
            "# BRAVE_API_KEY=your-brave-key-here\n");

        Debug.LogWarning($"[AgentOfficeSceneBuilder] .env 생성됨 — ANTHROPIC_API_KEY 설정 필요: {envPath}");
    }

    // ================================================================
    //  유틸
    // ================================================================

    private static GameObject CreateStyledButton(string name, RectTransform parent, string label, Color32 bgColor, int fontSize)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        var img = obj.AddComponent<Image>();
        img.color = (Color)bgColor;
        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.color = (Color)ColWhite;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        if (_font != null) tmp.font = _font;

        return obj;
    }

    private static RectTransform CreateTMP(string name, RectTransform parent, string text, int fontSize, Color32 color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = (Color)color;
        tmp.enableWordWrapping = true;
        if (_font != null) tmp.font = _font;
        return rt;
    }

    // ================================================================
    //  세션 아이템 프리팹 생성
    // ================================================================

    private static GameObject BuildSessionItemPrefab()
    {
        // SessionItem: 높이 70, 클릭 가능, 3줄 텍스트 + 보더
        var root = new GameObject("SessionItem");
        var rootRT = root.AddComponent<RectTransform>();

        var le = root.AddComponent<LayoutElement>();
        le.preferredHeight = 70;
        le.flexibleWidth = 1;

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.16f, 0.16f, 0.22f, 1f);

        var btn = root.AddComponent<Button>();
        btn.targetGraphic = bg;
        var btnColors = btn.colors;
        btnColors.normalColor = new Color(0.16f, 0.16f, 0.22f);
        btnColors.highlightedColor = new Color(0.2f, 0.2f, 0.28f);
        btnColors.pressedColor = new Color(0.14f, 0.14f, 0.2f);
        btn.colors = btnColors;

        // 제목 (TMP 0)
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(root.transform, false);
        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 0.6f);
        titleRT.anchorMax = new Vector2(0.7f, 1);
        titleRT.offsetMin = new Vector2(12, 0);
        titleRT.offsetMax = new Vector2(0, -4);
        var titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "새 대화";
        titleTMP.fontSize = 15;
        titleTMP.color = Color.white;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.MidlineLeft;
        if (_font != null) titleTMP.font = _font;

        // 미리보기 (TMP 1)
        var previewObj = new GameObject("Preview");
        previewObj.transform.SetParent(root.transform, false);
        var previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = new Vector2(0, 0.15f);
        previewRT.anchorMax = new Vector2(0.85f, 0.6f);
        previewRT.offsetMin = new Vector2(12, 0);
        previewRT.offsetMax = Vector2.zero;
        var previewTMP = previewObj.AddComponent<TextMeshProUGUI>();
        previewTMP.text = "아직 대화가 없습니다";
        previewTMP.fontSize = 12;
        previewTMP.color = new Color(0.55f, 0.55f, 0.65f);
        previewTMP.alignment = TextAlignmentOptions.MidlineLeft;
        previewTMP.overflowMode = TextOverflowModes.Ellipsis;
        if (_font != null) previewTMP.font = _font;

        // 시간 (TMP 2)
        var timeObj = new GameObject("Time");
        timeObj.transform.SetParent(root.transform, false);
        var timeRT = timeObj.AddComponent<RectTransform>();
        timeRT.anchorMin = new Vector2(0.7f, 0.6f);
        timeRT.anchorMax = new Vector2(1, 1);
        timeRT.offsetMin = Vector2.zero;
        timeRT.offsetMax = new Vector2(-8, -4);
        var timeTMP = timeObj.AddComponent<TextMeshProUGUI>();
        timeTMP.text = "방금";
        timeTMP.fontSize = 11;
        timeTMP.color = new Color(0.45f, 0.45f, 0.55f);
        timeTMP.alignment = TextAlignmentOptions.MidlineRight;
        if (_font != null) timeTMP.font = _font;

        // 활성 보더 (좌측 강조 바 — 기본 숨김)
        var border = new GameObject("Border");
        border.transform.SetParent(root.transform, false);
        var borderRT = border.AddComponent<RectTransform>();
        borderRT.anchorMin = new Vector2(0, 0.05f);
        borderRT.anchorMax = new Vector2(0, 0.95f);
        borderRT.offsetMin = Vector2.zero;
        borderRT.offsetMax = new Vector2(4, 0);
        var borderImg = border.AddComponent<Image>();
        borderImg.color = new Color(0.31f, 0.55f, 1f, 1f); // 파란 강조
        border.SetActive(false);

        // 프리팹 저장
        var dir = "Assets/05.Prefabs/Agent";
        EnsureFolder(dir);
        var path = $"{dir}/SessionItem.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        Debug.Log($"[AgentOfficeSceneBuilder] 세션 아이템 프리팹: {path}");
        return prefab;
    }

    // ================================================================
    //  채팅 버블 프리팹 생성
    // ================================================================

    private static (GameObject user, GameObject agent, GameObject system) BuildChatBubblePrefabs()
    {
        var dir = "Assets/05.Prefabs/Agent";
        EnsureFolder(dir);

        var user  = BuildBubblePrefab("Bubble_User",   new Color(0.22f, 0.47f, 0.87f), TextAlignmentOptions.TopRight, dir);
        var agent = BuildBubblePrefab("Bubble_Agent",  new Color(0.22f, 0.22f, 0.3f),  TextAlignmentOptions.TopLeft, dir);
        var sys   = BuildBubblePrefab("Bubble_System", Color.clear,                     TextAlignmentOptions.Center, dir);

        return (user, agent, sys);
    }

    private static GameObject BuildBubblePrefab(string name, Color bgColor, TextAlignmentOptions align, string dir)
    {
        var root = new GameObject(name);
        root.AddComponent<RectTransform>();

        if (bgColor.a > 0.01f)
        {
            var bg = root.AddComponent<Image>();
            bg.color = bgColor;
        }

        var vlgB = root.AddComponent<VerticalLayoutGroup>();
        vlgB.padding = new RectOffset(12, 12, 8, 8);
        vlgB.childForceExpandWidth = true;
        vlgB.childForceExpandHeight = false;
        vlgB.childControlWidth = true;
        vlgB.childControlHeight = true;

        var csfB = root.AddComponent<ContentSizeFitter>();
        csfB.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var leB = root.AddComponent<LayoutElement>();
        leB.flexibleWidth = 1;

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(root.transform, false);
        textObj.AddComponent<RectTransform>();
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "";
        tmp.fontSize = 14;
        tmp.color = name.Contains("System") ? new Color(0.6f, 0.6f, 0.7f) : Color.white;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        if (_font != null) tmp.font = _font;

        var path = $"{dir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>Office 프리팹에서 바닥(Floor) 메쉬의 Y 높이를 감지</summary>
    private static float DetectFloorHeight(GameObject officeRoot)
    {
        // Floor 오브젝트를 찾아서 상단 Y 좌표 반환
        foreach (Transform child in officeRoot.transform)
        {
            if (!child.name.Contains("Floor")) continue;
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                float topY = renderer.bounds.max.y;
                Debug.Log($"[AgentOfficeSceneBuilder] 바닥 감지: {child.name} → Y={topY:F2}");
                return topY;
            }
        }

        // Floor를 못 찾으면 Raycast로 탐색
        if (Physics.Raycast(new Vector3(4f, 10f, 2f), Vector3.down, out var hit, 20f))
        {
            Debug.Log($"[AgentOfficeSceneBuilder] Raycast 바닥 감지: Y={hit.point.y:F2}");
            return hit.point.y;
        }

        Debug.LogWarning("[AgentOfficeSceneBuilder] 바닥 감지 실패 — Y=0 사용");
        return 0f;
    }

    /// <summary>오브젝트와 자식 전체에 Navigation Static 설정</summary>
    private static void SetStaticRecursive(GameObject obj, bool isStatic)
    {
        GameObjectUtility.SetStaticEditorFlags(obj,
            StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
        foreach (Transform child in obj.transform)
            SetStaticRecursive(child.gameObject, isStatic);
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Replace("\\", "/").Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
