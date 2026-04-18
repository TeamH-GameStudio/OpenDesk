using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AgentProtocolTestScene — Office 환경 + 새 프로토콜 테스트 통합 씬.
/// 메뉴: Tools > OpenDesk > Build Agent Protocol Test Scene
///
/// 초기 상태: 3D Office 전체 뷰 + 우측 로그 패널만 표시
/// 에이전트 클릭 시: 좌측 패널 슬라이드인 (채팅+테스트 버튼)
///
/// 레이아웃 (좌측 패널 활성 시, 우측 패널 제외 기준):
///   좌측 40% = 채팅/테스트 패널
///   우측 60% = 3D Office 뷰 (에이전트)
///   고정 우측 320px = 세션+로그
/// </summary>
public static class AgentProtocolTestSceneBuilder
{
    // ── 색상 ──────────────────────────────────────────────
    static readonly Color32 CHeaderBg   = new(40, 40, 40, 255);
    static readonly Color32 CPanelBg    = new(38, 38, 42, 255);
    static readonly Color32 CInputBg    = new(51, 51, 51, 255);
    static readonly Color32 CInputField = new(66, 66, 66, 255);
    static readonly Color32 CUserBubble = new(33, 150, 243, 255);
    static readonly Color32 CAIBubble   = new(66, 66, 66, 255);
    static readonly Color32 CSysText    = new(255, 213, 79, 255);
    static readonly Color32 CGreen      = new(76, 175, 80, 255);
    static readonly Color32 CRed        = new(244, 67, 54, 255);
    static readonly Color32 CBlue       = new(33, 150, 243, 255);
    static readonly Color32 CBtnDef     = new(80, 80, 80, 255);
    static readonly Color32 CBtnAct     = new(33, 150, 243, 255);
    static readonly Color32 CMockBtn    = new(100, 80, 40, 255);
    static readonly Color32 CMockGreen  = new(40, 100, 40, 255);
    static readonly Color32 CWhite      = new(255, 255, 255, 255);
    static readonly Color32 CGray       = new(170, 170, 170, 255);
    static readonly Color32 CDimGray    = new(120, 120, 120, 255);
    static readonly Color32 CThinkBg    = new(60, 50, 30, 255);
    static readonly Color32 CLogBg      = new(35, 35, 35, 255);
    static readonly Color32 CSessionBg  = new(50, 50, 50, 255);

    const string FontPath       = "Assets/NotoSansKR-VariableFont_wght.asset";
    const string PrefabDir      = "Assets/05.Prefabs/UI/ProtocolTest";
    const string OfficePath     = "Assets/Mnostva_Art/Prefabs/Rooms/Office_35.prefab";
    const string ModelPath      = "Assets/03.Models/SD_Maneqquin/SD_ManeequinPrefab.prefab";
    const string AnimCtrlPath   = "Assets/03.Models/SD_Maneqquin/AgentAnimatorController.controller";
    const string HudPath        = "Assets/05.Prefabs/Agent/AgentHUD.prefab";
    const string AgentOfficePath = "Assets/05.Prefabs/AgentOffice.prefab";

    const int RightW = 640;

    static TMP_FontAsset _font;

    // ══════════════════════════════════════════════════════
    //  메인
    // ══════════════════════════════════════════════════════

    // ── Office 프리팹 직접 지정 위저드 ──────────────────────
    internal static GameObject _customOfficePrefab;

    [MenuItem("Tools/OpenDesk/Build Agent Protocol Test Scene", false, 110)]
    public static void ShowBuildWizard()
    {
        var window = EditorWindow.GetWindow<AgentProtocolTestSceneWizard>(
            true, "Agent Protocol Test Scene Builder", true);
        window.minSize = new Vector2(420, 160);
        window.maxSize = new Vector2(420, 160);
    }

