using OpenDesk.AgentCreation.Models;
using OpenDesk.Presentation.UI.AgentCreation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AgentCreationScene 자동 생성 에디터 유틸리티.
/// 메뉴: Tools → OpenDesk → Build Agent Creation Scene
///
/// 1280x720 (LOL 클라이언트 비율)
/// 좌측: 캐릭터 프리뷰 (항상 표시)
/// 우측: Step별 패널 (이전/다음 공통 버튼)
/// </summary>
public static class AgentCreationSceneBuilder
{
    // ── 컬러 팔레트 ──────────────────────────────────────────
    private static readonly Color32 ColBg          = new(25, 25, 35, 255);      // 전체 배경 (다크 네이비)
    private static readonly Color32 ColPanel       = new(35, 35, 50, 255);      // 패널 배경
    private static readonly Color32 ColLeftPanel   = new(30, 30, 45, 255);      // 좌측 캐릭터 패널
    private static readonly Color32 ColCard        = new(50, 50, 70, 255);      // 카드/칩 배경
    private static readonly Color32 ColCardHover   = new(60, 60, 85, 255);      // 카드 호버
    private static readonly Color32 ColAccent      = new(80, 140, 255, 255);    // 강조색 (파란)
    private static readonly Color32 ColAccentDim   = new(60, 110, 220, 255);    // 강조색 눌림
    private static readonly Color32 ColProgress    = new(80, 140, 255, 255);    // 프로그레스바
    private static readonly Color32 ColProgressBg  = new(50, 50, 65, 255);      // 프로그레스바 배경
    private static readonly Color32 ColNavBg       = new(30, 30, 42, 255);      // 하단 네비 배경
    private static readonly Color32 ColBtnPrev     = new(70, 70, 90, 255);      // 이전 버튼
    private static readonly Color32 ColBtnNext     = new(80, 140, 255, 255);    // 다음 버튼
    private static readonly Color32 ColBtnDisabled = new(55, 55, 70, 255);      // 비활성 버튼
    private static readonly Color32 ColBtnCreate   = new(76, 175, 80, 255);     // 생성 버튼 (녹색)
    private static readonly Color32 ColSelected    = new(80, 140, 255, 180);    // 선택 인디케이터
    private static readonly Color32 ColRecommend   = new(255, 193, 7, 255);     // 추천 태그 (노란)
    private static readonly Color32 ColOverlay     = new(0, 0, 0, 180);         // 생성중 오버레이
    private static readonly Color32 ColWhite       = new(255, 255, 255, 255);
    private static readonly Color32 ColTextMain    = new(230, 230, 240, 255);   // 주 텍스트
    private static readonly Color32 ColTextSub     = new(160, 160, 180, 255);   // 부 텍스트
    private static readonly Color32 ColTextDim     = new(120, 120, 140, 255);   // 희미한 텍스트
    private static readonly Color32 ColInputBg     = new(45, 45, 62, 255);      // 입력필드 배경
    private static readonly Color32 ColError       = new(255, 100, 100, 255);   // 에러/경고 텍스트
    private static readonly Color32 ColCharBg      = new(40, 40, 58, 255);      // 캐릭터 프리뷰 원형 배경

    private const string FontAssetPath = "Assets/NotoSansKR-VariableFont_wght.asset";
    private static TMP_FontAsset _font;

    // ── 역할/모델/말투 데이터 ────────────────────────────────
    private static readonly (AgentRole role, string label)[] Roles =
    {
        (AgentRole.Planning,    "기획"),
        (AgentRole.Development, "개발"),
        (AgentRole.Design,      "디자인"),
        (AgentRole.Legal,       "법률"),
        (AgentRole.Marketing,   "마케팅"),
        (AgentRole.Research,    "리서치"),
        (AgentRole.Support,     "고객지원"),
        (AgentRole.Finance,     "재무"),
    };

    private static readonly (AgentAIModel model, string name, string desc, bool recommended)[] Models =
    {
        (AgentAIModel.GPT4o,       "GPT-4o",            "가장 똑똑하고\n다재다능해요",         true),
        (AgentAIModel.ClaudeSonnet, "Claude 3.5 Sonnet", "코딩과 글쓰기에\n탁월해요",          false),
        (AgentAIModel.GeminiPro,    "Gemini 1.5 Pro",   "방대한 데이터를\n빠르게 처리해요",    false),
    };

    private static readonly (AgentTone tone, string label)[] Tones =
    {
        (AgentTone.Friendly, "친절한"),
        (AgentTone.Logical,  "논리적인"),
        (AgentTone.Humorous, "유머러스한"),
        (AgentTone.Formal,   "격식체"),
        (AgentTone.Casual,   "편안한"),
    };

    // ================================================================
    //  메인
    // ================================================================

