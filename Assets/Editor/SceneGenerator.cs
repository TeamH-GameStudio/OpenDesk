#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// OpenDesk > Generate Onboarding Scene / Office Scene
    /// 메뉴 클릭 한 번으로 전체 씬 자동 생성
    /// </summary>
    public static class SceneGenerator
    {
        // ═══════════════════════════════════════════════════
        //  색상 상수
        // ═══════════════════════════════════════════════════
        static readonly Color BgDark      = new(0.102f, 0.102f, 0.180f); // #1A1A2E
        static readonly Color PanelDark   = new(0.067f, 0.067f, 0.094f); // #111118
        static readonly Color BarDark     = new(0.165f, 0.165f, 0.165f); // #2A2A2A
        static readonly Color BtnBlue     = new(0.231f, 0.510f, 0.965f); // #3B82F6
        static readonly Color BtnGreen    = new(0.133f, 0.773f, 0.369f); // #22C55E
        static readonly Color BtnRed      = new(0.937f, 0.267f, 0.267f); // #EF4444
        static readonly Color BtnGray     = new(0.267f, 0.267f, 0.267f); // #444
        static readonly Color TextWhite   = Color.white;
        static readonly Color TextGray    = new(0.6f, 0.6f, 0.6f);
        static readonly Color TextDimGray = new(0.4f, 0.4f, 0.4f);

        // ═══════════════════════════════════════════════════
        //  ONBOARDING SCENE
        // ═══════════════════════════════════════════════════

        [MenuItem("OpenDesk/Generate Scenes/Onboarding Scene")]
        public static void GenerateOnboardingScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ── DI Installers ───────────────────────────
            var coreInstaller = new GameObject("CoreInstaller");
            AddComponent(coreInstaller, "OpenDesk.Core.Installers.CoreInstaller");

            var onbInstaller = new GameObject("OnboardingInstaller");
            var onbComp = AddComponent(onbInstaller, "OpenDesk.Onboarding.Installers.OnboardingInstaller");
            // Parent 연결은 수동으로 해야 함 (LifetimeScope 참조)

            // ── Canvas + Scaler ─────────────────────────
            var canvas = CreateCanvas("Canvas", 0);

            // Background
            var bg = CreateImage(canvas, "Background", BgDark);
            StretchFull(bg.GetComponent<RectTransform>());
            bg.GetComponent<Image>().raycastTarget = false;

            // WizardContainer
            var wizard = CreateEmpty(canvas, "WizardContainer");
            SetAnchors(wizard, 0.5f, 0.5f, 0.5f, 0.5f);
            wizard.sizeDelta = new Vector2(800, 600);

            // ProgressBar
            var progressSlider = CreateSlider(wizard.gameObject, "ProgressBar");
            var progressRt = progressSlider.GetComponent<RectTransform>();
            SetTopStretch(progressRt, 8, 0);

            // StepTitle
            var stepTitle = CreateTMP(wizard.gameObject, "StepTitle", "환경 스캔 중...", 24, TextAlignmentOptions.Center);
            var stepTitleRt = stepTitle.GetComponent<RectTransform>();
            SetTopStretch(stepTitleRt, 50, 15);

            // ── 10개 스텝 패널 ──────────────────────────
            var panelArea = new RectOffset(20, 20, 70, 60);

            var scanningPanel       = CreateStepPanel(wizard.gameObject, "ScanningPanel", panelArea, true);
            var installingNodePanel = CreateStepPanel(wizard.gameObject, "InstallingNodePanel", panelArea, false);
            var wsl2Panel           = CreateStepPanel(wizard.gameObject, "Wsl2Panel", panelArea, false);
            var detectingPanel      = CreateStepPanel(wizard.gameObject, "DetectingPanel", panelArea, false);
            var installingClawPanel = CreateStepPanel(wizard.gameObject, "InstallingClawPanel", panelArea, false);
            var gatewayPanel        = CreateStepPanel(wizard.gameObject, "GatewayPanel", panelArea, false);
            var agentsPanel         = CreateStepPanel(wizard.gameObject, "AgentsPanel", panelArea, false);
            var workspacePanel      = CreateStepPanel(wizard.gameObject, "WorkspacePanel", panelArea, false);
            var completePanel       = CreateStepPanel(wizard.gameObject, "CompletePanel", panelArea, false);
            var errorPanel          = CreateStepPanel(wizard.gameObject, "ErrorPanel", panelArea, false);

            // ScanningPanel 내부
            BuildScanningPanel(scanningPanel);

            // InstallingNodePanel 내부
            var nodeTitle = CreateTMP(installingNodePanel, "TitleText", "Node.js 설치 중...", 18);
            var nodeSlider = CreateSlider(installingNodePanel, "InstallProgress");
            var nodeStatus = CreateTMP(installingNodePanel, "InstallStatus", "다운로드 준비 중...", 14, color: TextGray);

            // Wsl2Panel 내부
            CreateTMP(wsl2Panel, "TitleText", "WSL2 확인", 18);
            CreateTMP(wsl2Panel, "StatusText", "WSL2 상태를 확인하고 있습니다...", 14, color: TextGray);

            // DetectingPanel 내부
            CreateTMP(detectingPanel, "TitleText", "OpenClaw 감지 중...", 18);
            CreateTMP(detectingPanel, "StatusText", "설치된 OpenClaw를 찾고 있습니다...", 14, color: TextGray);

            // InstallingClawPanel 내부
            CreateTMP(installingClawPanel, "TitleText", "OpenClaw 설치 중...", 18);
            var clawSlider = CreateSlider(installingClawPanel, "ClawInstallProgress");
            var clawStatus = CreateTMP(installingClawPanel, "ClawInstallStatus", "환경 확인 중...", 14, color: TextGray);

            // GatewayPanel 내부
            BuildGatewayPanel(gatewayPanel);

            // AgentsPanel 내부
            CreateTMP(agentsPanel, "TitleText", "에이전트 감지 완료", 18);
            var agentScroll = CreateScrollView(agentsPanel, "AgentListScroll", 200);
            CreateTMP(agentsPanel, "StatusText", "", 13, color: TextGray);

            // WorkspacePanel 내부
            BuildWorkspacePanel(workspacePanel);

            // CompletePanel 내부
            BuildCompletePanel(completePanel);

            // ErrorPanel 내부
            CreateTMP(errorPanel, "TitleText", "오류 발생", 22, color: BtnRed);
            var errorText = CreateTMP(errorPanel, "ErrorText", "오류 내용이 여기에 표시됩니다.", 14, color: new Color(0.988f, 0.647f, 0.647f));
            CreateTMP(errorPanel, "HelpText", "하단의 '재시도' 버튼을 눌러 다시 시도하세요.", 12, color: TextDimGray);

            // ── Footer Buttons ──────────────────────────
            var footer = CreateEmpty(wizard.gameObject, "FooterButtons");
            SetBottomStretch(footer, 50, 0);
            var footerHlg = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerHlg.spacing = 10;
            footerHlg.childAlignment = TextAnchor.MiddleCenter;
            footerHlg.childForceExpandWidth = true;

            var retryBtn   = CreateButton(footer.gameObject, "RetryButton", "재시도", BtnBlue);
            var offlineBtn = CreateButton(footer.gameObject, "OfflineButton", "오프라인 모드", BtnGray);
            var skipBtn    = CreateButton(footer.gameObject, "SkipButton", "건너뛰기", BtnGray);

            // ── OnboardingUIController ──────────────────
            var uiCtrl = canvas.AddComponent<OnboardingUIControllerSetup>();
            // 직접 연결을 위한 셋업 컴포넌트 — 아래에서 삭제하고 실제 컨트롤러로 교체 필요

            // EventSystem 확인
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            // 저장
            EnsureScenesFolder();
            EditorSceneManager.SaveScene(scene, "Assets/01.Scenes/OnboardingScene.unity");
            Debug.Log("[SceneGen] Onboarding Scene 생성 완료! Assets/01.Scenes/OnboardingScene.unity");
            Debug.Log("[SceneGen] 수동 작업 필요: OnboardingInstaller의 Parent에 CoreInstaller 드래그, OnboardingUIController 컴포넌트 추가 후 Inspector 연결");
        }

        // ═══════════════════════════════════════════════════
        //  OFFICE SCENE
        // ═══════════════════════════════════════════════════

        [MenuItem("OpenDesk/Generate Scenes/Office Scene")]
        public static void GenerateOfficeScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var coreInstaller = new GameObject("CoreInstaller");
            AddComponent(coreInstaller, "OpenDesk.Core.Installers.CoreInstaller");

            // ── Canvas_Main ─────────────────────────────
            var canvas = CreateCanvas("Canvas_Main", 0);

            // 전체 배경
            var rootBg = CreateImage(canvas, "RootBackground", BgDark);
            StretchFull(rootBg.GetComponent<RectTransform>());
            rootBg.GetComponent<Image>().raycastTarget = false;

            // TopBar (높이 56)
            BuildTopBar(canvas);

            // MainContent (TopBar 아래, BottomHUD 위)
            var mainContent = CreateEmpty(canvas, "MainContent");
            SetAnchors(mainContent, 0, 0, 1, 1);
            mainContent.offsetMin = new Vector2(0, 160);  // BottomHUD 높이
            mainContent.offsetMax = new Vector2(0, -56);   // TopBar 높이
            var mainHlg = mainContent.gameObject.AddComponent<HorizontalLayoutGroup>();
            mainHlg.spacing = 4;
            mainHlg.padding = new RectOffset(8, 8, 8, 8);
            mainHlg.childForceExpandHeight = true;

            BuildTerminalPanel(mainContent.gameObject);
            BuildRightPanel(mainContent.gameObject);

            // BottomHUD (높이 160)
            BuildBottomHUD(canvas);

            // ── Canvas_Modal ────────────────────────────
            var modalCanvas = CreateCanvas("Canvas_Modal", 10);
            BuildModals(modalCanvas);

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            EnsureScenesFolder();
            EditorSceneManager.SaveScene(scene, "Assets/01.Scenes/OfficeScene.unity");
            Debug.Log("[SceneGen] Office Scene 생성 완료!");
            Debug.Log("[SceneGen] 수동 작업: 각 컨트롤러 컴포넌트 추가 + Inspector 연결");
        }

        // ═══════════════════════════════════════════════════
        //  빌더 함수 — Onboarding
        // ═══════════════════════════════════════════════════

        static void BuildScanningPanel(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            string[] labels = { "Node.js", "WSL2 (Windows)", "OpenClaw", "Gateway (18789)" };
            string[] names  = { "CheckItem_NodeJs", "CheckItem_WSL2", "CheckItem_OpenClaw", "CheckItem_Gateway" };

            for (int i = 0; i < labels.Length; i++)
            {
                var row = CreateEmpty(parent, names[i]);
                var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 10;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 35;

                var icon = CreateImage(row.gameObject, "StatusIcon", TextGray);
                icon.GetComponent<LayoutElement>().preferredWidth = 30;
                icon.GetComponent<LayoutElement>().preferredHeight = 30;

                var label = CreateTMP(row.gameObject, "Label", labels[i], 16);
                label.GetComponent<LayoutElement>().flexibleWidth = 1;

                var ver = CreateTMP(row.gameObject, "VersionText", "확인 중...", 14, color: TextGray);
                ver.GetComponent<LayoutElement>().preferredWidth = 150;
            }
        }

        static void BuildGatewayPanel(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 15;
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.childAlignment = TextAnchor.MiddleCenter;

            CreateTMP(parent, "TitleText", "Gateway 연결", 18);
            CreateTMP(parent, "DescText", "OpenClaw Gateway에 연결합니다.", 13, color: TextGray);

            var input = CreateInputField(parent, "UrlInputField", "ws://localhost:18789/events");
            var btn = CreateButton(parent, "ConnectButton", "연결", BtnBlue);
            btn.GetComponent<LayoutElement>().preferredWidth = 200;

            CreateTMP(parent, "StatusText", "", 14);
            CreateTMP(parent, "HelpText", "실패 시 '재시도' 또는 '오프라인 모드' 사용", 11, color: TextDimGray);
        }

        static void BuildWorkspacePanel(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 15;
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.childAlignment = TextAnchor.MiddleCenter;

            CreateTMP(parent, "TitleText", "워크스페이스 설정", 18);
            CreateTMP(parent, "DescText", "AI가 작업할 폴더를 선택하세요.", 13, color: TextGray);

            var pathRow = CreateEmpty(parent, "PathRow");
            var phlg = pathRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            phlg.spacing = 8;

            var pathInput = CreateInputField(pathRow.gameObject, "PathInputField", "폴더 경로...");
            pathInput.GetComponent<LayoutElement>().flexibleWidth = 1;

            var browseBtn = CreateButton(pathRow.gameObject, "BrowseButton", "찾아보기", BtnBlue);
            browseBtn.GetComponent<LayoutElement>().preferredWidth = 100;

            var skipBtn = CreateButton(parent, "WorkspaceSkipButton", "건너뛰기 (나중에 설정)", BtnGray);
            skipBtn.GetComponent<LayoutElement>().preferredWidth = 250;
        }

        static void BuildCompletePanel(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 20;
            vlg.padding = new RectOffset(30, 30, 30, 30);
            vlg.childAlignment = TextAnchor.MiddleCenter;

            var icon = CreateImage(parent, "SuccessIcon", BtnGreen);
            icon.GetComponent<LayoutElement>().preferredWidth = 80;
            icon.GetComponent<LayoutElement>().preferredHeight = 80;

            CreateTMP(parent, "TitleText", "설정 완료!", 28, FontStyles.Bold, BtnGreen);
            CreateTMP(parent, "MessageText", "OpenDesk가 준비되었습니다.\nAI 에이전트와 함께 작업할 수 있습니다.", 15, color: new Color(0.87f, 0.87f, 0.87f));

            var enterBtn = CreateButton(parent, "EnterButton", "시작하기", BtnBlue);
            enterBtn.GetComponent<LayoutElement>().preferredWidth = 250;
            enterBtn.GetComponent<LayoutElement>().preferredHeight = 50;
            var enterTmp = enterBtn.GetComponentInChildren<TMP_Text>();
            if (enterTmp) { enterTmp.fontSize = 18; enterTmp.fontStyle = FontStyles.Bold; }
        }

        // ═══════════════════════════════════════════════════
        //  빌더 함수 — Office
        // ═══════════════════════════════════════════════════

        static void BuildTopBar(GameObject canvas)
        {
            var topBar = CreateEmpty(canvas, "TopBar");
            SetAnchors(topBar, 0, 1, 1, 1);
            topBar.offsetMin = new Vector2(0, -56);
            topBar.offsetMax = Vector2.zero;

            var bgImg = topBar.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.09f, 0.09f, 0.13f); // 더 진한 헤더

            var hlg = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.padding = new RectOffset(24, 24, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;

            CreateTMP(topBar.gameObject, "TitleText", "OpenDesk", 24, FontStyles.Bold);

            var spacer = CreateEmpty(topBar.gameObject, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // 연결 상태 표시 (아이콘 + 텍스트)
            var statusIcon = CreateImage(topBar.gameObject, "StatusIcon", Color.gray);
            var iconLe = statusIcon.GetComponent<LayoutElement>();
            iconLe.preferredWidth = 14; iconLe.preferredHeight = 14;

            CreateTMP(topBar.gameObject, "StatusText", "연결 끊김", 16, color: TextGray);

            var settingsBtn = CreateButton(topBar.gameObject, "SettingsButton", "설정", BtnGray);
            settingsBtn.GetComponent<LayoutElement>().preferredWidth = 80;
            settingsBtn.GetComponent<LayoutElement>().preferredHeight = 36;
        }

        static void BuildTerminalPanel(GameObject parent)
        {
            var panel = CreateEmpty(parent, "TerminalChatPanel");
            var panelLe = panel.gameObject.AddComponent<LayoutElement>();
            panelLe.flexibleWidth = 0.4f;
            panelLe.minWidth = 400;

            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.color = new Color(0.075f, 0.075f, 0.10f); // 채팅 배경

            var pvlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            pvlg.spacing = 8;
            pvlg.padding = new RectOffset(12, 12, 12, 12);

            // ── 헤더 ────────────────────────────────────
            var header = CreateEmpty(panel.gameObject, "Header");
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var hhlg = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            hhlg.spacing = 12;
            hhlg.childAlignment = TextAnchor.MiddleLeft;
            hhlg.childForceExpandWidth = false;

            CreateTMP(header.gameObject, "Title", "터미널", 20, FontStyles.Bold);

            // 세션 드롭다운 자리 (코드에서 TMP_Dropdown 추가)
            var dropdown = CreateEmpty(header.gameObject, "SessionDropdown");
            var ddImg = dropdown.gameObject.AddComponent<Image>();
            ddImg.color = BtnGray;
            var ddLe = dropdown.gameObject.AddComponent<LayoutElement>();
            ddLe.preferredWidth = 120;
            ddLe.preferredHeight = 32;
            CreateTMP(dropdown.gameObject, "Label", "main", 14, TextAlignmentOptions.Center);

            var clearBtn = CreateButton(header.gameObject, "ClearBtn", "초기화", BtnGray);
            clearBtn.GetComponent<LayoutElement>().preferredHeight = 32;
            clearBtn.GetComponent<LayoutElement>().preferredWidth = 80;

            // ── 채팅 영역 ───────────────────────────────
            var scroll = CreateScrollView(panel.gameObject, "ChatScroll", 0);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            scroll.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.5f);

            // ── 타이핑 인디케이터 ────────────────────────
            var typing = CreateEmpty(panel.gameObject, "TypingIndicator");
            typing.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            CreateTMP(typing.gameObject, "TypingText", "에이전트가 사고 중...", 14, FontStyles.Italic, TextGray);
            typing.gameObject.SetActive(false);

            // 최신으로 스크롤 버튼
            var scrollBtn = CreateButton(panel.gameObject, "ScrollToBottomBtn", "최신 메시지로 이동", BtnBlue);
            scrollBtn.GetComponent<LayoutElement>().preferredHeight = 32;
            scrollBtn.SetActive(false);

            // ── 입력 영역 ───────────────────────────────
            var inputArea = CreateEmpty(panel.gameObject, "InputArea");
            inputArea.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            var iahlg = inputArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            iahlg.spacing = 8;
            iahlg.childAlignment = TextAnchor.MiddleCenter;

            var chatInput = CreateInputField(inputArea.gameObject, "ChatInput", "명령을 입력하세요... (Enter: 전송, Shift+Enter: 줄바꿈)");
            chatInput.GetComponent<LayoutElement>().flexibleWidth = 1;
            chatInput.GetComponent<LayoutElement>().preferredHeight = 48;

            var sendBtn = CreateButton(inputArea.gameObject, "SendBtn", "전송", BtnBlue);
            sendBtn.GetComponent<LayoutElement>().preferredWidth = 80;
            sendBtn.GetComponent<LayoutElement>().preferredHeight = 48;
            var sendTmp = sendBtn.GetComponentInChildren<TMP_Text>();
            if (sendTmp) { sendTmp.fontSize = 16; sendTmp.fontStyle = FontStyles.Bold; }
        }

        static void BuildRightPanel(GameObject parent)
        {
            var right = CreateEmpty(parent, "RightPanel");
            right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.6f;
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.085f, 0.085f, 0.11f); // 패널 배경

            var rvlg = right.gameObject.AddComponent<VerticalLayoutGroup>();
            rvlg.spacing = 0;
            rvlg.padding = new RectOffset(0, 0, 0, 0);

            // ── 탭 바 ──────────────────────────────────
            var tabBar = CreateEmpty(right.gameObject, "TabBar");
            tabBar.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            var tabBg = tabBar.gameObject.AddComponent<Image>();
            tabBg.color = new Color(0.07f, 0.07f, 0.09f);

            var tabHlg = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabHlg.spacing = 0;
            tabHlg.childForceExpandWidth = true;
            tabHlg.childForceExpandHeight = true;

            string[] tabNames = { "채널", "API 키", "라우팅", "스킬", "보안", "설정" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                var tab = CreateButton(tabBar.gameObject, $"Tab{i}", tabNames[i],
                    i == 0 ? BtnBlue : new Color(0.12f, 0.12f, 0.15f));
                tab.GetComponent<LayoutElement>().preferredHeight = 48;
                var tabTmp = tab.GetComponentInChildren<TMP_Text>();
                if (tabTmp) { tabTmp.fontSize = 15; }
            }

            // ── 탭 컨텐츠 ──────────────────────────────
            var tabContent = CreateEmpty(right.gameObject, "TabContent");
            tabContent.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            string[] panelNames  = { "ChannelsPanel", "ApiKeysPanel", "RoutingPanel", "SkillsPanel", "SecurityPanel", "SettingsPanel" };
            string[] panelTitles = { "채널 연동", "API 키 관리", "라우팅 모드", "스킬 마켓", "보안 감사", "설정" };
            string[] panelDescs  = { "Telegram, Discord 등 메신저를 연결합니다",
                                     "14개+ AI 제공업체 API 키를 안전하게 관리합니다",
                                     "AI 모델 비용을 자동으로 최적화합니다",
                                     "에이전트 스킬을 검색하고 설치합니다",
                                     "시스템 보안 상태를 점검하고 자동 수정합니다",
                                     "Gateway URL 및 환경 설정을 변경합니다" };

            for (int i = 0; i < panelNames.Length; i++)
            {
                var p = CreateEmpty(tabContent.gameObject, panelNames[i]);
                SetAnchors(p, 0, 0, 1, 1);
                p.offsetMin = Vector2.zero;
                p.offsetMax = Vector2.zero;

                var pvlg2 = p.gameObject.AddComponent<VerticalLayoutGroup>();
                pvlg2.spacing = 12;
                pvlg2.padding = new RectOffset(20, 20, 16, 16);
                pvlg2.childForceExpandHeight = false;

                CreateTMP(p.gameObject, "Title", panelTitles[i], 22, FontStyles.Bold);
                CreateTMP(p.gameObject, "Description", panelDescs[i], 15, color: TextGray);

                // 구분선
                var divider = CreateImage(p.gameObject, "Divider", new Color(0.2f, 0.2f, 0.25f));
                divider.GetComponent<LayoutElement>().preferredHeight = 1;

                // 컨텐츠 스크롤 영역
                var container = CreateScrollView(p.gameObject, "Container", 0);
                container.GetComponent<LayoutElement>().flexibleHeight = 1;

                if (panelNames[i] == "RoutingPanel")  BuildRoutingContent(p.gameObject);
                if (panelNames[i] == "SecurityPanel") BuildSecurityContent(p.gameObject);
                if (panelNames[i] == "SettingsPanel") BuildSettingsContent(p.gameObject);

                p.gameObject.SetActive(i == 0);
            }
        }

        static void BuildRoutingContent(GameObject parent)
        {
            string[] modes = { "Free (무료 — Ollama 로컬)", "Eco (~$8/월)", "Auto (~$35/월)", "Premium (~$150/월)" };
            string[] descs = { "API 키 없이 무료 사용", "단순 작업은 저가, 복잡한 작업만 중급", "난이도에 따라 자동 분배", "항상 최고 성능 모델 사용" };

            for (int i = 0; i < modes.Length; i++)
            {
                var row = CreateEmpty(parent, $"Mode{i}");
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
                var rowBg = row.gameObject.AddComponent<Image>();
                rowBg.color = i == 0 ? new Color(0.1f, 0.2f, 0.15f, 0.5f) : new Color(0.1f, 0.1f, 0.12f, 0.5f);

                var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 12;
                hlg.padding = new RectOffset(16, 16, 8, 8);
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;

                var toggleGo = new GameObject("Toggle");
                toggleGo.transform.SetParent(row, false);
                toggleGo.AddComponent<RectTransform>();
                toggleGo.AddComponent<Toggle>().isOn = (i == 0);
                toggleGo.AddComponent<LayoutElement>().preferredWidth = 30;

                var textCol = CreateEmpty(row.gameObject, "TextCol");
                textCol.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 2;
                textCol.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

                CreateTMP(textCol.gameObject, "ModeLabel", modes[i], 16, FontStyles.Bold);
                CreateTMP(textCol.gameObject, "ModeDesc", descs[i], 13, color: TextGray);
            }

            var costText = CreateTMP(parent, "EstimatedCost", "예상 월 비용: 무료", 18, FontStyles.Bold, BtnGreen);
            costText.GetComponent<LayoutElement>().preferredHeight = 40;

            var applyBtn = CreateButton(parent, "ApplyButton", "적용", BtnBlue);
            applyBtn.GetComponent<LayoutElement>().preferredHeight = 44;
            var applyTmp = applyBtn.GetComponentInChildren<TMP_Text>();
            if (applyTmp) { applyTmp.fontSize = 16; applyTmp.fontStyle = FontStyles.Bold; }
        }

        static void BuildSecurityContent(GameObject parent)
        {
            var btnRow = CreateEmpty(parent, "ButtonRow");
            var hlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            btnRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;

            var qBtn = CreateButton(btnRow.gameObject, "QuickScanBtn", "빠른 스캔", BtnBlue);
            qBtn.GetComponent<LayoutElement>().preferredHeight = 44;
            qBtn.GetComponentInChildren<TMP_Text>().fontSize = 15;

            var dBtn = CreateButton(btnRow.gameObject, "DeepScanBtn", "심층 스캔", new Color(0.15f, 0.35f, 0.65f));
            dBtn.GetComponent<LayoutElement>().preferredHeight = 44;
            dBtn.GetComponentInChildren<TMP_Text>().fontSize = 15;

            var fixBtn = CreateButton(btnRow.gameObject, "AutoFixBtn", "자동 수정", BtnRed);
            fixBtn.GetComponent<LayoutElement>().preferredHeight = 44;
            fixBtn.GetComponentInChildren<TMP_Text>().fontSize = 15;
            fixBtn.SetActive(false);

            var progress = CreateSlider(parent, "ProgressSlider");
            progress.SetActive(false);

            CreateTMP(parent, "SummaryText", "스캔을 실행하세요", 16, color: TextGray);
        }

        static void BuildSettingsContent(GameObject parent)
        {
            // Gateway 섹션
            CreateTMP(parent, "GatewayLabel", "Gateway URL", 16, FontStyles.Bold);
            var urlInput = CreateInputField(parent, "GatewayUrlInput", "ws://localhost:18789/events");
            urlInput.GetComponent<LayoutElement>().preferredHeight = 42;

            var saveRow = CreateEmpty(parent, "SaveRow");
            var shlg = saveRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            shlg.spacing = 12;
            saveRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;

            var saveBtn = CreateButton(saveRow.gameObject, "GatewaySaveBtn", "저장", BtnBlue);
            saveBtn.GetComponent<LayoutElement>().preferredWidth = 100;
            CreateTMP(saveRow.gameObject, "GatewayStatus", "", 14, color: TextGray);

            // 구분
            var div = CreateImage(parent, "Divider2", new Color(0.2f, 0.2f, 0.25f));
            div.GetComponent<LayoutElement>().preferredHeight = 1;

            // 로그 섹션
            CreateTMP(parent, "LogLabel", "로그 레벨", 16, FontStyles.Bold);
            var clearBtn = CreateButton(parent, "ClearLogsBtn", "로그 초기화", BtnGray);
            clearBtn.GetComponent<LayoutElement>().preferredHeight = 40;

            // 디버그 섹션
            var div2 = CreateImage(parent, "Divider3", new Color(0.2f, 0.2f, 0.25f));
            div2.GetComponent<LayoutElement>().preferredHeight = 1;

            CreateTMP(parent, "DebugLabel", "디버그 (에디터 전용)", 16, FontStyles.Bold);
            var debugInput = CreateInputField(parent, "DebugSessionInput", "main");
            debugInput.GetComponent<LayoutElement>().preferredHeight = 42;
            var applyBtn = CreateButton(parent, "ApplyStateBtn", "상태 강제 전환", BtnGray);
            applyBtn.GetComponent<LayoutElement>().preferredHeight = 40;
        }

        static void BuildBottomHUD(GameObject canvas)
        {
            var hud = CreateEmpty(canvas, "BottomHUD");
            SetAnchors(hud, 0, 0, 1, 0);
            hud.offsetMin = Vector2.zero;
            hud.offsetMax = new Vector2(0, 160);

            var hudImg = hud.gameObject.AddComponent<Image>();
            hudImg.color = new Color(0.065f, 0.065f, 0.085f);

            var hudHlg = hud.gameObject.AddComponent<HorizontalLayoutGroup>();
            hudHlg.spacing = 8;
            hudHlg.padding = new RectOffset(12, 12, 8, 8);

            // Loop Panel
            var loopPanel = CreateEmpty(hud.gameObject, "LoopPanel");
            loopPanel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.3f;
            BuildLoopNodes(loopPanel.gameObject);

            // Cost Panel
            var costPanel = CreateEmpty(hud.gameObject, "CostPanel");
            costPanel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.3f;
            BuildCostHud(costPanel.gameObject);

            // Console Panel
            var consolePanel = CreateEmpty(hud.gameObject, "ConsolePanel");
            consolePanel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 0.4f;
            BuildConsole(consolePanel.gameObject);
        }

        static void BuildLoopNodes(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(8, 8, 6, 6);

            CreateTMP(parent, "SessionText", "에이전틱 루프", 14, FontStyles.Bold);

            var nodeRow = CreateEmpty(parent, "NodeContainer");
            var nhlg = nodeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            nhlg.spacing = 4;
            nhlg.childAlignment = TextAnchor.MiddleCenter;
            nodeRow.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            string[] nodeNames = { "사고", "계획", "실행", "검토", "완료" };
            for (int i = 0; i < nodeNames.Length; i++)
            {
                if (i > 0)
                {
                    var line = CreateImage(nodeRow.gameObject, $"Line{i - 1}", TextDimGray);
                    var lineLe = line.GetComponent<LayoutElement>();
                    lineLe.preferredWidth = 16; lineLe.preferredHeight = 3;
                }

                var node = CreateEmpty(nodeRow.gameObject, $"Node_{nodeNames[i]}");
                node.gameObject.AddComponent<LayoutElement>().preferredWidth = 64;
                var nvlg = node.gameObject.AddComponent<VerticalLayoutGroup>();
                nvlg.childAlignment = TextAnchor.MiddleCenter;
                nvlg.spacing = 4;

                var bg = CreateImage(node.gameObject, "Background", new Color(0.15f, 0.15f, 0.2f));
                bg.GetComponent<LayoutElement>().preferredHeight = 48;
                bg.GetComponent<LayoutElement>().preferredWidth = 60;

                CreateTMP(node.gameObject, "Label", nodeNames[i], 13, TextAlignmentOptions.Center, TextGray);
            }

            CreateTMP(parent, "StatusText", "대기 중", 14, color: TextGray);
        }

        static void BuildCostHud(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(12, 12, 6, 6);

            CreateTMP(parent, "CostTitle", "비용 / 리소스", 14, FontStyles.Bold);

            // 비용 바
            var costRow = CreateEmpty(parent, "CostRow");
            var crHlg = costRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            crHlg.spacing = 10;
            crHlg.childAlignment = TextAnchor.MiddleLeft;
            costRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

            CreateTMP(costRow.gameObject, "CostLabel", "API", 13, color: TextGray);
            var costSlider = CreateSlider(costRow.gameObject, "CostSlider");
            costSlider.GetComponent<LayoutElement>().flexibleWidth = 1;
            CreateTMP(costRow.gameObject, "CostText", "$0.00", 14, FontStyles.Bold);

            // 토큰 정보
            var tokenRow = CreateEmpty(parent, "TokenRow");
            var trHlg = tokenRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            trHlg.spacing = 20;
            tokenRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

            CreateTMP(tokenRow.gameObject, "TokensUsed", "0 토큰 사용", 13, color: TextGray);
            CreateTMP(tokenRow.gameObject, "TokensSaved", "0 토큰 절약", 13, color: BtnGreen);

            // CPU / RAM
            var resRow = CreateEmpty(parent, "ResourceRow");
            var rrHlg = resRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rrHlg.spacing = 12;
            rrHlg.childAlignment = TextAnchor.MiddleLeft;
            resRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 24;

            CreateTMP(resRow.gameObject, "CpuText", "CPU 0%", 13, color: TextGray);
            CreateSlider(resRow.gameObject, "CpuSlider").GetComponent<LayoutElement>().flexibleWidth = 0.5f;
            CreateTMP(resRow.gameObject, "RamText", "RAM 0MB", 13, color: TextGray);
            CreateSlider(resRow.gameObject, "RamSlider").GetComponent<LayoutElement>().flexibleWidth = 0.5f;

            // 경고 패널
            var alert = CreateEmpty(parent, "AlertPanel");
            alert.gameObject.AddComponent<Image>().color = new Color(0.4f, 0.1f, 0.1f, 0.8f);
            alert.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            CreateTMP(alert.gameObject, "AlertText", "", 14, FontStyles.Bold, BtnRed, TextAlignmentOptions.Center);
            alert.gameObject.SetActive(false);
        }

        static void BuildConsole(GameObject parent)
        {
            var vlg = parent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(8, 8, 4, 4);

            // ── 타이틀 바 ───────────────────────────────
            var titleBar = CreateEmpty(parent, "TitleBar");
            titleBar.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
            var thlg = titleBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            thlg.spacing = 6;
            thlg.childAlignment = TextAnchor.MiddleLeft;
            thlg.childForceExpandWidth = false;

            var toggleBtn = CreateButton(titleBar.gameObject, "ToggleBtn", "콘솔", BtnGray);
            toggleBtn.GetComponent<LayoutElement>().preferredWidth = 70;
            toggleBtn.GetComponentInChildren<TMP_Text>().fontSize = 14;

            var spacer = CreateEmpty(titleBar.gameObject, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            string[] filterNames = { "전체", "Info", "Warn", "Error" };
            string[] filterObjNames = { "FilterAllBtn", "FilterInfoBtn", "FilterWarnBtn", "FilterErrorBtn" };
            for (int i = 0; i < filterNames.Length; i++)
            {
                var fb = CreateButton(titleBar.gameObject, filterObjNames[i], filterNames[i], BtnGray);
                fb.GetComponent<LayoutElement>().preferredWidth = 60;
                fb.GetComponentInChildren<TMP_Text>().fontSize = 12;
            }

            var clearBtn = CreateButton(titleBar.gameObject, "ClearBtn", "Clear", new Color(0.35f, 0.15f, 0.15f));
            clearBtn.GetComponent<LayoutElement>().preferredWidth = 60;
            clearBtn.GetComponentInChildren<TMP_Text>().fontSize = 12;

            // ── 로그 영역 ───────────────────────────────
            var contentArea = CreateEmpty(parent, "ContentArea");
            contentArea.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var scroll = CreateScrollView(contentArea.gameObject, "LogScroll", 0);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            scroll.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 0.8f);

            // LogText (Content 안에 생성)
            var content = scroll.transform.Find("Viewport/Content");
            if (content != null)
            {
                var logText = CreateTMP(content.gameObject, "LogText", "", 13);
                logText.enableWordWrapping = true;
                logText.richText = true;
                logText.alignment = TextAlignmentOptions.TopLeft;
                logText.GetComponent<LayoutElement>().flexibleWidth = 1;
            }
        }

        static void BuildModals(GameObject canvas)
        {
            // ConfirmDialog
            var confirm = CreateEmpty(canvas, "ConfirmDialog");
            StretchFull(confirm);
            var confirmBg = confirm.gameObject.AddComponent<Image>();
            confirmBg.color = new Color(0, 0, 0, 0.6f);

            var confirmWindow = CreateEmpty(confirm.gameObject, "Window");
            SetAnchors(confirmWindow, 0.5f, 0.5f, 0.5f, 0.5f);
            confirmWindow.sizeDelta = new Vector2(400, 200);
            var cwImg = confirmWindow.gameObject.AddComponent<Image>();
            cwImg.color = BarDark;
            var cwVlg = confirmWindow.gameObject.AddComponent<VerticalLayoutGroup>();
            cwVlg.spacing = 10; cwVlg.padding = new RectOffset(20, 20, 20, 20);

            CreateTMP(confirmWindow.gameObject, "TitleText", "확인", 18, FontStyles.Bold);
            CreateTMP(confirmWindow.gameObject, "MessageText", "진행하시겠습니까?", 14);
            var btnRow = CreateEmpty(confirmWindow.gameObject, "ButtonRow");
            btnRow.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 10;
            CreateButton(btnRow.gameObject, "ConfirmBtn", "확인", BtnBlue);
            CreateButton(btnRow.gameObject, "CancelBtn", "취소", BtnGray);

            confirm.gameObject.SetActive(false);

            // ErrorDialog
            var error = CreateEmpty(canvas, "ErrorDialog");
            StretchFull(error);
            var errorBg = error.gameObject.AddComponent<Image>();
            errorBg.color = new Color(0, 0, 0, 0.6f);

            var errorWindow = CreateEmpty(error.gameObject, "Window");
            SetAnchors(errorWindow, 0.5f, 0.5f, 0.5f, 0.5f);
            errorWindow.sizeDelta = new Vector2(400, 250);
            var ewImg = errorWindow.gameObject.AddComponent<Image>();
            ewImg.color = BarDark;
            var ewVlg = errorWindow.gameObject.AddComponent<VerticalLayoutGroup>();
            ewVlg.spacing = 10; ewVlg.padding = new RectOffset(20, 20, 20, 20);

            CreateTMP(errorWindow.gameObject, "TitleText", "오류", 18, FontStyles.Bold, BtnRed);
            CreateTMP(errorWindow.gameObject, "MessageText", "오류 내용", 14);
            CreateButton(errorWindow.gameObject, "CloseBtn", "닫기", BtnGray);

            error.gameObject.SetActive(false);
        }

        // ═══════════════════════════════════════════════════
        //  유틸리티
        // ═══════════════════════════════════════════════════

        static GameObject CreateCanvas(string name, int sortOrder)
        {
            var canvasGo = new GameObject(name);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            return canvasGo;
        }

        static RectTransform CreateEmpty(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            return rt;
        }

        static RectTransform CreateEmpty(RectTransform parent, string name)
        {
            return CreateEmpty(parent.gameObject, name);
        }

        static GameObject CreateImage(GameObject parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            go.AddComponent<LayoutElement>();
            return go;
        }

        // NotoSansKR 폰트 캐시
        private static TMP_FontAsset _notoSansKR;
        static TMP_FontAsset NotoSansKR
        {
            get
            {
                if (_notoSansKR == null)
                    _notoSansKR = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                        "Assets/NotoSansKR-VariableFont_wght.asset");
                return _notoSansKR;
            }
        }

        static void ApplyFont(TMP_Text tmp)
        {
            if (NotoSansKR != null)
                tmp.font = NotoSansKR;
        }

        static TMP_Text CreateTMP(GameObject parent, string name, string text, float fontSize,
            FontStyles style = FontStyles.Normal, Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color ?? TextWhite;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = true;
            ApplyFont(tmp);
            go.AddComponent<LayoutElement>();
            return tmp;
        }

        static TMP_Text CreateTMP(GameObject parent, string name, string text, float fontSize,
            TextAlignmentOptions alignment, Color? color = null)
        {
            return CreateTMP(parent, name, text, fontSize, FontStyles.Normal, color, alignment);
        }

        static GameObject CreateButton(GameObject parent, string name, string label, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.8f;
            btn.colors = colors;

            go.AddComponent<LayoutElement>().preferredHeight = 30;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            StretchFull(textRt);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.color = TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            ApplyFont(tmp);

            return go;
        }

        static GameObject CreateSlider(GameObject parent, string name)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<LayoutElement>().preferredHeight = 20;

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0;
            slider.interactable = false;

            // Handle 삭제
            var handle = go.transform.Find("Handle Slide Area");
            if (handle) Object.DestroyImmediate(handle.gameObject);

            // Fill 색상 변경
            var fill = go.transform.Find("Fill Area/Fill");
            if (fill)
            {
                var fillImg = fill.GetComponent<Image>();
                if (fillImg) fillImg.color = BtnBlue;
            }

            return go;
        }

        static GameObject CreateInputField(GameObject parent, string name, string placeholder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.10f);

            go.AddComponent<LayoutElement>().preferredHeight = 35;

            // TMP_InputField는 Text Area, Text, Placeholder 자식이 필요
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var taRt = textArea.AddComponent<RectTransform>();
            StretchFull(taRt, 8);
            textArea.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textArea.transform, false);
            var tRt = textGo.AddComponent<RectTransform>();
            StretchFull(tRt);
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = 13;
            textTmp.color = TextWhite;
            ApplyFont(textTmp);

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(textArea.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            StretchFull(phRt);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = 13;
            ApplyFont(phTmp);
            phTmp.color = TextDimGray;
            phTmp.fontStyle = FontStyles.Italic;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRt;
            input.textComponent = textTmp;
            input.placeholder = phTmp;

            return go;
        }

        static GameObject CreateScrollView(GameObject parent, string name, float height)
        {
            var go = DefaultControls.CreateScrollView(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent.transform, false);

            // 배경 투명
            var bgImg = go.GetComponent<Image>();
            if (bgImg) bgImg.color = new Color(0, 0, 0, 0.2f);

            var le = go.AddComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;

            // Content 설정
            var content = go.transform.Find("Viewport/Content");
            if (content)
            {
                var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 4;
                vlg.padding = new RectOffset(4, 4, 4, 4);
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                var csf = content.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // Horizontal Scrollbar 삭제
            var hScroll = go.transform.Find("Scrollbar Horizontal");
            if (hScroll) Object.DestroyImmediate(hScroll.gameObject);
            var scrollRect = go.GetComponent<ScrollRect>();
            if (scrollRect) scrollRect.horizontal = false;

            return go;
        }

        // Anchor/Stretch 유틸리티
        static void StretchFull(RectTransform rt, float padding = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        static void SetAnchors(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void SetTopStretch(RectTransform rt, float height, float topOffset)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, -topOffset - height);
            rt.offsetMax = new Vector2(0, -topOffset);
        }

        static void SetBottomStretch(RectTransform rt, float height, float bottomOffset)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.offsetMin = new Vector2(0, bottomOffset);
            rt.offsetMax = new Vector2(0, bottomOffset + height);
        }

        static GameObject CreateStepPanel(GameObject parent, string name, RectOffset area, bool active)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(area.left, area.bottom);
            rt.offsetMax = new Vector2(-area.right, -area.top);
            go.SetActive(active);
            return go;
        }

        static Component AddComponent(GameObject go, string typeName)
        {
            var type = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (type != null)
                return go.AddComponent(type);

            Debug.LogWarning($"[SceneGen] 컴포넌트 타입 미발견 (컴파일 후 수동 추가): {typeName}");
            return null;
        }

        static void EnsureScenesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/01.Scenes"))
                AssetDatabase.CreateFolder("Assets", "01.Scenes");
        }

        // 임시 마커 컴포넌트 (실제 컨트롤러 연결 전 플레이스홀더)
        class OnboardingUIControllerSetup : MonoBehaviour { }
    }
}
#endif