    public static void Build()
    {
        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        // 프리팹 생성
        var userPrefab   = BuildBubblePrefab("Bubble_User", CUserBubble, TextAlignmentOptions.TopRight);
        var aiPrefab     = BuildBubblePrefab("Bubble_AI",   CAIBubble,   TextAlignmentOptions.TopLeft);
        var sysPrefab    = BuildSystemBubblePrefab();
        var sessionItem  = BuildSessionItemPrefab();
        var sessionTab   = BuildSessionTabPrefab();

        // 씬 생성
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = "AgentProtocolTestScene";

        // ─── 3D 환경 ───────────────────────────────────────
        Setup3DEnvironment(out var spawnerComp, out var bootComp, out var clickHandler, out var isoCam);

        // ─── Canvas ─────────────────────────────────────────
        var canvasObj = new GameObject("Canvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();
        var canvasRT = canvasObj.GetComponent<RectTransform>();

        // ═══════════════════════════════════════════════════
        //  좌측 패널 (항상 표시, 240px) — 테스트 버튼 + 로그
        // ═══════════════════════════════════════════════════
        const int LeftW = 240;
        var leftPanel = CreatePanel("LeftPanel", canvasRT, CPanelBg);
        Anchor(leftPanel, V(0,0), V(0,1), V(0,0), V(LeftW,0));

        // 상단 헤더
        var leftHeader = CreatePanel("LeftHeader", leftPanel, CHeaderBg);
        Anchor(leftHeader, V(0,1), V(1,1), V(0,-45), V(0,0));
        var connDot = CreateImage("ConnectionDot", leftHeader, CRed);
        Anchor(connDot, V(0,0.5f), V(0,0.5f), V(10,0), V(10,0));
        connDot.sizeDelta = new Vector2(14, 14);
        var agentLabel = CreateTMP("AgentLabel", leftHeader, "에이전트: -", 16, CWhite);
        Anchor(agentLabel, V(0,0), V(1,1), V(30,0), V(-5,0));
        agentLabel.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
        agentLabel.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;

        // 테스트 버튼 영역 (스크롤, 상단 65%)
        var btnScroll = CreateScrollRect("ButtonScroll", leftPanel, out var btnContent, 4);
        Anchor(btnScroll.GetComponent<RectTransform>(), V(0,0), V(1,1), V(2,4), V(-2,-48));
        btnScroll.GetComponent<Image>().color = new Color(0, 0, 0, 0.1f);

        // 에이전트 선택
        CreateLabel("LblAgents", btnContent, "-- 에이전트 선택 --", 12, CDimGray);
        var btnResearcher = CreateBtn("BtnResearcher", btnContent, "리서처", CBtnAct);
        var btnWriter     = CreateBtn("BtnWriter",     btnContent, "라이터", CBtnDef);
        var btnAnalyst    = CreateBtn("BtnAnalyst",    btnContent, "분석가", CBtnDef);

        CreateSpacer("Sp0", btnContent, 6);
        CreateLabel("LblFsm", btnContent, "-- FSM 상태 테스트 --", 12, CDimGray);
        var btnFsmIdle        = CreateBtn("BtnFsmIdle",        btnContent, "Idle",         CBtnDef);
        var btnFsmThinking    = CreateBtn("BtnFsmThinking",    btnContent, "Thinking",     CBtnDef);
        var btnFsmChatting    = CreateBtn("BtnFsmChatting",    btnContent, "Chatting",     CBtnDef);
        var btnFsmCompleted   = CreateBtn("BtnFsmCompleted",   btnContent, "Completed",    CBtnDef);
        var btnFsmError       = CreateBtn("BtnFsmError",       btnContent, "Error",        CBtnDef);
        var btnFsmDisconnected= CreateBtn("BtnFsmDisconnected",btnContent, "Disconnected", CBtnDef);
        var btnFsmTyping      = CreateBtn("BtnFsmTyping",      btnContent, "Typing(도구)", CBtnDef);

        CreateSpacer("Sp1", btnContent, 6);
        CreateLabel("LblTest", btnContent, "-- 서버 테스트 --", 12, CDimGray);
        var btnSessionList  = CreateBtn("BtnSessionList",  btnContent, "세션 목록",   CBtnDef);
        var btnSessionNew   = CreateBtn("BtnSessionNew",   btnContent, "새 세션",     CBtnDef);
        var btnStatusReq    = CreateBtn("BtnStatusRequest", btnContent, "상태 요청",   CBtnDef);
        var btnChatClear    = CreateBtn("BtnChatClear",    btnContent, "대화 초기화", CBtnDef);
        var btnReconnect    = CreateBtn("BtnReconnect",    btnContent, "재연결",      CBtnDef);

        CreateSpacer("Sp2", btnContent, 6);
        CreateLabel("LblMock", btnContent, "-- Mock 프로토콜 --", 12, CDimGray);
        var btnMockThinking    = CreateBtn("BtnMockThinking",    btnContent, "생각 주입",     CMockBtn);
        var btnMockDelta       = CreateBtn("BtnMockDelta",       btnContent, "스트리밍 주입", CMockBtn);
        var btnMockMessage     = CreateBtn("BtnMockMessage",     btnContent, "최종 응답 주입", CMockBtn);
        var btnMockState       = CreateBtn("BtnMockState",       btnContent, "상태 시퀀스",   CMockBtn);
        var btnMockSessionList = CreateBtn("BtnMockSessionList", btnContent, "세션 목록 주입", CMockBtn);
        var btnMockFullFlow    = CreateBtn("BtnMockFullFlow",    btnContent, "전체 흐름 시뮬", CMockGreen);

        // 로그 제거 — Debug.Log로만 확인
        ScrollRect logScroll = null;
        RectTransform logContent = null;

        // ═══════════════════════════════════════════════════
        //  우측 패널 (초기 숨김, 에이전트 클릭 시 활성화)
        //  세션 목록 → 세션 클릭 → 채팅 패널로 전환
        // ═══════════════════════════════════════════════════
        var rightPanel = new GameObject("RightSessionChatPanel");
        rightPanel.transform.SetParent(canvasRT, false);
        var rightRT = rightPanel.AddComponent<RectTransform>();
        Anchor(rightRT, V(1,0), V(1,1), V(-RightW,0), V(0,0));
        rightPanel.AddComponent<Image>().color = (Color)CPanelBg;
        rightPanel.SetActive(false); // 에이전트 클릭 시 활성화

        // ═══ 세션 목록 뷰 (초기 표시) ═══════════════════════
        var sessionView = new GameObject("SessionView");
        sessionView.transform.SetParent(rightRT, false);
        var sessionViewRT = sessionView.AddComponent<RectTransform>();
        Anchor(sessionViewRT, V(0,0), V(1,1), V(0,0), V(0,0));

        // 세션 헤더
        var sessionHeader = CreatePanel("SessionHeader", sessionViewRT, CHeaderBg);
        Anchor(sessionHeader, V(0,1), V(1,1), V(0,-45), V(0,0));
        var sessionLabel = CreateTMP("SessionLabel", sessionHeader, "세션 목록", 17, CWhite);
        Anchor(sessionLabel, V(0,0), V(0.65f,1), V(12,0), V(0,0));
        sessionLabel.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
        sessionLabel.GetComponent<TMP_Text>().fontStyle = FontStyles.Bold;

        // + 새 세션 버튼
        var btnAddSessionObj = new GameObject("BtnAddSession");
        btnAddSessionObj.transform.SetParent(sessionHeader, false);
        var btnAddRT = btnAddSessionObj.AddComponent<RectTransform>();
        Anchor(btnAddRT, V(0.65f,0), V(1,1), V(4,6), V(-6,-6));
        var btnAddImg = btnAddSessionObj.AddComponent<Image>();
        btnAddImg.color = (Color)CGreen;
        var btnAddBtn = btnAddSessionObj.AddComponent<Button>();
        btnAddBtn.targetGraphic = btnAddImg;
        var btnAddTextObj = new GameObject("Text");
        btnAddTextObj.transform.SetParent(btnAddSessionObj.transform, false);
        Anchor(btnAddTextObj.AddComponent<RectTransform>(), V(0,0), V(1,1), V(0,0), V(0,0));
        var btnAddTMP = btnAddTextObj.AddComponent<TextMeshProUGUI>();
        btnAddTMP.text = "+ 새 세션";
        btnAddTMP.fontSize = 18;
        btnAddTMP.color = (Color)CWhite;
        btnAddTMP.alignment = TextAlignmentOptions.Center;
        btnAddTMP.fontStyle = FontStyles.Bold;
        if (_font) btnAddTMP.font = _font;

        // 세션 리스트 (vertical 스크롤)
        var sessionScroll = CreateScrollRect("SessionList", sessionViewRT, out var sessionListContent, 4);
        Anchor(sessionScroll.GetComponent<RectTransform>(), V(0,0), V(1,1), V(4,4), V(-4,-48));

        // ═══ 채팅 뷰 (세션 클릭 시 전환, 초기 숨김) ═══════
        var chatView = new GameObject("ChatView");
        chatView.transform.SetParent(rightRT, false);
        var chatViewRT = chatView.AddComponent<RectTransform>();
        Anchor(chatViewRT, V(0,0), V(1,1), V(0,0), V(0,0));
        chatView.SetActive(false);

        // 채팅 헤더 (세션 이름 + 뒤로가기)
        var chatHeader = CreatePanel("ChatHeader", chatViewRT, CHeaderBg);
        Anchor(chatHeader, V(0,1), V(1,1), V(0,-45), V(0,0));

        var btnBackObj = new GameObject("BtnBack");
        btnBackObj.transform.SetParent(chatHeader, false);
        var btnBackRT = btnBackObj.AddComponent<RectTransform>();
        Anchor(btnBackRT, V(0,0), V(0,1), V(4,6), V(44,-6));
        var btnBackImg = btnBackObj.AddComponent<Image>();
        btnBackImg.color = new Color(0.4f, 0.4f, 0.45f);
        var btnBackBtn = btnBackObj.AddComponent<Button>();
        btnBackBtn.targetGraphic = btnBackImg;
        var btnBackText = new GameObject("Text");
        btnBackText.transform.SetParent(btnBackObj.transform, false);
        Anchor(btnBackText.AddComponent<RectTransform>(), V(0,0), V(1,1), V(0,0), V(0,0));
        var btnBackTMP = btnBackText.AddComponent<TextMeshProUGUI>();
        btnBackTMP.text = "<";
        btnBackTMP.fontSize = 26;
        btnBackTMP.color = (Color)CWhite;
        btnBackTMP.alignment = TextAlignmentOptions.Center;
        if (_font) btnBackTMP.font = _font;

        var statusText = CreateTMP("StatusText", chatHeader, "대화 중", 15, CWhite);
        Anchor(statusText, V(0,0), V(1,1), V(50,0), V(-8,0));
        statusText.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;

        // 채팅 영역
        var chatScroll = CreateScrollRect("ChatScroll", chatViewRT, out var chatContent, 6);
        Anchor(chatScroll.GetComponent<RectTransform>(), V(0,0), V(1,1), V(4,110), V(-4,-48));
        chatScroll.GetComponent<Image>().color = new Color(0, 0, 0, 0.2f);

        // Thinking
        var thinkingBg = CreatePanel("ThinkingBg", chatViewRT, CThinkBg);
        Anchor(thinkingBg, V(0,0), V(1,0), V(0,65), V(0,110));
        var thinkingText = CreateTMP("ThinkingText", thinkingBg, "", 13, CSysText);
        Anchor(thinkingText, V(0,0), V(1,1), V(8,0), V(-8,0));
        thinkingText.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.MidlineLeft;
        thinkingText.GetComponent<TMP_Text>().fontStyle = FontStyles.Italic;
        thinkingBg.gameObject.SetActive(false);

        // Input (채팅 뷰에만 존재)
        var inputRow = CreatePanel("InputRow", chatViewRT, CInputBg);
        Anchor(inputRow, V(0,0), V(1,0), V(0,0), V(0,65));

        var inputFieldObj = new GameObject("InputField");
        inputFieldObj.transform.SetParent(inputRow, false);
        var inputFieldRT = inputFieldObj.AddComponent<RectTransform>();
        Anchor(inputFieldRT, V(0,0), V(1,1), V(8,8), V(-70,-8));
        inputFieldObj.AddComponent<Image>().color = (Color)CInputField;

        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputFieldObj.transform, false);
        var textAreaRT = textArea.AddComponent<RectTransform>();
        Anchor(textAreaRT, V(0,0), V(1,1), V(8,3), V(-8,-3));
        textArea.AddComponent<RectMask2D>();

        CreateTMPChild("Placeholder", textArea.transform, "메시지 입력...", 14, new Color(0.5f,0.5f,0.5f,0.5f));
        var inputTextObj = CreateTMPChild("Text", textArea.transform, "", 14, CWhite);

        var tmpInput = inputFieldObj.AddComponent<TMP_InputField>();
        tmpInput.textViewport = textAreaRT;
        tmpInput.textComponent = inputTextObj.GetComponent<TextMeshProUGUI>();
        tmpInput.placeholder = textArea.transform.Find("Placeholder").GetComponent<TMP_Text>();
        tmpInput.lineType = TMP_InputField.LineType.MultiLineNewline;
        tmpInput.richText = false;

        var sendBtn = CreateBtn("SendButton", inputRow, "전송", CBlue);
        sendBtn.SetParent(inputRow, false);
        Anchor(sendBtn, V(1,0), V(1,1), V(-62,8), V(-6,-8));

        // ─── Manager 오브젝트 ──────────────────────────────
        var wsClientObj = new GameObject("ClaudeWebSocketClient");
        var wsClient = wsClientObj.AddComponent<OpenDesk.Claude.ClaudeWebSocketClient>();

        // MiddlewareLauncher — Python 서버 자동 실행
        var launcherObj = new GameObject("MiddlewareLauncher");
        launcherObj.AddComponent<OpenDesk.Claude.MiddlewareLauncher>();

        var managerObj = new GameObject("AgentProtocolTestManager");
        var manager = managerObj.AddComponent<OpenDesk.Claude.AgentProtocolTestManager>();

        // ─── Inspector 바인딩 ──────────────────────────────
        var so = new SerializedObject(manager);
        so.FindProperty("_wsClient").objectReferenceValue = wsClient;

        so.FindProperty("_btnResearcher").objectReferenceValue = btnResearcher.GetComponent<Button>();
        so.FindProperty("_btnWriter").objectReferenceValue     = btnWriter.GetComponent<Button>();
        so.FindProperty("_btnAnalyst").objectReferenceValue    = btnAnalyst.GetComponent<Button>();

        so.FindProperty("_inputField").objectReferenceValue    = tmpInput;
        so.FindProperty("_sendButton").objectReferenceValue    = sendBtn.GetComponent<Button>();
        so.FindProperty("_chatScrollRect").objectReferenceValue = chatScroll;
        so.FindProperty("_chatContent").objectReferenceValue   = chatContent;
        so.FindProperty("_userBubblePrefab").objectReferenceValue   = userPrefab;
        so.FindProperty("_aiBubblePrefab").objectReferenceValue     = aiPrefab;
        so.FindProperty("_systemBubblePrefab").objectReferenceValue = sysPrefab;

        so.FindProperty("_statusText").objectReferenceValue    = statusText.GetComponent<TMP_Text>();
        so.FindProperty("_agentLabel").objectReferenceValue    = agentLabel.GetComponent<TMP_Text>();
        so.FindProperty("_thinkingText").objectReferenceValue  = thinkingText.GetComponent<TMP_Text>();
        so.FindProperty("_connectionDot").objectReferenceValue = connDot.GetComponent<Image>();

        so.FindProperty("_btnSessionList").objectReferenceValue  = btnSessionList.GetComponent<Button>();
        so.FindProperty("_btnSessionNew").objectReferenceValue   = btnSessionNew.GetComponent<Button>();
        so.FindProperty("_btnStatusRequest").objectReferenceValue = btnStatusReq.GetComponent<Button>();
        so.FindProperty("_btnChatClear").objectReferenceValue    = btnChatClear.GetComponent<Button>();
        so.FindProperty("_btnReconnect").objectReferenceValue    = btnReconnect.GetComponent<Button>();

        so.FindProperty("_btnMockThinking").objectReferenceValue    = btnMockThinking.GetComponent<Button>();
        so.FindProperty("_btnMockDelta").objectReferenceValue       = btnMockDelta.GetComponent<Button>();
        so.FindProperty("_btnMockMessage").objectReferenceValue     = btnMockMessage.GetComponent<Button>();
        so.FindProperty("_btnMockState").objectReferenceValue       = btnMockState.GetComponent<Button>();
        so.FindProperty("_btnMockSessionList").objectReferenceValue = btnMockSessionList.GetComponent<Button>();
        so.FindProperty("_btnMockFullFlow").objectReferenceValue    = btnMockFullFlow.GetComponent<Button>();

        // FSM 버튼
        so.FindProperty("_btnFsmIdle").objectReferenceValue         = btnFsmIdle.GetComponent<Button>();
        so.FindProperty("_btnFsmThinking").objectReferenceValue     = btnFsmThinking.GetComponent<Button>();
        so.FindProperty("_btnFsmChatting").objectReferenceValue     = btnFsmChatting.GetComponent<Button>();
        so.FindProperty("_btnFsmCompleted").objectReferenceValue    = btnFsmCompleted.GetComponent<Button>();
        so.FindProperty("_btnFsmError").objectReferenceValue        = btnFsmError.GetComponent<Button>();
        so.FindProperty("_btnFsmDisconnected").objectReferenceValue = btnFsmDisconnected.GetComponent<Button>();
        so.FindProperty("_btnFsmTyping").objectReferenceValue       = btnFsmTyping.GetComponent<Button>();

        // 우측 세션+채팅 패널
        so.FindProperty("_rightSessionChatPanel").objectReferenceValue = rightPanel;
        so.FindProperty("_btnAddSession").objectReferenceValue = btnAddBtn;

        // 세션/채팅 뷰 전환
        so.FindProperty("_sessionView").objectReferenceValue       = sessionView;
        so.FindProperty("_chatView").objectReferenceValue          = chatView;
        so.FindProperty("_btnBack").objectReferenceValue           = btnBackBtn;
        so.FindProperty("_sessionListContent").objectReferenceValue = sessionListContent;
        so.FindProperty("_sessionItemPrefab").objectReferenceValue = sessionItem;

        // 로그 제거됨 — null 바인딩
        so.FindProperty("_logScrollRect").objectReferenceValue = null;
        so.FindProperty("_logContent").objectReferenceValue    = null;
        so.FindProperty("_logEntryPrefab").objectReferenceValue = null;

        so.ApplyModifiedPropertiesWithoutUndo();

        // ─── 우측 패널 토글 (에이전트 클릭 시 세션+채팅 활성화) ───
        var panelToggle = managerObj.AddComponent<OpenDesk.Presentation.UI.LeftPanelToggle>();
        var toggleSo = new SerializedObject(panelToggle);
        toggleSo.FindProperty("_leftPanel").objectReferenceValue = rightPanel;
        toggleSo.ApplyModifiedPropertiesWithoutUndo();

        // ─── AgentClickHandler 바인딩 ──────────────────────
        if (clickHandler != null)
        {
            var chSo = new SerializedObject(clickHandler);
            chSo.FindProperty("_spawner").objectReferenceValue = spawnerComp;
            chSo.FindProperty("_isometricCamera").objectReferenceValue = isoCam;
            chSo.FindProperty("_leftPanelToggle").objectReferenceValue = panelToggle;
            chSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // .env 파일 자동 생성
        var projectRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, ".."));
        var envPath = System.IO.Path.Combine(projectRoot, "Middleware", ".env");
        if (!System.IO.File.Exists(envPath))
        {
            System.IO.File.WriteAllText(envPath,
                "# OpenDesk Middleware\n" +
                "ANTHROPIC_API_KEY=your-api-key-here\n" +
                "# BRAVE_API_KEY=your-brave-key-here\n");
            Debug.LogWarning($"[ProtocolTestBuilder] .env 생성됨 — ANTHROPIC_API_KEY 설정 필요: {envPath}");
        }

