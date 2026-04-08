using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 에이전트 큐브 플레이스홀더 + World Space HUD 프리팹 자동 생성.
/// 메뉴: Tools → OpenDesk → Build Agent Prefabs
/// </summary>
public static class AgentPrefabBuilder
{
    private const string PrefabDir = "Assets/05.Prefabs/Agent";
    private const string FontAssetPath = "Assets/NotoSansKR-VariableFont_wght.asset";

    private static readonly Color32 ColHudBg     = new(0, 0, 0, 140);
    private static readonly Color32 ColBarBg     = new(40, 40, 50, 200);
    private static readonly Color32 ColBarFill   = new(80, 140, 255, 255);
    private static readonly Color32 ColWhite     = new(255, 255, 255, 255);
    private static readonly Color32 ColTextSub   = new(200, 200, 210, 255);

    [MenuItem("Tools/OpenDesk/Build Agent Prefabs", false, 120)]
    public static void BuildAll()
    {
        EnsureFolder(PrefabDir);

        var cubePrefab = BuildCubePlaceholder();
        var hudPrefab = BuildHUDPrefab();

        AssetDatabase.Refresh();

        Debug.Log($"[AgentPrefabBuilder] 프리팹 생성 완료\n- {PrefabDir}/AgentCube_Placeholder.prefab\n- {PrefabDir}/AgentHUD.prefab");
        EditorUtility.DisplayDialog("완료",
            "에이전트 프리팹 2종 생성 완료.\n\n" +
            "• AgentCube_Placeholder.prefab (큐브 모델 대체)\n" +
            "• AgentHUD.prefab (World Space HUD)\n\n" +
            $"위치: {PrefabDir}/",
            "OK");
    }

    // ================================================================
    //  큐브 플레이스홀더
    // ================================================================

    private static GameObject BuildCubePlaceholder()
    {
        // 루트: 빈 오브젝트 (피벗을 바닥으로)
        var root = new GameObject("AgentCube_Placeholder");

        // 큐브 메쉬 (Y 0.5 위치 → 바닥이 원점)
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Body";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = new Vector3(0, 0.5f, 0);
        cube.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

        // 머리 (작은 구)
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0, 1.3f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        // Collider 제거 (프리팹용)
        Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        Object.DestroyImmediate(head.GetComponent<SphereCollider>());

        // 루트에 BoxCollider (클릭 감지용)
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.65f, 0);
        col.size = new Vector3(0.7f, 1.4f, 0.7f);

