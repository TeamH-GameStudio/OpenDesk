#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// Office 씬에 환영 마법사 UI를 자동 생성 + Inspector 바인딩
    /// 이미 있는 요소는 건드리지 않음
    ///
    /// 사용: OpenDesk > Patch Office Scene (환영 마법사 추가)
    /// </summary>
    public static class OfficeScenePatcher
    {
        static readonly Color BgOverlay = new(0, 0, 0, 0.85f);
        static readonly Color PanelBg   = new(0.08f, 0.08f, 0.14f);
        static readonly Color BtnBlue   = new(0.231f, 0.510f, 0.965f);
        static readonly Color BtnGreen  = new(0.133f, 0.773f, 0.369f);
        static readonly Color BtnOrange = new(0.85f, 0.55f, 0.1f);
        static readonly Color BtnGray   = new(0.267f, 0.267f, 0.267f);
        static readonly Color BtnDark   = new(0.15f, 0.15f, 0.22f);
        static readonly Color TextWhite = Color.white;
        static readonly Color TextGray  = new(0.6f, 0.6f, 0.6f);
        static readonly Color TextWarn  = new(1f, 0.85f, 0.4f);

        [MenuItem("OpenDesk/Patch Office Scene (환영 마법사 추가)")]
        public static void Patch()
        {
            // 기존 WizardOverlay가 있으면 스킵
            if (GameObject.Find("WizardOverlay") != null)
            {
                Debug.Log("[OfficePatcher] WizardOverlay 이미 존재 — 바인딩만 재실행");
                BindController();
                return;
            }

            // Canvas 찾기 (Canvas_Main 또는 Canvas)
            var canvasGo = GameObject.Find("Canvas_Main") ?? GameObject.Find("Canvas");
            if (canvasGo == null)
            {
                Debug.LogError("[OfficePatcher] Canvas를 찾을 수 없습니다. Office 씬을 먼저 열어주세요.");
                return;
            }

            Debug.Log("[OfficePatcher] 환영 마법사 UI 생성 시작...");

            // ═══════════════════════════════════════════
            //  WizardOverlay (전체 화면 오버레이)
            // ═══════════════════════════════════════════
            var overlay = CreateChild(canvasGo, "WizardOverlay");
            StretchFull(overlay);
            var overlayImg = overlay.AddComponent<Image>();
            overlayImg.color = BgOverlay;

            // 메인 컨테이너 (중앙, 여백)
            var container = CreateChild(overlay, "WizardContainer");
            var crt = container.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0);
            crt.anchorMax = new Vector2(1, 1);
            crt.offsetMin = new Vector2(60, 60);
            crt.offsetMax = new Vector2(-60, -60);
            var containerImg = container.AddComponent<Image>();
            containerImg.color = PanelBg;

            // ── 상단 헤더 (타이틀 + 진행바 + 스텝) ──
            var header = CreateChild(container, "Header");
            var hrt = header.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0, 1);
            hrt.anchorMax = new Vector2(1, 1);
            hrt.offsetMin = new Vector2(0, -350);
            hrt.offsetMax = Vector2.zero;
            var hVlg = header.AddComponent<VerticalLayoutGroup>();
            hVlg.spacing = 16;
            hVlg.padding = new RectOffset(60, 60, 30, 20);
            hVlg.childAlignment = TextAnchor.MiddleCenter;
            hVlg.childForceExpandWidth = true;
            hVlg.childForceExpandHeight = false;

            var titleText = CreateTMP(header, "WizardTitleText", "AI 비서 환경이 준비되었어요!", 56, FontStyles.Bold);
            titleText.GetComponent<LayoutElement>().minHeight = 80;
            titleText.GetComponent<LayoutElement>().preferredHeight = 80;
            var descText = CreateTMP(header, "WizardDescText", "", 42, color: TextGray);
            descText.GetComponent<LayoutElement>().minHeight = 100;
            descText.GetComponent<LayoutElement>().preferredHeight = 100;
            var progressBar = CreateSlider(header, "WizardProgressBar");
            progressBar.GetComponent<LayoutElement>().preferredHeight = 20;
            var stepText = CreateTMP(header, "WizardStepText", "", 40, color: TextGray);
            stepText.GetComponent<LayoutElement>().minHeight = 50;

            // 뒤로가기 버튼 (헤더 좌측 상단, 앵커 포지션)
            var backBtn = CreateButton(container.gameObject, "BackButton", "← 이전", BtnGray, 280, 80);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0, 1);
            backRt.anchorMax = new Vector2(0, 1);
            backRt.sizeDelta = new Vector2(280, 80);
            backRt.anchoredPosition = new Vector2(170, -50);
            var backLe = backBtn.GetComponent<LayoutElement>();
            if (backLe != null) Object.DestroyImmediate(backLe);
            backBtn.SetActive(false);

            // ── 콘텐츠 영역 (패널들이 들어감) ──
            var content = CreateChild(container, "ContentArea");
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 0);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.offsetMin = new Vector2(40, 40);
            contentRt.offsetMax = new Vector2(-40, -360);

            // ═══════════════════════════════════════════
            //  7개 패널 생성
            // ═══════════════════════════════════════════

            // -- 1. Welcome --
            var welcomePanel = CreatePanel(content, "WelcomePanel");
            var welcomeStart = CreateButton(welcomePanel, "WelcomeStartButton", "시작하기", BtnBlue, 700, 130);

            // -- 2. ModelChoice --
            var modelPanel = CreatePanel(content, "ModelChoicePanel");
            var freeBtn = CreateButton(modelPanel, "FreeModelButton",
                "무료로 시작하기 (추천)\n내 컴퓨터에서 AI를 직접 실행해요", BtnGreen, 900, 150);
            var apiBtn = CreateButton(modelPanel, "ApiKeyModelButton",
                "API 키로 시작하기\nChatGPT, Claude 등 외부 AI를 연결해요", BtnBlue, 900, 150);
            var modelSkip = CreateButton(modelPanel, "ModelSkipButton", "나중에 설정할게요", BtnGray, 600, 100);
            var modelDiffToggle = CreateButton(modelPanel, "ModelDiffToggle", "무료와 유료의 차이가 뭔가요?", BtnDark, 700, 80);
            var modelDiffPanel = CreateChild(modelPanel, "ModelDiffPanel");
            modelDiffPanel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            var mdVlg = modelDiffPanel.AddComponent<VerticalLayoutGroup>();
            mdVlg.padding = new RectOffset(24, 24, 16, 16);
            modelDiffPanel.AddComponent<LayoutElement>().preferredHeight = 280;
            CreateTMP(modelDiffPanel, "DiffText",
                "무료(Ollama): 내 컴퓨터에서 실행.\n인터넷 불필요. 속도는 PC 성능에 따라 다름.\n\n" +
                "API 키: 외부 서버의 AI 사용.\n더 빠르고 똑똑하지만 사용량에 따라\n월 $8~$150 비용.\n\n" +
                "나중에 언제든 바꿀 수 있어요!",
                40, color: new Color(0.7f, 0.7f, 0.8f));
            modelDiffPanel.SetActive(false);

            // -- 3. OllamaSetup --
            var ollamaPanel = CreatePanel(content, "OllamaSetupPanel");
            var ollamaStatus = CreateTMP(ollamaPanel, "OllamaStatusText", "Ollama 상태 확인 중...", 44, color: TextWarn);
            var ollamaProgress = CreateSlider(ollamaPanel, "OllamaProgressSlider");
            var ollamaNext = CreateButton(ollamaPanel, "OllamaNextButton", "다음", BtnBlue, 500, 120);

            // -- 4. ApiKeySetup --
            var apiKeyPanel = CreatePanel(content, "ApiKeySetupPanel");
            CreateTMP(apiKeyPanel, "ProviderLabel", "사용할 AI 서비스를 선택하세요:", 42);
            var provRow = CreateChild(apiKeyPanel, "ProviderRow");
            var provHlg = provRow.AddComponent<HorizontalLayoutGroup>();
            provHlg.spacing = 16;
            provHlg.childForceExpandWidth = true;
            provRow.AddComponent<LayoutElement>().preferredHeight = 130;
            var pAnthro = CreateButton(provRow, "ProviderAnthropicBtn", "Anthropic\n(Claude)", BtnBlue, 0, 130);
            var pOpenAI = CreateButton(provRow, "ProviderOpenAIBtn", "OpenAI\n(ChatGPT)", BtnGreen, 0, 130);
            var pGoogle = CreateButton(provRow, "ProviderGoogleBtn", "Google\n(Gemini)", BtnOrange, 0, 130);
            var pOther = CreateButton(provRow, "ProviderOtherBtn", "기타", BtnGray, 0, 130);
            // Row 내부 버튼은 flexible width
            foreach (Transform child in provRow.transform)
            {
                var cle = child.GetComponent<LayoutElement>();
                if (cle != null) { cle.preferredWidth = -1; cle.flexibleWidth = 1; }
            }

            var apiInputArea = CreateChild(apiKeyPanel, "ApiKeyInputArea");
            var aiVlg = apiInputArea.AddComponent<VerticalLayoutGroup>();
            aiVlg.spacing = 14;
            aiVlg.childForceExpandWidth = true;
            aiVlg.childForceExpandHeight = false;
            var selectedProv = CreateTMP(apiInputArea, "SelectedProviderText", "", 44, FontStyles.Bold);
            var apiKeyInput = CreateInputField(apiInputArea, "ApiKeyInput", "API 키를 붙여넣기 하세요...");
            var apiValidate = CreateButton(apiInputArea, "ApiKeyValidateBtn", "키 확인하기", BtnBlue, 500, 110);
            var apiStatus = CreateTMP(apiInputArea, "ApiKeyStatusText", "", 40, color: TextGray);
            var apiNext = CreateButton(apiInputArea, "ApiKeyNextButton", "다음", BtnBlue, 500, 110);
            apiNext.SetActive(false);
            apiInputArea.SetActive(false);

            // -- 5. ChannelSetup --
            var channelPanel = CreatePanel(content, "ChannelSetupPanel");
            var chRow = CreateChild(channelPanel, "ChannelRow");
            var chHlg = chRow.AddComponent<HorizontalLayoutGroup>();
            chHlg.spacing = 16;
            chHlg.childForceExpandWidth = true;
            chRow.AddComponent<LayoutElement>().preferredHeight = 130;
            var chTele = CreateButton(chRow, "ChannelTelegramBtn", "Telegram", BtnBlue, 0, 130);
            var chDisc = CreateButton(chRow, "ChannelDiscordBtn", "Discord", new Color(0.34f, 0.40f, 0.95f), 0, 130);
            var chSlack = CreateButton(chRow, "ChannelSlackBtn", "Slack", new Color(0.25f, 0.60f, 0.45f), 0, 130);
            foreach (Transform child in chRow.transform)
            {
                var cle = child.GetComponent<LayoutElement>();
                if (cle != null) { cle.preferredWidth = -1; cle.flexibleWidth = 1; }
            }
            var chSkip = CreateButton(channelPanel, "ChannelSkipButton", "건너뛰기 — 나중에 설정에서", BtnGray, 700, 100);

            var chTokenArea = CreateChild(channelPanel, "ChannelTokenArea");
            var ctVlg = chTokenArea.AddComponent<VerticalLayoutGroup>();
            ctVlg.spacing = 14;
            ctVlg.childForceExpandWidth = true;
            ctVlg.childForceExpandHeight = false;
            var selectedCh = CreateTMP(chTokenArea, "SelectedChannelText", "", 44, FontStyles.Bold);
            var chTokenInput = CreateInputField(chTokenArea, "ChannelTokenInput", "봇 토큰을 입력하세요...");
            var chConnect = CreateButton(chTokenArea, "ChannelConnectBtn", "연결 테스트", BtnBlue, 500, 110);
            var chStatus = CreateTMP(chTokenArea, "ChannelStatusText", "", 40, color: TextGray);
            var chNext = CreateButton(chTokenArea, "ChannelNextButton", "다음", BtnBlue, 500, 110);
            chNext.SetActive(false);
            chTokenArea.SetActive(false);

            var chDiffToggle = CreateButton(channelPanel, "ChannelDiffToggle", "채널이 뭔가요?", BtnDark, 500, 80);
            var chDiffPanel = CreateChild(channelPanel, "ChannelDiffPanel");
            chDiffPanel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f);
            var cdVlg = chDiffPanel.AddComponent<VerticalLayoutGroup>();
            cdVlg.padding = new RectOffset(24, 24, 16, 16);
            chDiffPanel.AddComponent<LayoutElement>().preferredHeight = 240;
            CreateTMP(chDiffPanel, "DiffText",
                "채널은 AI 비서와 대화하는 '창구'예요.\nTelegram이나 Discord 같은 메신저에서도\nAI에게 질문하고 답을 받을 수 있어요.\n\n" +
                "설정하지 않아도 이 프로그램에서\n직접 대화 가능해요!",
                40, color: new Color(0.7f, 0.7f, 0.8f));
            chDiffPanel.SetActive(false);

            // -- 6. TestChat --
            var testPanel = CreatePanel(content, "TestChatPanel");
            CreateTMP(testPanel, "SuggestLabel", "추천 메시지를 클릭하거나 직접 입력해보세요:", 40, color: TextGray);
            var sug1 = CreateButton(testPanel, "Suggestion1Btn", "안녕! 넌 뭘 할 수 있어?", BtnDark, 800, 100);
            var sug2 = CreateButton(testPanel, "Suggestion2Btn", "오늘 날씨 알려줘", BtnDark, 800, 100);
            var sug3 = CreateButton(testPanel, "Suggestion3Btn", "내 파일 정리해줘", BtnDark, 800, 100);
            var testInput = CreateInputField(testPanel, "TestChatInput", "자유롭게 입력...");
            var testSend = CreateButton(testPanel, "TestChatSendBtn", "전송", BtnBlue, 300, 110);
            var testResponse = CreateTMP(testPanel, "TestChatResponseText", "", 40, color: new Color(0.7f, 0.9f, 1f));
            testResponse.GetComponent<LayoutElement>().minHeight = 100;
            var testDone = CreateButton(testPanel, "TestChatDoneBtn", "설정 완료하기", BtnGreen, 600, 120);
            testDone.SetActive(false);

            // -- 7. Complete --
            var completePanel = CreatePanel(content, "CompletePanel");
            var successIcon = CreateChild(completePanel, "SuccessIcon");
            successIcon.AddComponent<Image>().color = BtnGreen;
            var siLe = successIcon.AddComponent<LayoutElement>();
            siLe.preferredWidth = 120; siLe.preferredHeight = 120;
            CreateTMP(completePanel, "CompleteTitleText", "모든 준비가 끝났어요!", 52, FontStyles.Bold, BtnGreen);
            var summary = CreateTMP(completePanel, "SetupSummaryText", "", 40, color: TextGray);
            summary.GetComponent<LayoutElement>().minHeight = 100;
            CreateTMP(completePanel, "ChangeHint", "설정은 언제든 상단 탭에서 변경할 수 있어요.", 40, color: TextGray);
            var finishBtn = CreateButton(completePanel, "FinishButton", "AI 비서 사용 시작!", BtnBlue, 800, 140);

            // 모든 패널 초기 비활성
            welcomePanel.SetActive(false);
            modelPanel.SetActive(false);
            ollamaPanel.SetActive(false);
            apiKeyPanel.SetActive(false);
            channelPanel.SetActive(false);
            testPanel.SetActive(false);
            completePanel.SetActive(false);

            Debug.Log("[OfficePatcher] 7개 패널 + 45개 UI 요소 생성 완료");

            // ═══════════════════════════════════════════
            //  Inspector 바인딩
            // ═══════════════════════════════════════════
            BindController();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[OfficePatcher] 완료! Ctrl+S로 씬 저장하세요.");
        }

        // ═══════════════════════════════════════════════
        //  Inspector 자동 바인딩
        // ═══════════════════════════════════════════════

        static void BindController()
        {
            var overlay = GameObject.Find("WizardOverlay");
            if (overlay == null)
            {
                Debug.LogWarning("[OfficePatcher] WizardOverlay를 찾을 수 없습니다.");
                return;
            }

            var canvasGo = overlay.transform.parent?.gameObject;
            if (canvasGo == null) return;

            var ctrlType = System.Type.GetType(
                "OpenDesk.Presentation.UI.OfficeWizard.OfficeWizardController, Assembly-CSharp");
            if (ctrlType == null)
            {
                Debug.LogWarning("[OfficePatcher] OfficeWizardController 타입 미발견 — 컴파일 후 다시 실행");
                return;
            }

            var ctrl = canvasGo.GetComponent(ctrlType) ?? canvasGo.AddComponent(ctrlType);
            var so = new SerializedObject(ctrl);

            void Bind(string prop, Object value)
            {
                if (value == null) return;
                var sp = so.FindProperty(prop);
                if (sp != null && sp.objectReferenceValue == null)
                    sp.objectReferenceValue = value;
            }

            var container = overlay.transform.Find("WizardContainer");
            if (container == null) return;
            var contentArea = container.Find("ContentArea");
            if (contentArea == null) return;

            // 루트
            Bind("_wizardOverlay", overlay);

            // 공통
            Bind("_backButton", FindC<Button>(container, "BackButton"));

            // 공통 헤더
            Bind("_wizardTitleText", FindC<TMP_Text>(container, "Header/WizardTitleText"));
            Bind("_wizardDescText", FindC<TMP_Text>(container, "Header/WizardDescText"));
            Bind("_wizardProgressBar", FindC<Slider>(container, "Header/WizardProgressBar"));
            Bind("_wizardStepText", FindC<TMP_Text>(container, "Header/WizardStepText"));

            // 패널
            Bind("_welcomePanel", FindGO(contentArea, "WelcomePanel"));
            Bind("_modelChoicePanel", FindGO(contentArea, "ModelChoicePanel"));
            Bind("_ollamaSetupPanel", FindGO(contentArea, "OllamaSetupPanel"));
            Bind("_apiKeySetupPanel", FindGO(contentArea, "ApiKeySetupPanel"));
            Bind("_channelSetupPanel", FindGO(contentArea, "ChannelSetupPanel"));
            Bind("_testChatPanel", FindGO(contentArea, "TestChatPanel"));
            Bind("_completePanel", FindGO(contentArea, "CompletePanel"));

            // Welcome
            Bind("_welcomeStartButton", FindC<Button>(contentArea, "WelcomePanel/WelcomeStartButton"));

            // ModelChoice
            Bind("_freeModelButton", FindC<Button>(contentArea, "ModelChoicePanel/FreeModelButton"));
            Bind("_apiKeyModelButton", FindC<Button>(contentArea, "ModelChoicePanel/ApiKeyModelButton"));
            Bind("_modelSkipButton", FindC<Button>(contentArea, "ModelChoicePanel/ModelSkipButton"));
            Bind("_modelDiffToggle", FindC<Button>(contentArea, "ModelChoicePanel/ModelDiffToggle"));
            Bind("_modelDiffPanel", FindGO(contentArea, "ModelChoicePanel/ModelDiffPanel"));

            // Ollama
            Bind("_ollamaStatusText", FindC<TMP_Text>(contentArea, "OllamaSetupPanel/OllamaStatusText"));
            Bind("_ollamaProgressSlider", FindC<Slider>(contentArea, "OllamaSetupPanel/OllamaProgressSlider"));
            Bind("_ollamaNextButton", FindC<Button>(contentArea, "OllamaSetupPanel/OllamaNextButton"));

            // ApiKey
            Bind("_providerAnthropicBtn", FindC<Button>(contentArea, "ApiKeySetupPanel/ProviderRow/ProviderAnthropicBtn"));
            Bind("_providerOpenAIBtn", FindC<Button>(contentArea, "ApiKeySetupPanel/ProviderRow/ProviderOpenAIBtn"));
            Bind("_providerGoogleBtn", FindC<Button>(contentArea, "ApiKeySetupPanel/ProviderRow/ProviderGoogleBtn"));
            Bind("_providerOtherBtn", FindC<Button>(contentArea, "ApiKeySetupPanel/ProviderRow/ProviderOtherBtn"));
            Bind("_apiKeyInputArea", FindGO(contentArea, "ApiKeySetupPanel/ApiKeyInputArea"));
            Bind("_selectedProviderText", FindC<TMP_Text>(contentArea, "ApiKeySetupPanel/ApiKeyInputArea/SelectedProviderText"));
            Bind("_apiKeyInput", FindC<TMP_InputField>(contentArea, "ApiKeySetupPanel/ApiKeyInputArea/ApiKeyInput"));
            Bind("_apiKeyValidateBtn", FindC<Button>(contentArea, "ApiKeySetupPanel/ApiKeyInputArea/ApiKeyValidateBtn"));
            Bind("_apiKeyStatusText", FindC<TMP_Text>(contentArea, "ApiKeySetupPanel/ApiKeyInputArea/ApiKeyStatusText"));
            Bind("_apiKeyNextButton", FindC<Button>(contentArea, "ApiKeySetupPanel/ApiKeyInputArea/ApiKeyNextButton"));

            // Channel
            Bind("_channelTelegramBtn", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelRow/ChannelTelegramBtn"));
            Bind("_channelDiscordBtn", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelRow/ChannelDiscordBtn"));
            Bind("_channelSlackBtn", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelRow/ChannelSlackBtn"));
            Bind("_channelSkipButton", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelSkipButton"));
            Bind("_channelTokenArea", FindGO(contentArea, "ChannelSetupPanel/ChannelTokenArea"));
            Bind("_selectedChannelText", FindC<TMP_Text>(contentArea, "ChannelSetupPanel/ChannelTokenArea/SelectedChannelText"));
            Bind("_channelTokenInput", FindC<TMP_InputField>(contentArea, "ChannelSetupPanel/ChannelTokenArea/ChannelTokenInput"));
            Bind("_channelConnectBtn", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelTokenArea/ChannelConnectBtn"));
            Bind("_channelStatusText", FindC<TMP_Text>(contentArea, "ChannelSetupPanel/ChannelTokenArea/ChannelStatusText"));
            Bind("_channelNextButton", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelTokenArea/ChannelNextButton"));
            Bind("_channelDiffToggle", FindC<Button>(contentArea, "ChannelSetupPanel/ChannelDiffToggle"));
            Bind("_channelDiffPanel", FindGO(contentArea, "ChannelSetupPanel/ChannelDiffPanel"));

            // TestChat
            Bind("_suggestion1Btn", FindC<Button>(contentArea, "TestChatPanel/Suggestion1Btn"));
            Bind("_suggestion2Btn", FindC<Button>(contentArea, "TestChatPanel/Suggestion2Btn"));
            Bind("_suggestion3Btn", FindC<Button>(contentArea, "TestChatPanel/Suggestion3Btn"));
            Bind("_testChatInput", FindC<TMP_InputField>(contentArea, "TestChatPanel/TestChatInput"));
            Bind("_testChatSendBtn", FindC<Button>(contentArea, "TestChatPanel/TestChatSendBtn"));
            Bind("_testChatResponseText", FindC<TMP_Text>(contentArea, "TestChatPanel/TestChatResponseText"));
            Bind("_testChatDoneBtn", FindC<Button>(contentArea, "TestChatPanel/TestChatDoneBtn"));

            // Complete
            Bind("_setupSummaryText", FindC<TMP_Text>(contentArea, "CompletePanel/SetupSummaryText"));
            Bind("_finishButton", FindC<Button>(contentArea, "CompletePanel/FinishButton"));

            so.ApplyModifiedPropertiesWithoutUndo();

            // 결과 리포트
            int bound = 0, total = 0;
            var iter = so.GetIterator();
            while (iter.NextVisible(true))
            {
                if (iter.propertyType == SerializedPropertyType.ObjectReference &&
                    iter.name.StartsWith("_") && iter.depth == 0)
                {
                    total++;
                    if (iter.objectReferenceValue != null) bound++;
                }
            }
            Debug.Log($"[OfficePatcher] Inspector 바인딩: {bound}/{total} 연결됨");
        }

        // ═══════════════════════════════════════════════
        //  유틸리티
        // ═══════════════════════════════════════════════

        static T FindC<T>(Transform root, string path) where T : Component
        {
            var t = root.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        static GameObject FindGO(Transform root, string path)
        {
            var t = root.Find(path);
            return t?.gameObject;
        }

        static GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        static void StretchFull(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static GameObject CreatePanel(GameObject parent, string name)
        {
            var go = CreateChild(parent, name);
            StretchFull(go);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 24;
            vlg.padding = new RectOffset(40, 40, 30, 30);
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            // 스크롤 가능하도록 ContentSizeFitter 추가
            var csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static TMP_FontAsset _font;
        static TMP_FontAsset Font
        {
            get
            {
                if (_font == null)
                    _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NotoSansKR-VariableFont_wght.asset");
                return _font;
            }
        }

        // ── 최소 폰트 40 기준 사이즈 설정 ──────────────────

        static TMP_Text CreateTMP(GameObject parent, string name, string text, float size,
            FontStyles style = FontStyles.Normal, Color? color = null)
        {
            // 최소 40pt 보장
            size = Mathf.Max(size, 40f);

            var go = CreateChild(parent, name);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color ?? TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            if (Font != null) tmp.font = Font;

            var le = go.AddComponent<LayoutElement>();
            // 텍스트 줄 수에 따라 최소 높이 확보 (1줄=60, 2줄=110, 3줄+=160)
            int lineCount = text.Split('\n').Length;
            le.minHeight = 60 + (lineCount - 1) * 50;
            return tmp;
        }

        static TMP_Text CreateTMP(GameObject parent, string name, string text, float size, Color? color)
            => CreateTMP(parent, name, text, size, FontStyles.Normal, color);

        static GameObject CreateButton(GameObject parent, string name, string label,
            Color bgColor, float width = 700, float height = 120)
        {
            var go = CreateChild(parent, name);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.8f;
            btn.colors = colors;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var textGo = CreateChild(go, "Text");
            StretchFull(textGo);
            // 텍스트 영역에 여백
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.offsetMin = new Vector2(16, 8);
            textRt.offsetMax = new Vector2(-16, -8);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 40;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 28;
            tmp.fontSizeMax = 48;
            if (Font != null) tmp.font = Font;

            return go;
        }

        static GameObject CreateInputField(GameObject parent, string name, string placeholder)
        {
            var go = CreateChild(parent, name);
            go.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f);
            go.AddComponent<LayoutElement>().preferredHeight = 100;

            var textArea = CreateChild(go, "Text Area");
            StretchFull(textArea);
            textArea.GetComponent<RectTransform>().offsetMin = new Vector2(20, 8);
            textArea.GetComponent<RectTransform>().offsetMax = new Vector2(-20, -8);
            textArea.AddComponent<RectMask2D>();

            var textGo = CreateChild(textArea, "Text");
            StretchFull(textGo);
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = 40;
            textTmp.color = TextWhite;
            if (Font != null) textTmp.font = Font;

            var phGo = CreateChild(textArea, "Placeholder");
            StretchFull(phGo);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = 40;
            phTmp.color = TextGray;
            phTmp.fontStyle = FontStyles.Italic;
            if (Font != null) phTmp.font = Font;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = textTmp;
            input.placeholder = phTmp;

            return go;
        }

        static GameObject CreateSlider(GameObject parent, string name)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 14;

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0;
            slider.interactable = false;

            var handle = go.transform.Find("Handle Slide Area");
            if (handle) Object.DestroyImmediate(handle.gameObject);

            var fill = go.transform.Find("Fill Area/Fill");
            if (fill)
            {
                var fillImg = fill.GetComponent<Image>();
                if (fillImg) fillImg.color = BtnBlue;
            }

            return go;
        }
    }
}
#endif