    [MenuItem("Tools/OpenDesk/Build Agent Creation Scene", false, 110)]
    public static void BuildScene()
    {
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (_font == null)
            Debug.LogWarning($"[AgentCreationSceneBuilder] 폰트 없음: {FontAssetPath}");

        // ── 새 씬 ────────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = "AgentCreationScene";

        // 카메라
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = (Color)ColBg;
        }

        // ── Canvas (1280x720) ────────────────────────────────
        var canvasObj = CreateUIRoot("Canvas", null);
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();

        var canvasRT = canvasObj.GetComponent<RectTransform>();

        // ── WizardRoot (전체 채우기) ─────────────────────────
        var wizardRoot = CreatePanel("WizardRoot", canvasRT, ColBg);
        Stretch(wizardRoot);

        // ================================================================
        //  상단: Progress Area (높이 60)
        // ================================================================
        var progressArea = CreatePanel("ProgressArea", wizardRoot, new Color32(0, 0, 0, 0));
        AnchorTop(progressArea, 60);

        // Step Count Text
        var stepCountRT = CreateTMP("StepCountText", progressArea, "Step 1 / 5", 14, ColTextSub);
        SetRect(stepCountRT, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, -10), new Vector2(180, 10));
        stepCountRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // Progress Bar Background
        var progressBgRT = CreatePanel("ProgressBarBg", progressArea, ColProgressBg);
        SetRect(progressBgRT, new Vector2(0.15f, 0.35f), new Vector2(0.85f, 0.65f), Vector2.zero, Vector2.zero);

        // Progress Bar (Slider)
        var sliderObj = new GameObject("ProgressBar");
        sliderObj.transform.SetParent(progressArea, false);
        var sliderRT = sliderObj.AddComponent<RectTransform>();
        SetRect(sliderRT, new Vector2(0.15f, 0.35f), new Vector2(0.85f, 0.65f), Vector2.zero, Vector2.zero);

        // Slider - Background
        var sliderBg = CreatePanel("Background", sliderRT, ColProgressBg);
        Stretch(sliderBg);

        // Slider - Fill Area
        var fillAreaObj = CreateUIRoot("Fill Area", sliderObj.transform);
        var fillAreaRT = fillAreaObj.GetComponent<RectTransform>();
        Stretch(fillAreaRT);

        var fill = CreatePanel("Fill", fillAreaRT, ColProgress);
        Stretch(fill);

        var slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0.2f;
        slider.interactable = false;
        // Slider handle 없음 (표시 전용)

        // ================================================================
        //  중앙: Content Area
        // ================================================================
        var contentArea = CreatePanel("ContentArea", wizardRoot, new Color32(0, 0, 0, 0));
        SetRect(contentArea, Vector2.zero, Vector2.one,
            new Vector2(0, 60),   // 하단: Nav 영역
            new Vector2(0, -60)); // 상단: Progress 영역

        // ── 좌측 패널 (캐릭터 프리뷰) ── 35% ─────────────────
        var leftPanel = CreatePanel("LeftPanel_Character", contentArea, ColLeftPanel);
        SetRect(leftPanel, Vector2.zero, new Vector2(0.35f, 1), Vector2.zero, Vector2.zero);

        // 캐릭터 프리뷰 원형 배경
        var charCircle = CreatePanel("CharacterCircle", leftPanel, ColCharBg);
        SetRect(charCircle, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
            new Vector2(-90, -90), new Vector2(90, 90));

        // 캐릭터 프리뷰 이미지 (플레이스홀더)
        var charPreviewRT = CreatePanel("CharacterPreview", charCircle, new Color32(100, 100, 130, 255));
        SetRect(charPreviewRT, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f), Vector2.zero, Vector2.zero);
        var charPreviewImage = charPreviewRT.GetComponent<Image>();

        // 캐릭터 이름 라벨
        var charNameRT = CreateTMP("CharacterNameLabel", leftPanel, "???", 22, ColWhite);
        SetRect(charNameRT, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0.25f),
            new Vector2(-100, -15), new Vector2(100, 15));
        var charNameTMP = charNameRT.GetComponent<TMP_Text>();
        charNameTMP.alignment = TextAlignmentOptions.Center;
        charNameTMP.fontStyle = FontStyles.Bold;

        // "에이전트 미리보기" 안내 텍스트
        var previewGuide = CreateTMP("PreviewGuide", leftPanel, "에이전트 미리보기", 13, ColTextDim);
        SetRect(previewGuide, new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.18f),
            new Vector2(-80, -10), new Vector2(80, 10));
        previewGuide.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        // ── 우측 패널 (Step 컨텐츠) ── 65% ────────────────────
        var rightPanel = CreatePanel("RightPanel_Steps", contentArea, ColPanel);
        SetRect(rightPanel, new Vector2(0.35f, 0), Vector2.one, new Vector2(2, 0), Vector2.zero);

        // Step Title
        var stepTitleRT = CreateTMP("StepTitle", rightPanel, "이름 정하기", 26, ColWhite);
        SetRect(stepTitleRT, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -65), new Vector2(-30, -25));
        var stepTitleTMP = stepTitleRT.GetComponent<TMP_Text>();
        stepTitleTMP.alignment = TextAlignmentOptions.MidlineLeft;
        stepTitleTMP.fontStyle = FontStyles.Bold;

        // Step Description
        var stepDescRT = CreateTMP("StepDesc", rightPanel, "함께 일할 에이전트의 이름을 정해주세요.", 15, ColTextSub);
        SetRect(stepDescRT, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -95), new Vector2(-30, -65));
        stepDescRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // ================================================================
        //  Step 1: 이름 입력 패널
        // ================================================================
        var stepNamePanel = CreateStepPanel("StepNamePanel", rightPanel);

        // 입력 필드 배경
        var nameInputBg = CreatePanel("NameInputBg", stepNamePanel, ColInputBg);
        SetRect(nameInputBg, new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.7f), Vector2.zero, Vector2.zero);

        // TMP_InputField 구성
        var nameInputObj = new GameObject("NameInput");
        nameInputObj.transform.SetParent(nameInputBg, false);
        var nameInputRT = nameInputObj.AddComponent<RectTransform>();
        Stretch(nameInputRT);
        nameInputRT.offsetMin = new Vector2(15, 5);
        nameInputRT.offsetMax = new Vector2(-15, -5);

        var nameTextArea = CreateUIRoot("Text Area", nameInputObj.transform);
        var nameTextAreaRT = nameTextArea.GetComponent<RectTransform>();
        Stretch(nameTextAreaRT);
        nameTextArea.AddComponent<RectMask2D>();

        var namePlaceholder = CreateTextChild("Placeholder", nameTextArea.transform,
            "에이전트 이름을 입력하세요...", 18, new Color(0.5f, 0.5f, 0.6f, 0.7f));
        var nameText = CreateTextChild("Text", nameTextArea.transform, "", 18, (Color)ColWhite);

        var nameInputField = nameInputObj.AddComponent<TMP_InputField>();
        nameInputField.textViewport = nameTextAreaRT;
        nameInputField.textComponent = nameText;
        nameInputField.placeholder = namePlaceholder;
        nameInputField.characterLimit = 20;
        nameInputField.contentType = TMP_InputField.ContentType.Standard;

        // 유효성 텍스트
        var nameValidRT = CreateTMP("NameValidation", stepNamePanel, "", 13, ColError);
        SetRect(nameValidRT, new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.55f), Vector2.zero, Vector2.zero);
        nameValidRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // 안내 텍스트
        var nameHint = CreateTMP("NameHint", stepNamePanel, "2~20자, 한글/영문/숫자 사용 가능", 12, ColTextDim);
        SetRect(nameHint, new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.45f), Vector2.zero, Vector2.zero);
        nameHint.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // ================================================================
        //  Step 2: 역할 선택 패널 (칩 그리드)
        // ================================================================
        var stepRolePanel = CreateStepPanel("StepRolePanel", rightPanel);

        var roleGrid = CreateUIRoot("RoleChipGrid", stepRolePanel.transform);
        var roleGridRT = roleGrid.GetComponent<RectTransform>();
        SetRect(roleGridRT, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);

        var roleGridLayout = roleGrid.AddComponent<GridLayoutGroup>();
        roleGridLayout.cellSize = new Vector2(170, 55);
        roleGridLayout.spacing = new Vector2(15, 12);
        roleGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        roleGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        roleGridLayout.constraintCount = 4;
        roleGridLayout.childAlignment = TextAnchor.MiddleCenter;
        roleGridLayout.padding = new RectOffset(10, 10, 10, 10);

        // 칩 8개 생성
        var roleChipObjects = new (GameObject chipObj, Button btn, GameObject indicator)[Roles.Length];
        for (int i = 0; i < Roles.Length; i++)
        {
            var (chipObj, btn, indicator) = CreateChip($"Chip_{Roles[i].label}", roleGrid.transform, Roles[i].label);
            roleChipObjects[i] = (chipObj, btn, indicator);
        }

        // ================================================================
        //  Step 3: AI 모델 선택 패널 (카드)
        // ================================================================
        var stepModelPanel = CreateStepPanel("StepModelPanel", rightPanel);

        var cardRow = CreateUIRoot("ModelCardRow", stepModelPanel.transform);
        var cardRowRT = cardRow.GetComponent<RectTransform>();
        SetRect(cardRowRT, new Vector2(0.03f, 0.08f), new Vector2(0.97f, 0.9f), Vector2.zero, Vector2.zero);

        var cardRowLayout = cardRow.AddComponent<HorizontalLayoutGroup>();
        cardRowLayout.spacing = 15;
        cardRowLayout.childForceExpandWidth = true;
        cardRowLayout.childForceExpandHeight = true;
        cardRowLayout.childControlWidth = true;
        cardRowLayout.childControlHeight = true;
        cardRowLayout.padding = new RectOffset(5, 5, 5, 5);

        var modelCardObjects = new (GameObject cardObj, Button btn, GameObject indicator, TMP_Text nameTMP, TMP_Text descTMP, GameObject recommendTag)[Models.Length];
        for (int i = 0; i < Models.Length; i++)
        {
            var m = Models[i];
            var (cardObj, btn, indicator, nameTMP, descTMP, recommendTag) =
                CreateModelCard($"Card_{m.name.Replace(" ", "")}", cardRow.transform, m.name, m.desc, m.recommended);
            modelCardObjects[i] = (cardObj, btn, indicator, nameTMP, descTMP, recommendTag);
        }

        // ================================================================
        //  Step 4: 말투 선택 패널
        // ================================================================
        var stepTonePanel = CreateStepPanel("StepTonePanel", rightPanel);

        var toneGrid = CreateUIRoot("ToneChipGrid", stepTonePanel.transform);
        var toneGridRT = toneGrid.GetComponent<RectTransform>();
        SetRect(toneGridRT, new Vector2(0.05f, 0.2f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);

        var toneGridLayout = toneGrid.AddComponent<GridLayoutGroup>();
        toneGridLayout.cellSize = new Vector2(170, 55);
        toneGridLayout.spacing = new Vector2(15, 12);
        toneGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        toneGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        toneGridLayout.constraintCount = 3;
        toneGridLayout.childAlignment = TextAnchor.MiddleCenter;
        toneGridLayout.padding = new RectOffset(10, 10, 10, 10);

        var toneChipObjects = new (GameObject chipObj, Button btn, GameObject indicator)[Tones.Length];
        for (int i = 0; i < Tones.Length; i++)
        {
            var (chipObj, btn, indicator) = CreateChip($"Chip_{Tones[i].label}", toneGrid.transform, Tones[i].label);
            toneChipObjects[i] = (chipObj, btn, indicator);
        }

        // ================================================================
        //  Step 5: 아바타(3D 모델) 선택 패널
        // ================================================================
        var stepAvatarPanel = CreateStepPanel("StepAvatarPanel", rightPanel);

        var avatarGrid = CreateUIRoot("AvatarCardGrid", stepAvatarPanel.transform);
        var avatarGridRT = avatarGrid.GetComponent<RectTransform>();
        SetRect(avatarGridRT, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.85f), Vector2.zero, Vector2.zero);

        var avatarGridLayout = avatarGrid.AddComponent<GridLayoutGroup>();
        avatarGridLayout.cellSize = new Vector2(170, 180);
        avatarGridLayout.spacing = new Vector2(15, 12);
        avatarGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        avatarGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        avatarGridLayout.constraintCount = 3;
        avatarGridLayout.childAlignment = TextAnchor.MiddleCenter;
        avatarGridLayout.padding = new RectOffset(10, 10, 10, 10);

        // 아바타 카드 (현재 큐브 1종만)
        var avatarDefs = new (string prefabName, string displayName)[]
        {
            ("AgentCube_Placeholder", "기본 큐브"),
        };

        var avatarCardObjects = new (GameObject obj, Button btn, GameObject indicator, Image preview)[avatarDefs.Length];
        for (int i = 0; i < avatarDefs.Length; i++)
        {
            var (obj, btn, indicator, preview) =
                CreateAvatarCard($"Avatar_{avatarDefs[i].prefabName}", avatarGrid.transform, avatarDefs[i].displayName);
            avatarCardObjects[i] = (obj, btn, indicator, preview);
        }

        // ================================================================
        //  Step 6: 최종 확인 패널
        // ================================================================
        var stepConfirmPanel = CreateStepPanel("StepConfirmPanel", rightPanel);

        // 확인 정보 카드
        var confirmCard = CreatePanel("ConfirmCard", stepConfirmPanel, ColCard);
        SetRect(confirmCard, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.9f), Vector2.zero, Vector2.zero);

        // 5줄 요약 (라벨 + 값)
        var confirmNameTMP   = CreateConfirmRow("이름",    0.82f, confirmCard);
        var confirmRoleTMP   = CreateConfirmRow("역할",    0.65f, confirmCard);
        var confirmModelTMP  = CreateConfirmRow("AI 모델", 0.48f, confirmCard);
        var confirmToneTMP   = CreateConfirmRow("말투",    0.31f, confirmCard);
        var confirmAvatarTMP = CreateConfirmRow("외형",    0.14f, confirmCard);

        // 생성 버튼
        var createBtnObj = CreateStyledButton("CreateButton", stepConfirmPanel, "에이전트 생성하기", ColBtnCreate, 18);
        var createBtnRT = createBtnObj.GetComponent<RectTransform>();
        SetRect(createBtnRT, new Vector2(0.2f, 0.05f), new Vector2(0.8f, 0.2f), Vector2.zero, Vector2.zero);
        var createBtn = createBtnObj.GetComponent<Button>();

        // 생성 중 오버레이
        var creatingOverlay = CreatePanel("CreatingOverlay", stepConfirmPanel, ColOverlay);
        Stretch(creatingOverlay);
        creatingOverlay.gameObject.SetActive(false);

        var creatingStatusRT = CreateTMP("CreatingStatus", creatingOverlay, "설정하신 두뇌와 외형을 연결하고 있어요...", 18, ColWhite);
        SetRect(creatingStatusRT, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f), Vector2.zero, Vector2.zero);
        var creatingStatusTMP = creatingStatusRT.GetComponent<TMP_Text>();
        creatingStatusTMP.alignment = TextAlignmentOptions.Center;

        // ================================================================
        //  하단: Navigation Area (높이 60)
        // ================================================================
        var navArea = CreatePanel("NavigationArea", wizardRoot, ColNavBg);
        AnchorBottom(navArea, 60);

        // 이전 버튼
        var prevBtnObj = CreateStyledButton("PrevButton", navArea, "◀ 이전", ColBtnPrev, 16);
        var prevBtnRT = prevBtnObj.GetComponent<RectTransform>();
        SetRect(prevBtnRT, new Vector2(0.35f, 0.15f), new Vector2(0.48f, 0.85f), Vector2.zero, Vector2.zero);
        var prevBtn = prevBtnObj.GetComponent<Button>();

        // 다음 버튼
        var nextBtnObj = CreateStyledButton("NextButton", navArea, "다음 ▶", ColBtnNext, 16);
        var nextBtnRT = nextBtnObj.GetComponent<RectTransform>();
        SetRect(nextBtnRT, new Vector2(0.52f, 0.15f), new Vector2(0.65f, 0.85f), Vector2.zero, Vector2.zero);
        var nextBtn = nextBtnObj.GetComponent<Button>();

        // 다음 버튼 텍스트 참조
        var nextBtnText = nextBtnObj.GetComponentInChildren<TMP_Text>();

        // ================================================================
        //  VContainer Installer
        // ================================================================
        var installerObj = new GameObject("[VContainer] AgentCreationInstaller");
        installerObj.AddComponent<OpenDesk.AgentCreation.Installers.AgentCreationInstaller>();

        // ================================================================
        //  Controller 부착 + 바인딩
        // ================================================================
        var controllerObj = new GameObject("AgentCreationWizardController");
        var controller = controllerObj.AddComponent<AgentCreationWizardController>();

        var so = new SerializedObject(controller);

        // 루트
        so.FindProperty("_wizardRoot").objectReferenceValue = wizardRoot.gameObject;

        // 좌측 캐릭터
        so.FindProperty("_characterPanel").objectReferenceValue     = leftPanel.gameObject;
        so.FindProperty("_characterPreviewImage").objectReferenceValue = charPreviewImage;
        so.FindProperty("_characterNameLabel").objectReferenceValue  = charNameTMP;

        // 우측 Step 패널
        so.FindProperty("_stepNamePanel").objectReferenceValue    = stepNamePanel.gameObject;
        so.FindProperty("_stepRolePanel").objectReferenceValue    = stepRolePanel.gameObject;
        so.FindProperty("_stepModelPanel").objectReferenceValue   = stepModelPanel.gameObject;
        so.FindProperty("_stepTonePanel").objectReferenceValue    = stepTonePanel.gameObject;
        so.FindProperty("_stepAvatarPanel").objectReferenceValue  = stepAvatarPanel.gameObject;
        so.FindProperty("_stepConfirmPanel").objectReferenceValue = stepConfirmPanel.gameObject;

        // 공통 UI
        so.FindProperty("_stepTitleText").objectReferenceValue = stepTitleTMP;
        so.FindProperty("_stepDescText").objectReferenceValue  = stepDescRT.GetComponent<TMP_Text>();
        so.FindProperty("_stepCountText").objectReferenceValue = stepCountRT.GetComponent<TMP_Text>();
        so.FindProperty("_progressBar").objectReferenceValue   = slider;
        so.FindProperty("_prevButton").objectReferenceValue    = prevBtn;
        so.FindProperty("_nextButton").objectReferenceValue    = nextBtn;
        so.FindProperty("_nextButtonText").objectReferenceValue = nextBtnText;

        // Step 1
        so.FindProperty("_nameInput").objectReferenceValue          = nameInputField;
        so.FindProperty("_nameValidationText").objectReferenceValue = nameValidRT.GetComponent<TMP_Text>();

        // Step 2: 역할 칩 배열
        var roleChipsProp = so.FindProperty("_roleChips");
        roleChipsProp.arraySize = Roles.Length;
        for (int i = 0; i < Roles.Length; i++)
        {
            var elem = roleChipsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("Role").enumValueIndex = (int)Roles[i].role;
            elem.FindPropertyRelative("Button").objectReferenceValue = roleChipObjects[i].btn;
            elem.FindPropertyRelative("SelectedIndicator").objectReferenceValue = roleChipObjects[i].indicator;
        }

        // Step 3: 모델 카드 배열
        var modelCardsProp = so.FindProperty("_modelCards");
        modelCardsProp.arraySize = Models.Length;
        for (int i = 0; i < Models.Length; i++)
        {
            var elem = modelCardsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("Model").enumValueIndex = (int)Models[i].model;
            elem.FindPropertyRelative("Button").objectReferenceValue = modelCardObjects[i].btn;
            elem.FindPropertyRelative("SelectedIndicator").objectReferenceValue = modelCardObjects[i].indicator;
            elem.FindPropertyRelative("NameText").objectReferenceValue = modelCardObjects[i].nameTMP;
            elem.FindPropertyRelative("DescText").objectReferenceValue = modelCardObjects[i].descTMP;
            elem.FindPropertyRelative("RecommendTag").objectReferenceValue = modelCardObjects[i].recommendTag;
        }

        // Step 4: 말투 칩 배열
        var toneChipsProp = so.FindProperty("_toneChips");
        toneChipsProp.arraySize = Tones.Length;
        for (int i = 0; i < Tones.Length; i++)
        {
            var elem = toneChipsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("Tone").enumValueIndex = (int)Tones[i].tone;
            elem.FindPropertyRelative("Button").objectReferenceValue = toneChipObjects[i].btn;
            elem.FindPropertyRelative("SelectedIndicator").objectReferenceValue = toneChipObjects[i].indicator;
        }

        // Step 5: 아바타 카드 배열
        var avatarCardsProp = so.FindProperty("_avatarCards");
        avatarCardsProp.arraySize = avatarDefs.Length;
        for (int i = 0; i < avatarDefs.Length; i++)
        {
            var elem = avatarCardsProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("PrefabName").stringValue = avatarDefs[i].prefabName;
            elem.FindPropertyRelative("DisplayName").stringValue = avatarDefs[i].displayName;
            elem.FindPropertyRelative("Button").objectReferenceValue = avatarCardObjects[i].btn;
            elem.FindPropertyRelative("SelectedIndicator").objectReferenceValue = avatarCardObjects[i].indicator;
            elem.FindPropertyRelative("PreviewImage").objectReferenceValue = avatarCardObjects[i].preview;
        }

        // Step 6: 확인
        so.FindProperty("_confirmNameText").objectReferenceValue   = confirmNameTMP;
        so.FindProperty("_confirmRoleText").objectReferenceValue   = confirmRoleTMP;
        so.FindProperty("_confirmModelText").objectReferenceValue  = confirmModelTMP;
        so.FindProperty("_confirmToneText").objectReferenceValue   = confirmToneTMP;
        so.FindProperty("_confirmAvatarText").objectReferenceValue = confirmAvatarTMP;
        so.FindProperty("_createButton").objectReferenceValue      = createBtn;
        so.FindProperty("_creatingOverlay").objectReferenceValue   = creatingOverlay.gameObject;
        so.FindProperty("_creatingStatusText").objectReferenceValue = creatingStatusTMP;

        so.ApplyModifiedPropertiesWithoutUndo();

        // ================================================================
        //  EventSystem
        // ================================================================
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ================================================================
        //  초기 상태: Step1만 보이게
        // ================================================================
        stepNamePanel.gameObject.SetActive(true);
        stepRolePanel.gameObject.SetActive(false);
        stepModelPanel.gameObject.SetActive(false);
        stepTonePanel.gameObject.SetActive(false);
        stepAvatarPanel.gameObject.SetActive(false);
        stepConfirmPanel.gameObject.SetActive(false);

        // ================================================================
        //  씬 저장
        // ================================================================
        var scenePath = "Assets/01.Scenes/AgentCreationScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[AgentCreationSceneBuilder] 씬 생성 완료: {scenePath}");
        EditorUtility.DisplayDialog("완료",
            "AgentCreationScene이 생성되었습니다.\n\n" +
            "• 해상도: 1280 x 720\n" +
            "• 좌측: 캐릭터 프리뷰\n" +
            "• 우측: 6단계 위저드 (이름→역할→AI모델→말투→아바타→확인)\n" +
            "• 하단: 이전/다음 공통 버튼\n\n" +
            "Game View에서 1280x720 해상도로 확인하세요.",
            "OK");
    }

    // ================================================================
    //  Step 패널 팩토리
    // ================================================================

    private static RectTransform CreateStepPanel(string name, RectTransform parent)
    {
        var panel = CreatePanel(name, parent, new Color32(0, 0, 0, 0));
        // StepTitle/Desc 아래 ~ 패널 하단
        SetRect(panel, new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(0, 0),
            new Vector2(0, -110)); // Title+Desc 공간 확보
        return panel;
    }

    // ================================================================
    //  칩 (Chip) 생성
    // ================================================================

    private static (GameObject obj, Button btn, GameObject indicator) CreateChip(
        string name, Transform parent, string label)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        var img = obj.AddComponent<Image>();
        img.color = (Color)ColCard;

        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        var btnColors = btn.colors;
        btnColors.normalColor = (Color)ColCard;
        btnColors.highlightedColor = (Color)ColCardHover;
        btnColors.pressedColor = (Color)ColAccentDim;
        btn.colors = btnColors;

        // 라벨
        var labelObj = new GameObject("Label");
        labelObj.transform.SetParent(obj.transform, false);
        var labelRT = labelObj.AddComponent<RectTransform>();
        Stretch(labelRT);
        var labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label;
        labelTMP.fontSize = 16;
        labelTMP.color = (Color)ColTextMain;
        labelTMP.alignment = TextAlignmentOptions.Center;
        if (_font != null) labelTMP.font = _font;

        // 선택 인디케이터 (좌측 바)
        var indicator = new GameObject("SelectedIndicator");
        indicator.transform.SetParent(obj.transform, false);
        var indRT = indicator.AddComponent<RectTransform>();
        indRT.anchorMin = new Vector2(0, 0.1f);
        indRT.anchorMax = new Vector2(0, 0.9f);
        indRT.offsetMin = Vector2.zero;
        indRT.offsetMax = new Vector2(4, 0);
        var indImg = indicator.AddComponent<Image>();
        indImg.color = (Color)ColSelected;
        indicator.SetActive(false);

        return (obj, btn, indicator);
    }

    // ================================================================
    //  모델 카드 생성
    // ================================================================

    private static (GameObject obj, Button btn, GameObject indicator, TMP_Text nameTMP, TMP_Text descTMP, GameObject recommendTag)
        CreateModelCard(string name, Transform parent, string modelName, string modelDesc, bool recommended)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();

        var img = obj.AddComponent<Image>();
        img.color = (Color)ColCard;

        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        var btnColors = btn.colors;
        btnColors.normalColor = (Color)ColCard;
        btnColors.highlightedColor = (Color)ColCardHover;
        btnColors.pressedColor = (Color)ColAccentDim;
        btn.colors = btnColors;

        // LayoutElement
        var le = obj.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;

        // 모델 이름
        var nameRT = CreateTMP("ModelName", rt, modelName, 18, ColWhite);
        SetRect(nameRT, new Vector2(0, 0.6f), new Vector2(1, 0.85f), new Vector2(12, 0), new Vector2(-12, 0));
        var nameTMP = nameRT.GetComponent<TMP_Text>();
        nameTMP.alignment = TextAlignmentOptions.Center;
        nameTMP.fontStyle = FontStyles.Bold;

        // 설명
        var descRT = CreateTMP("ModelDesc", rt, modelDesc, 13, ColTextSub);
        SetRect(descRT, new Vector2(0, 0.2f), new Vector2(1, 0.6f), new Vector2(10, 0), new Vector2(-10, 0));
        var descTMP = descRT.GetComponent<TMP_Text>();
        descTMP.alignment = TextAlignmentOptions.Center;

        // 추천 태그
        var tagObj = new GameObject("RecommendTag");
        tagObj.transform.SetParent(obj.transform, false);
        var tagRT = tagObj.AddComponent<RectTransform>();
        tagRT.anchorMin = new Vector2(0.5f, 0.88f);
        tagRT.anchorMax = new Vector2(0.5f, 0.88f);
        tagRT.sizeDelta = new Vector2(55, 22);
        var tagImg = tagObj.AddComponent<Image>();
        tagImg.color = (Color)ColRecommend;

        var tagLabel = new GameObject("TagLabel");
        tagLabel.transform.SetParent(tagObj.transform, false);
        var tagLabelRT = tagLabel.AddComponent<RectTransform>();
        Stretch(tagLabelRT);
        var tagLabelTMP = tagLabel.AddComponent<TextMeshProUGUI>();
        tagLabelTMP.text = "추천";
        tagLabelTMP.fontSize = 11;
        tagLabelTMP.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        tagLabelTMP.alignment = TextAlignmentOptions.Center;
        tagLabelTMP.fontStyle = FontStyles.Bold;
        if (_font != null) tagLabelTMP.font = _font;

        tagObj.SetActive(recommended);

        // 선택 인디케이터 (하단 바)
        var indicator = new GameObject("SelectedIndicator");
        indicator.transform.SetParent(obj.transform, false);
        var indRT = indicator.AddComponent<RectTransform>();
        indRT.anchorMin = new Vector2(0.1f, 0);
        indRT.anchorMax = new Vector2(0.9f, 0);
        indRT.offsetMin = Vector2.zero;
        indRT.offsetMax = new Vector2(0, 4);
        var indImg = indicator.AddComponent<Image>();
        indImg.color = (Color)ColSelected;
        indicator.SetActive(false);

        return (obj, btn, indicator, nameTMP, descTMP, tagObj);
    }

    // ================================================================
    //  아바타 카드 생성
    // ================================================================

    private static (GameObject obj, Button btn, GameObject indicator, Image preview)
        CreateAvatarCard(string name, Transform parent, string displayName)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<RectTransform>();

        var img = obj.AddComponent<Image>();
        img.color = (Color)ColCard;

        var btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;
        var btnColors = btn.colors;
        btnColors.normalColor = (Color)ColCard;
        btnColors.highlightedColor = (Color)ColCardHover;
        btnColors.pressedColor = (Color)ColAccentDim;
        btn.colors = btnColors;

        // 미리보기 영역 (색칠된 사각형 — 큐브 아이콘 대용)
        var previewObj = new GameObject("Preview");
        previewObj.transform.SetParent(obj.transform, false);
        var previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = new Vector2(0.15f, 0.35f);
        previewRT.anchorMax = new Vector2(0.85f, 0.9f);
        previewRT.offsetMin = Vector2.zero;
        previewRT.offsetMax = Vector2.zero;
        var previewImg = previewObj.AddComponent<Image>();
        previewImg.color = new Color(0.4f, 0.6f, 0.9f, 0.6f); // 큐브 색상 힌트

        // 큐브 아이콘 텍스트 (임시)
        var iconRT = CreateTMP("Icon", previewRT, "[ ]", 30, ColWhite);
        Stretch(iconRT);
        iconRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        // 이름 라벨
        var labelRT = CreateTMP("Label", obj.GetComponent<RectTransform>(), displayName, 14, ColTextMain);
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(1, 0.3f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        labelRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

        // 선택 인디케이터 (하단 바)
        var indicator = new GameObject("SelectedIndicator");
        indicator.transform.SetParent(obj.transform, false);
        var indRT = indicator.AddComponent<RectTransform>();
        indRT.anchorMin = new Vector2(0.1f, 0);
        indRT.anchorMax = new Vector2(0.9f, 0);
        indRT.offsetMin = Vector2.zero;
        indRT.offsetMax = new Vector2(0, 4);
        var indImg = indicator.AddComponent<Image>();
        indImg.color = (Color)ColSelected;
        indicator.SetActive(false);

        return (obj, btn, indicator, previewImg);
    }

    // ================================================================
    //  확인 패널 행 생성
    // ================================================================

    private static TMP_Text CreateConfirmRow(string label, float yNorm, RectTransform parent)
    {
        // 라벨
        var labelRT = CreateTMP($"Label_{label}", parent, label, 14, ColTextSub);
        SetRect(labelRT, new Vector2(0.05f, yNorm), new Vector2(0.3f, yNorm + 0.15f), Vector2.zero, Vector2.zero);
        labelRT.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // 값
        var valueRT = CreateTMP($"Value_{label}", parent, "-", 17, ColWhite);
        SetRect(valueRT, new Vector2(0.32f, yNorm), new Vector2(0.95f, yNorm + 0.15f), Vector2.zero, Vector2.zero);
        var valueTMP = valueRT.GetComponent<TMP_Text>();
        valueTMP.alignment = TextAlignmentOptions.MidlineLeft;
        valueTMP.fontStyle = FontStyles.Bold;

        // 구분선
        var line = CreatePanel($"Line_{label}", parent, new Color32(60, 60, 80, 255));
        SetRect(line, new Vector2(0.05f, yNorm - 0.01f), new Vector2(0.95f, yNorm), Vector2.zero, Vector2.zero);

        return valueTMP;
    }

    // ================================================================
    //  UI 헬퍼
    // ================================================================

    private static GameObject CreateUIRoot(string name, Transform parent)
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

    private static TextMeshProUGUI CreateTextChild(string name, Transform parent, string text, int fontSize, Color color)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var rt = obj.AddComponent<RectTransform>();
        Stretch(rt);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        if (_font != null) tmp.font = _font;
        return tmp;
    }

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
        Stretch(textRT);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.color = (Color)ColWhite;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        if (_font != null) tmp.font = _font;

        return obj;
    }

    // ── RectTransform 유틸 ───────────────────────────────────

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void AnchorTop(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(0, -height);
        rt.offsetMax = Vector2.zero;
    }

    private static void AnchorBottom(RectTransform rt, float height)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(1, 0);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, height);
    }
}