        // 씬 저장
        var scenePath = "Assets/01.Scenes/AgentProtocolTestScene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[ProtocolTestBuilder] 씬 생성 완료: {scenePath}");
        EditorUtility.DisplayDialog("완료",
            "AgentProtocolTestScene 생성 완료!\n\n" +
            "1. Middleware/.env에 ANTHROPIC_API_KEY 설정\n" +
            "2. Play → 서버 자동 실행 + 에이전트 배회\n" +
            "3. 에이전트 클릭 → 좌측 패널 활성화\n" +
            "4. Mock 버튼으로 프로토콜 테스트\n" +
            "5. 입력란에 메시지 → Enter로 실제 대화", "OK");
    }

    // ══════════════════════════════════════════════════════
    //  3D 환경 구성
    // ══════════════════════════════════════════════════════

    static void Setup3DEnvironment(
        out OpenDesk.Presentation.Character.AgentSpawner spawner,
        out OpenDesk.Presentation.Character.AgentOfficeBootstrapper bootstrapper,
        out OpenDesk.Presentation.Character.AgentClickHandler clickHandler,
        out OpenDesk.Presentation.Camera.IsometricCameraController isoCam)
    {
        spawner = null;
        bootstrapper = null;
        clickHandler = null;
        isoCam = null;

        // -- Office 환경 (커스텀 프리팹 우선, 없으면 기본 경로 폴백)
        var officePrefab = _customOfficePrefab;
        if (officePrefab == null)
            officePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OfficePath);
        if (officePrefab == null)
            officePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgentOfficePath);

        GameObject officeInstance = null;
        if (officePrefab != null)
        {
            officeInstance = (GameObject)PrefabUtility.InstantiatePrefab(officePrefab);
            officeInstance.name = "Office";
            officeInstance.transform.position = Vector3.zero;
            officeInstance.transform.rotation = Quaternion.Euler(0, 140, 0);

            // Navigation Static
            SetStaticRecursive(officeInstance);

            // NavMeshSurface
            var navSurface = officeInstance.GetComponent<NavMeshSurface>();
            if (navSurface == null)
                navSurface = officeInstance.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.Children;
            navSurface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            navSurface.BuildNavMesh();
            Debug.Log("[ProtocolTestBuilder] NavMesh 베이크 완료");

            // 가구 레이어 — 프리팹에 이미 설정된 것 유지, 건드리지 않음
        }
        else
        {
            Debug.LogWarning("[ProtocolTestBuilder] Office 프리팹 없음 — 바닥만 생성");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2, 1, 2);
            officeInstance = floor;
        }

        // -- Office 바운드 계산 (카메라/스폰/조명 동적 배치 기준)
        var officeBounds = CalculateCompositeBounds(officeInstance);
        var center = officeBounds.center;
        var extents = officeBounds.extents;
        float floorY = DetectFloorHeight(officeInstance);

        Debug.Log($"[ProtocolTestBuilder] Office 바운드: center={center}, size={officeBounds.size}, floorY={floorY}");

        // -- 카메라 위치 계산 (이소메트릭 40도, yaw 0)
        // Office를 Y=140 회전해서 뚫린 면이 카메라(Z- 방향) 쪽을 향하게 함
        float maxExtent = Mathf.Max(extents.x, extents.z);
        float camHeight = center.y + maxExtent * 1.2f + 2f;
        float camDepth = center.z - maxExtent * 1.0f - 3f;
        var overviewPos = new Vector3(center.x, camHeight, camDepth);
        var overviewRot = Quaternion.Euler(40, 0, 0);

        // -- 카메라 설정 (이소메트릭, 각도 고정)
        var cam = UnityEngine.Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            cam.transform.position = overviewPos;
            cam.transform.rotation = overviewRot;
            cam.fieldOfView = 50;

            // WallFadeController
            cam.gameObject.AddComponent<OpenDesk.Presentation.Camera.WallFadeController>();
        }

        // -- Cinemachine Brain
        if (cam != null && cam.GetComponent<Cinemachine.CinemachineBrain>() == null)
        {
            var brain = cam.gameObject.AddComponent<Cinemachine.CinemachineBrain>();
            brain.m_DefaultBlend = new Cinemachine.CinemachineBlendDefinition(
                Cinemachine.CinemachineBlendDefinition.Style.EaseInOut, 0.8f);
        }

        // -- VCam 1: 오피스 오버뷰 (바운드 기반 자동 배치, 초기 활성)
        var overviewObj = new GameObject("CM_OverviewCam");
        var overviewCam = overviewObj.AddComponent<Cinemachine.CinemachineVirtualCamera>();
        overviewCam.m_Lens.FieldOfView = 50;
        overviewCam.Priority = 20;
        overviewObj.transform.position = overviewPos;
        overviewObj.transform.rotation = overviewRot;

        // -- VCam 2: 에이전트 포커스 (클릭 시 전환)
        var agentCamObj = new GameObject("CM_AgentCam");
        var agentCam = agentCamObj.AddComponent<Cinemachine.CinemachineVirtualCamera>();
        agentCam.m_Lens.FieldOfView = 50;
        agentCam.Priority = 10;
        agentCamObj.transform.rotation = overviewRot; // 동일 각도 고정

        var transposer = agentCam.AddCinemachineComponent<Cinemachine.CinemachineTransposer>();
        transposer.m_FollowOffset = new Vector3(0f, 3f, -5f);
        transposer.m_BindingMode = Cinemachine.CinemachineTransposer.BindingMode.WorldSpace;
        transposer.m_XDamping = 1f;
        transposer.m_YDamping = 1f;
        transposer.m_ZDamping = 1f;

        // -- IsometricCameraController (2 VCam 전환 관리)
        var isoCamObj = new GameObject("IsometricCameraController");
        isoCam = isoCamObj.AddComponent<OpenDesk.Presentation.Camera.IsometricCameraController>();
        var isoCamSo = new SerializedObject(isoCam);
        isoCamSo.FindProperty("_overviewCam").objectReferenceValue = overviewCam;
        isoCamSo.FindProperty("_agentCam").objectReferenceValue = agentCam;
        isoCamSo.FindProperty("_agentOffset").vector3Value = new Vector3(0f, 3f, -5f);
        isoCamSo.FindProperty("_zoomSpeed").floatValue = 15f;
        isoCamSo.FindProperty("_minDistance").floatValue = 0.5f;
        isoCamSo.FindProperty("_maxDistance").floatValue = 30f;
        isoCamSo.FindProperty("_focusHeight").floatValue = 0.8f;
        isoCamSo.ApplyModifiedPropertiesWithoutUndo();

        // -- 조명 (Office 중심 기준 동적 배치)
        var lightObj = GameObject.Find("Directional Light");
        if (lightObj != null)
        {
            lightObj.transform.rotation = Quaternion.Euler(40, -30, 0);
            var light = lightObj.GetComponent<Light>();
            if (light != null) light.intensity = 1.2f;
        }

        var pointLight = new GameObject("PointLight");
        var pl = pointLight.AddComponent<Light>();
        pl.type = LightType.Point;
        pl.range = Mathf.Max(maxExtent * 2f, 12f);
        pl.intensity = 0.8f;
        pl.color = new Color(1f, 0.95f, 0.9f);
        pointLight.transform.position = new Vector3(center.x, center.y + extents.y + 2f, center.z);

        // -- SpawnPoints (Office 바운드 내부에 균등 분배)
        var spawnParent = new GameObject("SpawnPoints");
        spawnParent.transform.SetParent(officeInstance.transform);
        spawnParent.transform.localPosition = Vector3.zero;
        spawnParent.transform.localRotation = Quaternion.identity;

        // 바운드 내부 25%~75% 범위에 4개 배치
        float xMin = center.x - extents.x * 0.5f;
        float xMax = center.x + extents.x * 0.5f;
        float zMin = center.z - extents.z * 0.5f;
        float zMax = center.z + extents.z * 0.5f;

        var spawnWorldPositions = new Vector3[]
        {
            new(xMin, floorY, zMin),
            new(xMax, floorY, zMin),
            new(xMin, floorY, zMax),
            new(xMax, floorY, zMax),
        };

        var spawnTransforms = new Transform[spawnWorldPositions.Length];
        for (int i = 0; i < spawnWorldPositions.Length; i++)
        {
            var sp = new GameObject($"SpawnPoint_{i}");
            sp.transform.SetParent(spawnParent.transform);
            sp.transform.position = spawnWorldPositions[i]; // 월드 좌표 직접 배치
            sp.transform.localRotation = Quaternion.identity;
            spawnTransforms[i] = sp.transform;
            Debug.Log($"[ProtocolTestBuilder] SpawnPoint_{i}: world={spawnWorldPositions[i]}, local={sp.transform.localPosition}");
        }

        // -- WorkStation: 프리팹에 있으면 그대로 사용
        var existingStations = officeInstance.GetComponentsInChildren<OpenDesk.Presentation.Character.WorkStation>();
        Debug.Log($"[ProtocolTestBuilder] WorkStation {existingStations.Length}개 발견 -- 프리팹 그대로 사용");

        // -- AgentSpawner
        var spawnerObj = new GameObject("AgentSpawner");
        spawner = spawnerObj.AddComponent<OpenDesk.Presentation.Character.AgentSpawner>();

        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);

        var spawnerSo = new SerializedObject(spawner);
        var spProp = spawnerSo.FindProperty("_spawnPoints");
        spProp.arraySize = spawnTransforms.Length;
        for (int i = 0; i < spawnTransforms.Length; i++)
            spProp.GetArrayElementAtIndex(i).objectReferenceValue = spawnTransforms[i];

        if (modelPrefab != null)
            spawnerSo.FindProperty("_defaultModelPrefab").objectReferenceValue = modelPrefab;
        if (hudPrefab != null)
            spawnerSo.FindProperty("_hudPrefab").objectReferenceValue = hudPrefab;
        spawnerSo.FindProperty("_hudHeight").floatValue = 2.2f;
        spawnerSo.ApplyModifiedPropertiesWithoutUndo();

        // -- AgentOfficeBootstrapper
        var bootObj = new GameObject("AgentOfficeBootstrapper");
        bootstrapper = bootObj.AddComponent<OpenDesk.Presentation.Character.AgentOfficeBootstrapper>();
        var bootSo = new SerializedObject(bootstrapper);
        bootSo.FindProperty("_spawner").objectReferenceValue = spawner;

        // 모델 프리팹 매핑
        var prefabsProp = bootSo.FindProperty("_modelPrefabs");
        if (prefabsProp != null && modelPrefab != null)
        {
            prefabsProp.arraySize = 1;
            var elem = prefabsProp.GetArrayElementAtIndex(0);
            elem.FindPropertyRelative("PrefabName").stringValue = "SD_Maneqquin";
            elem.FindPropertyRelative("Prefab").objectReferenceValue = modelPrefab;
        }
        bootSo.ApplyModifiedPropertiesWithoutUndo();

        // -- AgentClickHandler
        var clickObj = new GameObject("AgentClickHandler");
        clickHandler = clickObj.AddComponent<OpenDesk.Presentation.Character.AgentClickHandler>();
        var clickSo = new SerializedObject(clickHandler);
        clickSo.FindProperty("_spawner").objectReferenceValue = spawner;
        // _focusCamera는 AgentFocusCameraController 타입 — 이소메트릭 모드에서는 사용 안 함
        clickSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static float DetectFloorHeight(GameObject officeRoot)
    {
        // Floor 이름 오브젝트 검색
        foreach (var r in officeRoot.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.name.ToLower().Contains("floor"))
                return r.bounds.max.y;
        }
        // 바운드 최소 Y 폴백 (가장 낮은 렌더러)
        var renderers = officeRoot.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length > 0)
        {
            float minY = float.MaxValue;
            foreach (var r in renderers)
                minY = Mathf.Min(minY, r.bounds.min.y);
            return minY;
        }
        return 0f;
    }

    /// <summary>모든 자식 렌더러의 합산 바운드 계산</summary>
    static Bounds CalculateCompositeBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 10f);

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>
    /// 기존 WorkStation 컴포넌트를 실제 의자 위치로 재배치.
    /// Type별로 매칭: Main→MainAgentChair, Sub→SubAgentChair, Resting→RestingChair
    /// 의자 위치에 WorkStation을 이동하고, 가장 가까운 책상 방향으로 회전.
    /// </summary>
    static void RepositionWorkStationsToChairs(
        GameObject officeRoot,
        OpenDesk.Presentation.Character.WorkStation[] stations)
    {
        // 레이어별 의자 수집
        var chairsByType = new Dictionary<OpenDesk.Presentation.Character.WorkStationType,
            System.Collections.Generic.List<Transform>>();
        chairsByType[OpenDesk.Presentation.Character.WorkStationType.Main] = new();
        chairsByType[OpenDesk.Presentation.Character.WorkStationType.Sub] = new();
        chairsByType[OpenDesk.Presentation.Character.WorkStationType.Resting] = new();

        int mainLayer = LayerMask.NameToLayer("MainAgentChair");
        int subLayer = LayerMask.NameToLayer("SubAgentChair");
        int restLayer = LayerMask.NameToLayer("RestingChair");

        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            if (mainLayer >= 0 && t.gameObject.layer == mainLayer)
                chairsByType[OpenDesk.Presentation.Character.WorkStationType.Main].Add(t);
            else if (subLayer >= 0 && t.gameObject.layer == subLayer)
                chairsByType[OpenDesk.Presentation.Character.WorkStationType.Sub].Add(t);
            else if (restLayer >= 0 && t.gameObject.layer == restLayer)
                chairsByType[OpenDesk.Presentation.Character.WorkStationType.Resting].Add(t);
        }

        // 책상 수집 (방향 계산용)
        var desks = new System.Collections.Generic.List<Transform>();
        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLower();
            if (n.Contains("table") || n.Contains("desk"))
                desks.Add(t);
        }

        // 사용된 의자 추적 (중복 배치 방지)
        var usedChairs = new HashSet<Transform>();

        foreach (var ws in stations)
        {
            var type = ws.Type;
            if (!chairsByType.ContainsKey(type) || chairsByType[type].Count == 0)
            {
                Debug.LogWarning($"[ProtocolTestBuilder] '{ws.name}' (type={type}) — 매칭되는 의자 레이어 없음, 스킵");
                continue;
            }

            // 아직 안 쓴 의자 중 첫 번째
            Transform bestChair = null;
            foreach (var chair in chairsByType[type])
            {
                if (!usedChairs.Contains(chair))
                {
                    bestChair = chair;
                    break;
                }
            }

            if (bestChair == null)
            {
                Debug.LogWarning($"[ProtocolTestBuilder] '{ws.name}' (type={type}) — 남은 의자 없음, 스킵");
                continue;
            }

            usedChairs.Add(bestChair);

            // 가장 가까운 책상 → 바라볼 방향
            var chairPos = bestChair.position;
            Vector3 lookForward = bestChair.forward;

            float closestDist = float.MaxValue;
            foreach (var desk in desks)
            {
                float dist = Vector3.Distance(chairPos, desk.position);
                if (dist < closestDist && dist < 3f)
                {
                    closestDist = dist;
                    var dir = desk.position - chairPos;
                    dir.y = 0;
                    if (dir.sqrMagnitude > 0.01f)
                        lookForward = dir.normalized;
                }
            }

            // WorkStation 이동 + 회전
            ws.transform.position = chairPos;
            ws.transform.rotation = Quaternion.LookRotation(lookForward, Vector3.up);

            // SitPoint 자식이 있으면 위치 보정
            var wsSo = new SerializedObject(ws);
            var sitProp = wsSo.FindProperty("_sitPoint");
            if (sitProp.objectReferenceValue is Transform sitT)
            {
                sitT.position = chairPos + Vector3.up * 0.3f;
                sitT.rotation = Quaternion.LookRotation(lookForward, Vector3.up);
            }

            // ApproachPoint 자식이 있으면 옆쪽으로 배치
            var approachProp = wsSo.FindProperty("_approachPoint");
            if (approachProp.objectReferenceValue is Transform apT)
            {
                var sideDir = Vector3.Cross(Vector3.up, lookForward).normalized;
                apT.position = chairPos + sideDir * 0.6f;
                apT.rotation = Quaternion.LookRotation(lookForward, Vector3.up);
            }

            wsSo.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[ProtocolTestBuilder] '{ws.name}' → 의자 '{bestChair.name}' 위치로 재배치 (방향={lookForward})");
        }
    }

    /// <summary>
    /// 프리팹 내 WorkStationMain/WorkStationSub/WorkStationRest 이름 오브젝트에
    /// WorkStation 컴포넌트를 붙이고, 자식의 SitPoint/AproachPoint를 자동 바인딩.
    /// </summary>
    static int AttachWorkStationComponents(GameObject officeRoot)
    {
        int count = 0;
        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            var name = t.name;
            OpenDesk.Presentation.Character.WorkStationType? wsType = null;

            if (name.StartsWith("WorkStationMain"))
                wsType = OpenDesk.Presentation.Character.WorkStationType.Main;
            else if (name.StartsWith("WorkStationSub"))
                wsType = OpenDesk.Presentation.Character.WorkStationType.Sub;
            else if (name.StartsWith("WorkStationRest"))
                wsType = OpenDesk.Presentation.Character.WorkStationType.Resting;

            if (!wsType.HasValue) continue;

            // 이미 컴포넌트가 있으면 스킵
            if (t.GetComponent<OpenDesk.Presentation.Character.WorkStation>() != null) continue;

            var ws = t.gameObject.AddComponent<OpenDesk.Presentation.Character.WorkStation>();
            var wsSo = new SerializedObject(ws);
            wsSo.FindProperty("_type").enumValueIndex = (int)wsType.Value;

            // 자식에서 SitPoint / AproachPoint (오타 포함) 탐색
            Transform sitPoint = null;
            Transform approachPoint = null;
            foreach (Transform child in t)
            {
                var cn = child.name.ToLower();
                if (cn.Contains("sitpoint"))
                    sitPoint = child;
                else if (cn.Contains("aproachpoint") || cn.Contains("approachpoint"))
                    approachPoint = child;
            }

            if (sitPoint != null)
                wsSo.FindProperty("_sitPoint").objectReferenceValue = sitPoint;
            if (approachPoint != null)
                wsSo.FindProperty("_approachPoint").objectReferenceValue = approachPoint;

            wsSo.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[ProtocolTestBuilder] '{name}' → WorkStation({wsType.Value}) 컴포넌트 부착" +
                $" | SitPoint={(sitPoint != null ? "O" : "X")}" +
                $" | ApproachPoint={(approachPoint != null ? "O" : "X")}");
            count++;
        }

        if (count > 0)
            Debug.Log($"[ProtocolTestBuilder] WorkStation 컴포넌트 {count}개 자동 부착 완료");

        return count;
    }

    /// <summary>실제 의자 레이어(MainAgentChair/SubAgentChair/RestingChair)에서 WorkStation 자동 생성</summary>
    static void AutoCreateWorkStationsFromChairs(GameObject officeRoot, float floorY)
    {
        // 레이어 이름 → WorkStationType 매핑
        var layerMap = new (string layerName, OpenDesk.Presentation.Character.WorkStationType type)[]
        {
            ("MainAgentChair", OpenDesk.Presentation.Character.WorkStationType.Main),
            ("SubAgentChair",  OpenDesk.Presentation.Character.WorkStationType.Sub),
            ("RestingChair",   OpenDesk.Presentation.Character.WorkStationType.Resting),
            // 기존 레이어 폴백
            ("WorkPlaceChair", OpenDesk.Presentation.Character.WorkStationType.Main),
        };

        // 책상/테이블 오브젝트 수집 (방향 계산용)
        var desks = new System.Collections.Generic.List<Transform>();
        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLower();
            if (n.Contains("table") || n.Contains("desk"))
                desks.Add(t);
        }

        int created = 0;
        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            // 레이어 매칭
            OpenDesk.Presentation.Character.WorkStationType? wsType = null;
            foreach (var (layerName, type) in layerMap)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0 && t.gameObject.layer == layer)
                {
                    wsType = type;
                    break;
                }
            }

            // 레이어 매칭 안 되면 이름으로 폴백
            if (!wsType.HasValue)
            {
                var n = t.name.ToLower();
                if (n.Contains("mainagentchair") || n.Contains("main_chair"))
                    wsType = OpenDesk.Presentation.Character.WorkStationType.Main;
                else if (n.Contains("subagentchair") || n.Contains("sub_chair"))
                    wsType = OpenDesk.Presentation.Character.WorkStationType.Sub;
                else if (n.Contains("restingchair") || n.Contains("resting_chair"))
                    wsType = OpenDesk.Presentation.Character.WorkStationType.Resting;
            }

            if (!wsType.HasValue) continue;

            // 가장 가까운 책상 찾기 → 바라볼 방향 결정
            var chairPos = t.position;
            Vector3 lookForward = t.forward; // 기본값: 의자 자체 forward

            float closestDist = float.MaxValue;
            foreach (var desk in desks)
            {
                float dist = Vector3.Distance(chairPos, desk.position);
                if (dist < closestDist && dist < 3f) // 3m 이내 책상
                {
                    closestDist = dist;
                    var dir = desk.position - chairPos;
                    dir.y = 0;
                    if (dir.sqrMagnitude > 0.01f)
                        lookForward = dir.normalized;
                }
            }

            CreateTestWorkStation(
                officeRoot.transform,
                $"WorkStation_{wsType.Value}_{created}",
                wsType.Value,
                chairPos,
                lookForward);

            created++;
            Debug.Log($"[ProtocolTestBuilder] 의자 '{t.name}' (layer={LayerMask.LayerToName(t.gameObject.layer)}) → WorkStation ({wsType.Value}), 방향={lookForward}");
        }

        if (created == 0)
        {
            Debug.LogWarning("[ProtocolTestBuilder] 의자 레이어(MainAgentChair/SubAgentChair/RestingChair) 없음! " +
                "Office 프리팹의 의자에 레이어를 설정하거나, WorkStation 컴포넌트를 수동 추가하세요.");
        }
        else
        {
            Debug.Log($"[ProtocolTestBuilder] WorkStation {created}개 자동 생성 완료");
        }
    }

    /// <summary>테스트용 WorkStation 자동 생성 (프리팹에 없을 때)</summary>
    static void CreateTestWorkStation(
        Transform parent, string name,
        OpenDesk.Presentation.Character.WorkStationType type,
        Vector3 worldPos, Vector3 forward)
    {
        var wsObj = new GameObject(name);
        wsObj.transform.SetParent(parent);
        wsObj.transform.position = worldPos;
        wsObj.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        var ws = wsObj.AddComponent<OpenDesk.Presentation.Character.WorkStation>();
        var wsSo = new SerializedObject(ws);
        wsSo.FindProperty("_type").enumValueIndex = (int)type;
        wsSo.FindProperty("_seatYOffset").floatValue = 0.3f;
        wsSo.FindProperty("_approachDistance").floatValue = 0.7f;
        wsSo.ApplyModifiedPropertiesWithoutUndo();

        // SitPoint (자식, 의자 위치 = 부모 위치)
        var sitObj = new GameObject("SitPoint");
        sitObj.transform.SetParent(wsObj.transform);
        sitObj.transform.localPosition = Vector3.up * 0.3f;
        sitObj.transform.localRotation = Quaternion.identity;

        // ApproachPoint (자식, 오른쪽 0.6m — 등받이 회피)
        var approachObj = new GameObject("ApproachPoint");
        approachObj.transform.SetParent(wsObj.transform);
        approachObj.transform.localPosition = new Vector3(0.6f, 0, 0);
        approachObj.transform.localRotation = Quaternion.identity;

        // 포인트 바인딩
        wsSo = new SerializedObject(ws);
        wsSo.FindProperty("_sitPoint").objectReferenceValue = sitObj.transform;
        wsSo.FindProperty("_approachPoint").objectReferenceValue = approachObj.transform;
        wsSo.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[ProtocolTestBuilder] WorkStation 생성: {name} (type={type}, pos={worldPos})");
    }

    /// <summary>Office 가구에 레이어 자동 할당 (Chair/Table/Wall)</summary>
    static void AssignFurnitureLayers(GameObject officeRoot)
    {
        // 레이어 확인 (없으면 생성 불가 — 수동 생성 필요)
        int chairLayer = LayerMask.NameToLayer("WorkPlaceChair");
        int tableLayer = LayerMask.NameToLayer("WorkPlaceTable");
        int wallLayer  = LayerMask.NameToLayer("OfficeWall");

        if (chairLayer < 0 || tableLayer < 0 || wallLayer < 0)
        {
            Debug.LogWarning("[ProtocolTestBuilder] 레이어 미등록! " +
                "Edit > Project Settings > Tags and Layers에서 추가 필요:\n" +
                $"  WorkPlaceChair: {(chairLayer >= 0 ? "OK" : "없음")}\n" +
                $"  WorkPlaceTable: {(tableLayer >= 0 ? "OK" : "없음")}\n" +
                $"  OfficeWall: {(wallLayer >= 0 ? "OK" : "없음")}");
            return;
        }

        int assigned = 0;
        foreach (var t in officeRoot.GetComponentsInChildren<Transform>(true))
        {
            // 이미 Default(0)가 아닌 레이어가 설정된 오브젝트는 스킵 (프리팹 레이어 보존)
            if (t.gameObject.layer != 0) continue;

            var name = t.name.ToLower();

            if (name.Contains("chair"))
            {
                SetLayerRecursive(t.gameObject, chairLayer);
                assigned++;
            }
            else if (name.Contains("table") || name.Contains("desk"))
            {
                SetLayerRecursive(t.gameObject, tableLayer);
                assigned++;
            }
            else if (name.Contains("wall"))
            {
                SetLayerRecursive(t.gameObject, wallLayer);
                assigned++;
            }
        }

        Debug.Log($"[ProtocolTestBuilder] 가구 레이어 자동 할당: {assigned}개 오브젝트");
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    static void SetStaticRecursive(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
        foreach (Transform child in go.transform)
            SetStaticRecursive(child.gameObject);
    }

    // ══════════════════════════════════════════════════════
    //  프리팹
    // ══════════════════════════════════════════════════════

    static GameObject BuildBubblePrefab(string name, Color32 bg, TextAlignmentOptions align)
    {
        var root = new GameObject(name);
        root.AddComponent<RectTransform>();
        root.AddComponent<Image>().color = (Color)bg;
        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 12, 12);
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        root.AddComponent<LayoutElement>().flexibleWidth = 1;

        var t = new GameObject("MessageText");
        t.transform.SetParent(root.transform, false);
        t.AddComponent<RectTransform>();
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 22; tmp.color = (Color)CWhite; tmp.alignment = align;
        tmp.enableWordWrapping = true; tmp.richText = true;
        if (_font) tmp.font = _font;

        return SavePrefab(root, $"{PrefabDir}/{name}.prefab");
    }

    static GameObject BuildSystemBubblePrefab()
    {
        var root = new GameObject("Bubble_System");
        root.AddComponent<RectTransform>();
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var le = root.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minHeight = 26;

        var t = new GameObject("MessageText");
        t.transform.SetParent(root.transform, false);
        Anchor(t.AddComponent<RectTransform>(), V(0,0), V(1,1), V(0,0), V(0,0));
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 19; tmp.color = (Color)CSysText;
        tmp.alignment = TextAlignmentOptions.Center; tmp.fontStyle = FontStyles.Italic;
        tmp.enableWordWrapping = true;
        if (_font) tmp.font = _font;

        return SavePrefab(root, $"{PrefabDir}/Bubble_System.prefab");
    }

    static GameObject BuildSessionItemPrefab()
    {
        var root = new GameObject("SessionItem");
        root.AddComponent<RectTransform>();
        root.AddComponent<Image>().color = (Color)CSessionBg;
        root.AddComponent<Button>().targetGraphic = root.GetComponent<Image>();
        var le = root.AddComponent<LayoutElement>(); le.minHeight = 60; le.flexibleWidth = 1;

        var t = new GameObject("Label");
        t.transform.SetParent(root.transform, false);
        Anchor(t.AddComponent<RectTransform>(), V(0,0), V(1,1), V(12,4), V(-12,-4));
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 19; tmp.color = (Color)CWhite;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.overflowMode = TextOverflowModes.Ellipsis; tmp.enableWordWrapping = true;
        tmp.richText = true;
        if (_font) tmp.font = _font;

        return SavePrefab(root, $"{PrefabDir}/SessionItem.prefab");
    }

    static GameObject BuildSessionTabPrefab()
    {
        var root = new GameObject("SessionTab");
        var rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(100, 36);
        root.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);
        root.AddComponent<Button>().targetGraphic = root.GetComponent<Image>();

        var t = new GameObject("Label");
        t.transform.SetParent(root.transform, false);
        Anchor(t.AddComponent<RectTransform>(), V(0,0), V(1,1), V(8,2), V(-8,-2));
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 13; tmp.color = (Color)CWhite;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (_font) tmp.font = _font;

        return SavePrefab(root, $"{PrefabDir}/SessionTab.prefab");
    }

    static GameObject BuildLogEntryPrefab()
    {
        var root = new GameObject("LogEntry");
        root.AddComponent<RectTransform>();
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var le = root.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minHeight = 22;

        var t = new GameObject("Text");
        t.transform.SetParent(root.transform, false);
        Anchor(t.AddComponent<RectTransform>(), V(0,0), V(1,1), V(6,2), V(-6,-2));
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 13; tmp.color = (Color)CGray;
        tmp.alignment = TextAlignmentOptions.TopLeft; tmp.enableWordWrapping = true;
        if (_font) tmp.font = _font;

        return SavePrefab(root, $"{PrefabDir}/LogEntry.prefab");
    }

    // ══════════════════════════════════════════════════════
    //  UI 유틸
    // ══════════════════════════════════════════════════════

    static Vector2 V(float x, float y) => new(x, y);

    static RectTransform CreatePanel(string n, RectTransform p, Color32 c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>(); o.AddComponent<Image>().color = (Color)c;
        return rt;
    }

    static RectTransform CreateImage(string n, RectTransform p, Color32 c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>(); o.AddComponent<Image>().color = (Color)c;
        return rt;
    }

    static RectTransform CreateTMP(string n, RectTransform p, string text, int sz, Color32 c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>();
        var tmp = o.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = sz; tmp.color = (Color)c; tmp.enableWordWrapping = true;
        if (_font) tmp.font = _font;
        return rt;
    }

    static RectTransform CreateTMPChild(string n, Transform p, string text, int sz, Color c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>();
        Anchor(rt, V(0,0), V(1,1), V(0,0), V(0,0));
        var tmp = o.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = sz; tmp.color = c; tmp.enableWordWrapping = true;
        if (_font) tmp.font = _font;
        return rt;
    }

    static RectTransform CreateBtn(string n, RectTransform p, string label, Color32 bg)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>();
        var img = o.AddComponent<Image>(); img.color = (Color)bg;
        o.AddComponent<Button>().targetGraphic = img;
        var le = o.AddComponent<LayoutElement>(); le.minHeight = 36; le.flexibleWidth = 1;

        var t = new GameObject("Text"); t.transform.SetParent(o.transform, false);
        Anchor(t.AddComponent<RectTransform>(), V(0,0), V(1,1), V(0,0), V(0,0));
        var tmp = t.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 15; tmp.color = (Color)CWhite;
        tmp.alignment = TextAlignmentOptions.Center; tmp.fontStyle = FontStyles.Bold;
        if (_font) tmp.font = _font;
        return rt;
    }

    static void CreateLabel(string n, RectTransform p, string text, int sz, Color32 c)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        o.AddComponent<RectTransform>();
        var tmp = o.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = sz; tmp.color = (Color)c;
        tmp.alignment = TextAlignmentOptions.Center;
        if (_font) tmp.font = _font;
        o.AddComponent<LayoutElement>().minHeight = 22;
    }

    static void CreateSpacer(string n, RectTransform p, float h)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        o.AddComponent<RectTransform>(); o.AddComponent<LayoutElement>().minHeight = h;
    }

    static ScrollRect CreateScrollRect(string n, RectTransform p, out RectTransform content, float spacing)
    {
        var o = new GameObject(n); o.transform.SetParent(p, false);
        var rt = o.AddComponent<RectTransform>();
        Anchor(rt, V(0,0), V(1,1), V(0,0), V(0,0));
        o.AddComponent<Image>().color = new Color(0, 0, 0, 0.15f);
        var s = o.AddComponent<ScrollRect>(); s.horizontal = false; s.vertical = true;
        s.movementType = ScrollRect.MovementType.Clamped;

        var vp = new GameObject("Viewport"); vp.transform.SetParent(o.transform, false);
        var vpRT = vp.AddComponent<RectTransform>();
        Anchor(vpRT, V(0,0), V(1,1), V(0,0), V(0,0));
        vp.AddComponent<RectMask2D>();

        var co = new GameObject("Content"); co.transform.SetParent(vp.transform, false);
        content = co.AddComponent<RectTransform>();
        Anchor(content, V(0,1), V(1,1), V(0,0), V(0,0));
        content.pivot = new Vector2(0.5f, 1);
        var vlg = co.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing; vlg.padding = new RectOffset(6,6,6,6);
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        co.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        s.viewport = vpRT; s.content = content;
        return s;
    }

    static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
    { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax; }

    static GameObject SavePrefab(GameObject obj, string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
        if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
        {
            var parts = dir.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(obj, path);
        Object.DestroyImmediate(obj);
        return prefab;
    }
}

