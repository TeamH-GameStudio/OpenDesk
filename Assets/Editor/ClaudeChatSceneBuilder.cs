using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TestChattingScene 씬 + 버블 프리팹을 자동 생성하는 에디터 유틸리티.
/// 메뉴: Tools → OpenDesk → Build Claude Chat Scene
/// </summary>
public static class ClaudeChatSceneBuilder
{
    private static readonly Color32 ColorDarkBg     = new(30, 30, 30, 255);     // #1E1E1E
    private static readonly Color32 ColorHeaderBg   = new(45, 45, 45, 255);     // #2D2D2D
    private static readonly Color32 ColorInputBg    = new(51, 51, 51, 255);     // #333333
    private static readonly Color32 ColorInputField = new(66, 66, 66, 255);     // #424242
    private static readonly Color32 ColorUserBubble = new(33, 150, 243, 255);   // #2196F3
    private static readonly Color32 ColorAIBubble   = new(66, 66, 66, 255);     // #424242
    private static readonly Color32 ColorSystemText = new(255, 213, 79, 255);   // #FFD54F
    private static readonly Color32 ColorGreen      = new(76, 175, 80, 255);    // #4CAF50
    private static readonly Color32 ColorSendBtn    = new(33, 150, 243, 255);   // #2196F3
    private static readonly Color32 ColorClearBtn   = new(97, 97, 97, 255);     // #616161
    private static readonly Color32 ColorWhite      = new(255, 255, 255, 255);
    private static readonly Color32 ColorLightGray  = new(224, 224, 224, 255);  // #E0E0E0
    private static readonly Color32 ColorGray       = new(170, 170, 170, 255);  // #AAAAAA
    private static readonly Color32 ColorDimGray    = new(136, 136, 136, 255);  // #888888
    private static readonly Color32 ColorDarkGray   = new(153, 153, 153, 255);  // #999999

    private const string FontAssetPath = "Assets/NotoSansKR-VariableFont_wght.asset";