        var path = $"{PrefabDir}/AgentCube_Placeholder.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        Debug.Log($"[AgentPrefabBuilder] 큐브 프리팹: {path}");
        return prefab;
    }

    // ================================================================
    //  World Space HUD
    // ================================================================

    private static GameObject BuildHUDPrefab()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        // 루트: World Space Canvas
        var root = new GameObject("AgentHUD");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRT = root.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(200, 70);
        canvasRT.localScale = Vector3.one * 0.01f; // World Space 스케일

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        root.AddComponent<GraphicRaycaster>();

        // 배경 패널
        var bg = CreateChild<Image>(root.transform, "Background");
        StretchFull(bg.rectTransform);
        bg.color = (Color)ColHudBg;

        // ── 이름 텍스트 ─────────────────────────────────────
        var nameObj = CreateChild<TextMeshProUGUI>(root.transform, "NameText");
        nameObj.rectTransform.anchorMin = new Vector2(0, 0.55f);
        nameObj.rectTransform.anchorMax = new Vector2(1, 1);
        nameObj.rectTransform.offsetMin = new Vector2(8, 0);
        nameObj.rectTransform.offsetMax = new Vector2(-8, -4);
        nameObj.text = "에이전트";
        nameObj.fontSize = 22;
        nameObj.color = (Color)ColWhite;
        nameObj.alignment = TextAlignmentOptions.Center;
        nameObj.fontStyle = FontStyles.Bold;
        if (font != null) nameObj.font = font;

        // ── 상태바 영역 ─────────────────────────────────────
        var barArea = new GameObject("StatusBarArea");
        barArea.transform.SetParent(root.transform, false);
        var barAreaRT = barArea.AddComponent<RectTransform>();
        barAreaRT.anchorMin = new Vector2(0.08f, 0.15f);
        barAreaRT.anchorMax = new Vector2(0.92f, 0.42f);
        barAreaRT.offsetMin = Vector2.zero;
        barAreaRT.offsetMax = Vector2.zero;

        // Slider 구성
        var sliderObj = new GameObject("StatusBar");
        sliderObj.transform.SetParent(barArea.transform, false);
        var sliderRT = sliderObj.AddComponent<RectTransform>();
        StretchFull(sliderRT);

        // Background
        var sliderBgObj = new GameObject("Background");
        sliderBgObj.transform.SetParent(sliderObj.transform, false);
        var sliderBgRT = sliderBgObj.AddComponent<RectTransform>();
        StretchFull(sliderBgRT);
        var sliderBgImg = sliderBgObj.AddComponent<Image>();
        sliderBgImg.color = (Color)ColBarBg;

        // Fill Area
        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        var fillAreaRT = fillArea.AddComponent<RectTransform>();
        StretchFull(fillAreaRT);

        // Fill
        var fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillArea.transform, false);
        var fillRT = fillObj.AddComponent<RectTransform>();
        StretchFull(fillRT);
        var fillImg = fillObj.AddComponent<Image>();
        fillImg.color = (Color)ColBarFill;

        var slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fillRT;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0;
        slider.interactable = false;

        // ── 상태 텍스트 ─────────────────────────────────────
        var statusObj = CreateChild<TextMeshProUGUI>(root.transform, "StatusText");
        statusObj.rectTransform.anchorMin = new Vector2(0, 0);
        statusObj.rectTransform.anchorMax = new Vector2(1, 0.2f);
        statusObj.rectTransform.offsetMin = new Vector2(8, 2);
        statusObj.rectTransform.offsetMax = new Vector2(-8, 0);
        statusObj.text = "대기 중";
        statusObj.fontSize = 14;
        statusObj.color = (Color)ColTextSub;
        statusObj.alignment = TextAlignmentOptions.Center;
        if (font != null) statusObj.font = font;

        // ── 버블 UI (상태 표시용, 타원형, 기본 숨김) ──────────
        var bubbleRoot = new GameObject("BubbleRoot");
        bubbleRoot.transform.SetParent(root.transform, false);
        var bubbleRootRT = bubbleRoot.AddComponent<RectTransform>();
        // HUD 영역보다 위에 위치 (이름+상태바와 겹치지 않게)
        bubbleRootRT.anchorMin = new Vector2(0.5f, 1f);
        bubbleRootRT.anchorMax = new Vector2(0.5f, 1f);
        bubbleRootRT.pivot = new Vector2(0.5f, 0f);
        bubbleRootRT.anchoredPosition = new Vector2(0, 5);
        bubbleRootRT.sizeDelta = new Vector2(240, 50);

        var bubbleBgImg = bubbleRoot.AddComponent<Image>();
        bubbleBgImg.color = new Color32(255, 235, 59, 210);
        // 타원형: 런타임에서 Ellipse 스프라이트 생성
        bubbleBgImg.sprite = CreateEllipseSprite(128, 64);
        bubbleBgImg.type = Image.Type.Sliced;
        bubbleBgImg.pixelsPerUnitMultiplier = 1f;

        var bubbleTextObj = CreateChild<TextMeshProUGUI>(bubbleRoot.transform, "BubbleText");
        StretchFull(bubbleTextObj.rectTransform);
        bubbleTextObj.rectTransform.offsetMin = new Vector2(16, 6);
        bubbleTextObj.rectTransform.offsetMax = new Vector2(-16, -6);
        bubbleTextObj.text = "";
        bubbleTextObj.fontSize = 16;
        bubbleTextObj.color = new Color32(30, 30, 30, 255);
        bubbleTextObj.alignment = TextAlignmentOptions.Center;
        bubbleTextObj.fontStyle = FontStyles.Bold;
        bubbleTextObj.enableWordWrapping = true;
        bubbleTextObj.overflowMode = TextOverflowModes.Ellipsis;
        if (font != null) bubbleTextObj.font = font;

        bubbleRoot.SetActive(false); // 초기 숨김

        // ── AgentHUDController 부착 ─────────────────────────
        var hud = root.AddComponent<OpenDesk.Presentation.Character.AgentHUDController>();

        // SerializedObject로 바인딩
        var so = new SerializedObject(hud);
        so.FindProperty("_nameText").objectReferenceValue = nameObj;
        so.FindProperty("_statusText").objectReferenceValue = statusObj;
        so.FindProperty("_statusBar").objectReferenceValue = slider;
        so.FindProperty("_statusBarFill").objectReferenceValue = fillImg;
        so.FindProperty("_bubbleRoot").objectReferenceValue = bubbleRoot;
        so.FindProperty("_bubbleText").objectReferenceValue = bubbleTextObj;
        so.FindProperty("_bubbleBg").objectReferenceValue = bubbleBgImg;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 저장
        var path = $"{PrefabDir}/AgentHUD.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        Debug.Log($"[AgentPrefabBuilder] HUD 프리팹: {path}");
        return prefab;
    }

    // ================================================================
    //  유틸
    // ================================================================

    private static T CreateChild<T>(Transform parent, string name) where T : Component
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj.AddComponent<T>();
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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

    /// <summary>타원형 스프라이트를 프로그래매틱으로 생성</summary>
    private static Sprite CreateEllipseSprite(int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var cx = width / 2f;
        var cy = height / 2f;
        var rx = cx - 1;
        var ry = cy - 1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var dist = dx * dx + dy * dy;
                // 안티앨리어싱: 경계 부근 알파 부드럽게
                var alpha = Mathf.Clamp01(1f - (dist - 0.9f) / 0.1f);
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }

        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;

        // 4방향 border로 Sliced 모드 지원
        var border = new Vector4(width * 0.3f, height * 0.3f, width * 0.3f, height * 0.3f);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100, 0,
            SpriteMeshType.FullRect, border);
    }
}