/// <summary>
/// Office 프리팹을 직접 지정하고 씬을 생성하는 위저드 창.
/// Tools > OpenDesk > Build Agent Protocol Test Scene 으로 열림.
/// </summary>
public class AgentProtocolTestSceneWizard : EditorWindow
{
    GameObject _officePrefab;
    bool _showPreview;
    Bounds _previewBounds;

    void OnEnable()
    {
        if (_officePrefab == null)
        {
            _officePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/05.Prefabs/OfficePrefabs/OpenOffice.prefab");
        }
    }

    void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Agent Protocol Test Scene Builder", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUI.BeginChangeCheck();
        _officePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Office Prefab", _officePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
            _showPreview = false;

        if (_officePrefab == null)
        {
            EditorGUILayout.HelpBox(
                "Office 프리팹을 지정하지 않으면 기본 Office_35 프리팹을 사용합니다.",
                MessageType.Info);
        }
        else
        {
            // 프리팹 바운드 미리보기
            if (GUILayout.Button("바운드 미리보기", GUILayout.Height(22)))
            {
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(_officePrefab);
                temp.transform.position = Vector3.zero;
                temp.transform.rotation = Quaternion.identity;
                var renderers = temp.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length > 0)
                {
                    _previewBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        _previewBounds.Encapsulate(renderers[i].bounds);
                }
                Object.DestroyImmediate(temp);
                _showPreview = true;
            }

            if (_showPreview)
            {
                EditorGUILayout.HelpBox(
                    $"Center: {_previewBounds.center}\n" +
                    $"Size: {_previewBounds.size}\n" +
                    $"Min: {_previewBounds.min}  Max: {_previewBounds.max}",
                    MessageType.None);
            }
        }

        GUILayout.Space(10);

        if (GUILayout.Button("씬 생성", GUILayout.Height(36)))
        {
            AgentProtocolTestSceneBuilder._customOfficePrefab = _officePrefab;
            AgentProtocolTestSceneBuilder.Build();
            AgentProtocolTestSceneBuilder._customOfficePrefab = null;
            Close();
        }
    }
}