    private static TMP_FontAsset LoadFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
            Debug.LogWarning($"[ClaudeChatSceneBuilder] NotoSansKR 폰트를 찾을 수 없습니다: {FontAssetPath}");
        return font;
    }

    // ── 메인 메뉴 ─────────────────────────────────────────────

    [MenuItem("Tools/OpenDesk/Build Claude Chat Scene", false, 100)]
    public static void BuildScene()
    {
        // 0) 폰트 로드
        _sharedFont = LoadFont();

        // 1) 프리팹 생성
        var userPrefab   = BuildUserBubblePrefab();
        var aiBubble     = BuildAIBubblePrefab();
        var systemBubble = BuildSystemBubblePrefab();

        // 2) 새 씬 생성
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = "TestChattingScene";

        // 카메라 배경 어둡게
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = (Color)ColorDarkBg;
        }

        // 3) Canvas
        var canvasObj = CreateUIObject("Canvas", null);
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasObj.GetComponent<RectTransform>();

        // 4) Header
        var header = CreatePanel("Header", canvasRT, ColorHeaderBg);
        SetAnchors(header, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -50), Vector2.zero);
        header.sizeDelta = new Vector2(0, 50);

        var statusDot = CreateImage("StatusDot", header, ColorGreen);
        SetAnchors(statusDot, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), Vector2.zero);
        statusDot.sizeDelta = new Vector2(12, 12);

        var statusText = CreateTMP("StatusText", header, "연결됨", 14, (Color)ColorGray);
        SetAnchors(statusText, new Vector2(0, 0), new Vector2(0.3f, 1), Vector2.zero, Vector2.zero);
        statusText.offsetMin = new Vector2(40, 0);
        var statusTMP = statusText.GetComponent<TMP_Text>();
        statusTMP.alignment = TextAlignmentOptions.MidlineLeft;

        var titleText = CreateTMP("TitleText", header, "Claude Chat", 20, (Color)ColorWhite);
        SetAnchors(titleText, new Vector2(0.3f, 0), new Vector2(0.7f, 1), Vector2.zero, Vector2.zero);
        var titleTMP = titleText.GetComponent<TMP_Text>();
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.fontStyle = FontStyles.Bold;

        var modelText = CreateTMP("ModelText", header, "", 12, (Color)ColorDimGray);
        SetAnchors(modelText, new Vector2(0.7f, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        modelText.offsetMax = new Vector2(-15, 0);
        var modelTMP = modelText.GetComponent<TMP_Text>();
        modelTMP.alignment = TextAlignmentOptions.MidlineRight;

        // 5) InputArea (하단)
        var inputArea = CreatePanel("InputArea", canvasRT, ColorInputBg);
        SetAnchors(inputArea, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 60));
        inputArea.sizeDelta = new Vector2(0, 60);

        var inputFieldObj = new GameObject("InputField");
        inputFieldObj.transform.SetParent(inputArea, false);
        var inputFieldRT = inputFieldObj.AddComponent<RectTransform>();
        SetAnchors(inputFieldRT, new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        inputFieldRT.offsetMin = new Vector2(15, 8);
        inputFieldRT.offsetMax = new Vector2(-140, -8);

        var inputFieldImg = inputFieldObj.AddComponent<Image>();
        inputFieldImg.color = (Color)ColorInputField;

        // TextArea
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputFieldObj.transform, false);
        var textAreaRT = textArea.AddComponent<RectTransform>();
        SetAnchors(textAreaRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        textAreaRT.offsetMin = new Vector2(10, 5);
        textAreaRT.offsetMax = new Vector2(-10, -5);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        var placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        var placeholderRT = placeholderObj.AddComponent<RectTransform>();
        SetAnchors(placeholderRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholder.text = "메시지를 입력하세요... (Enter=전송)";
        placeholder.fontSize = 15;
        placeholder.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        placeholder.enableWordWrapping = true;
        if (_sharedFont != null) placeholder.font = _sharedFont;

        // Text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        SetAnchors(textRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var inputTMP = textObj.AddComponent<TextMeshProUGUI>();
        inputTMP.fontSize = 15;
        inputTMP.color = (Color)ColorWhite;
        inputTMP.enableWordWrapping = true;
        if (_sharedFont != null) inputTMP.font = _sharedFont;

        var tmpInput = inputFieldObj.AddComponent<TMP_InputField>();
        tmpInput.textViewport = textAreaRT;
        tmpInput.textComponent = inputTMP;
        tmpInput.placeholder = placeholder;
        tmpInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        tmpInput.richText = false;

        // Send Button
        var sendBtn = CreateButton("SendButton", inputArea, "전송", ColorSendBtn);
        var sendRT = sendBtn.GetComponent<RectTransform>();
        SetAnchors(sendRT, new Vector2(1, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        sendRT.offsetMin = new Vector2(-130, 8);
        sendRT.offsetMax = new Vector2(-72, -8);

        // Clear Button
        var clearBtn = CreateButton("ClearButton", inputArea, "초기화", ColorClearBtn);
        var clearRT = clearBtn.GetComponent<RectTransform>();
        SetAnchors(clearRT, new Vector2(1, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        clearRT.offsetMin = new Vector2(-65, 8);
        clearRT.offsetMax = new Vector2(-8, -8);

        // 6) TypingIndicator
        var typingObj = CreateTMP("TypingIndicator", canvasRT, "Claude가 응답 중...", 13, (Color)ColorDarkGray);
        SetAnchors(typingObj, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero);
        typingObj.offsetMin = new Vector2(20, 62);
        typingObj.offsetMax = new Vector2(0, 87);
        var typingTMP = typingObj.GetComponent<TMP_Text>();
        typingTMP.fontStyle = FontStyles.Italic;
        typingTMP.alignment = TextAlignmentOptions.MidlineLeft;
        typingObj.gameObject.SetActive(false);

        // 7) ChatArea (ScrollRect)
        var chatAreaObj = new GameObject("ChatArea");
        chatAreaObj.transform.SetParent(canvasRT, false);
        var chatAreaRT = chatAreaObj.AddComponent<RectTransform>();
        SetAnchors(chatAreaRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        chatAreaRT.offsetMin = new Vector2(0, 90);   // 하단: InputArea(60) + Typing(30)
        chatAreaRT.offsetMax = new Vector2(0, -50);   // 상단: Header(50)

        var chatAreaImg = chatAreaObj.AddComponent<Image>();
        chatAreaImg.color = new Color(0, 0, 0, 0); // 투명

        var scrollRect = chatAreaObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;

        // Viewport
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(chatAreaObj.transform, false);
        var viewportRT = viewport.AddComponent<RectTransform>();
        SetAnchors(viewportRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.AddComponent<RectMask2D>();

        // Content
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRT = content.AddComponent<RectTransform>();
        SetAnchors(contentRT, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        contentRT.pivot = new Vector2(0.5f, 1);

        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(15, 15, 10, 10);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;

        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewportRT;
        scrollRect.content = contentRT;

        // 8) Managers
        var wsClientObj = new GameObject("ClaudeWebSocketClient");
        wsClientObj.transform.SetParent(null);
        var wsClient = wsClientObj.AddComponent<OpenDesk.Claude.ClaudeWebSocketClient>();

        var chatMgrObj = new GameObject("ClaudeChatManager");
        chatMgrObj.transform.SetParent(null);
        var chatMgr = chatMgrObj.AddComponent<OpenDesk.Claude.ClaudeChatManager>();

        var launcherObj = new GameObject("MiddlewareLauncher");
        launcherObj.transform.SetParent(null);
        launcherObj.AddComponent<OpenDesk.Claude.MiddlewareLauncher>();

        // 9) Inspector 바인딩 (SerializedObject)
        var so = new SerializedObject(chatMgr);
        so.FindProperty("_client").objectReferenceValue      = wsClient;
        so.FindProperty("_scrollRect").objectReferenceValue   = scrollRect;
        so.FindProperty("_chatContent").objectReferenceValue  = contentRT;
        so.FindProperty("_inputField").objectReferenceValue   = tmpInput;
        so.FindProperty("_sendButton").objectReferenceValue   = sendBtn.GetComponent<Button>();
        so.FindProperty("_clearButton").objectReferenceValue  = clearBtn.GetComponent<Button>();
        so.FindProperty("_userBubblePrefab").objectReferenceValue   = userPrefab;
        so.FindProperty("_aiBubblePrefab").objectReferenceValue     = aiBubble;
        so.FindProperty("_systemBubblePrefab").objectReferenceValue = systemBubble;
        so.FindProperty("_typingIndicator").objectReferenceValue    = typingObj.gameObject;
        so.FindProperty("_statusDot").objectReferenceValue   = statusDot.GetComponent<Image>();
        so.FindProperty("_statusText").objectReferenceValue  = statusTMP;
        so.FindProperty("_modelText").objectReferenceValue   = modelTMP;
        so.ApplyModifiedPropertiesWithoutUndo();

        // EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // 10) 씬 저장
        var scenePath = "Assets/01.Scenes/TestChattingScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[ClaudeChatSceneBuilder] 씬 생성 완료: {scenePath}");
        Debug.Log("[ClaudeChatSceneBuilder] 프리팹 3종 생성 완료: Assets/05.Prefabs/UI/ClaudeChat/");
        EditorUtility.DisplayDialog("완료", "TestChattingScene 씬과 프리팹이 생성되었습니다.\n\n사용법:\n1. 터미널에서 python Middleware/server.py 실행\n2. Unity Play", "OK");
    }

    // ── 프리팹 생성 ───────────────────────────────────────────

    /// <summary>
    /// 단순 구조: Root(Background + padding) → MessageText
    /// ContentSizeFitter로 텍스트 크기에 맞게 높이 자동 조절
    /// </summary>
    private static GameObject BuildUserBubblePrefab()
    {
        var root = new GameObject("Bubble_User");
        root.AddComponent<RectTransform>();

        // 배경 이미지
        var bg = root.AddComponent<Image>();
        bg.color = (Color)ColorUserBubble;

        // 내부 여백
        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // 높이를 텍스트에 맞춤
        var csf = root.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // LayoutElement: 부모 VLG(Content)에서 폭 제어용
        var le = root.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;

        // MessageText
        var textObj = new GameObject("MessageText");
        textObj.transform.SetParent(root.transform, false);
        textObj.AddComponent<RectTransform>();
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 15;
        tmp.color = (Color)ColorWhite;
        tmp.alignment = TextAlignmentOptions.TopRight;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.text = "";
        if (_sharedFont != null) tmp.font = _sharedFont;

        return SavePrefab(root, "Assets/05.Prefabs/UI/ClaudeChat/Bubble_User.prefab");
    }

    private static GameObject BuildAIBubblePrefab()
    {
        var root = new GameObject("Bubble_AI");
        root.AddComponent<RectTransform>();

        var bg = root.AddComponent<Image>();
        bg.color = (Color)ColorAIBubble;

        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var csf = root.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var le = root.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;

        var textObj = new GameObject("MessageText");
        textObj.transform.SetParent(root.transform, false);
        textObj.AddComponent<RectTransform>();
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 15;
        tmp.color = (Color)ColorLightGray;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.text = "";
        if (_sharedFont != null) tmp.font = _sharedFont;

        return SavePrefab(root, "Assets/05.Prefabs/UI/ClaudeChat/Bubble_AI.prefab");
    }

    private static GameObject BuildSystemBubblePrefab()
    {
        var root = new GameObject("Bubble_System");
        root.AddComponent<RectTransform>();

        // 배경 없음 (투명)

        var csf = root.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var le = root.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.minHeight = 25;

        var textObj = new GameObject("MessageText");
        textObj.transform.SetParent(root.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        SetAnchors(textRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 12;
        tmp.color = (Color)ColorSystemText;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Italic;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.text = "";
        if (_sharedFont != null) tmp.font = _sharedFont;

        return SavePrefab(root, "Assets/05.Prefabs/UI/ClaudeChat/Bubble_System.prefab");
    }

    private static GameObject SavePrefab(GameObject obj, string path)
    {
        // 폴더 확인
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            var parts = dir.Replace("\\", "/").Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(obj, path);
        Object.DestroyImmediate(obj);
        Debug.Log($"[ClaudeChatSceneBuilder] 프리팹 저장: {path}");
        return prefab;
    }

    // ── UI 헬퍼 ───────────────────────────────────────────────

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        var obj = new GameObject(name);
        if (parent != null) obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();
        return obj;
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Color32 color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        var img = obj.AddComponent<Image>();
        img.color = (Color)color;
        return rt;
    }

    private static RectTransform CreateImage(string name, RectTransform parent, Color32 color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        var img = obj.AddComponent<Image>();
        img.color = (Color)color;
        return rt;
    }

    private static TMP_FontAsset _sharedFont;

    private static RectTransform CreateTMP(string name, RectTransform parent, string text, int fontSize, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        if (_sharedFont != null) tmp.font = _sharedFont;
        return rt;
    }

    private static GameObject CreateButton(string name, RectTransform parent, string label, Color32 bgColor)
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
        SetAnchors(textRT, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.color = (Color)ColorWhite;
        if (_sharedFont != null) tmp.font = _sharedFont;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;

        return obj;
    }

    private static void SetAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
