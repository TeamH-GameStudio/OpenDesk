#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// 기존 Onboarding 씬에 누락된 UI 요소만 추가하는 패치 도구
    /// 이미 있는 요소는 건드리지 않음
    ///
    /// 사용: OpenDesk > Patch Onboarding Scene
    /// </summary>
    public static class OnboardingScenePatcher
    {
        static readonly Color BtnGreen  = new(0.133f, 0.773f, 0.369f);
        static readonly Color BtnGray   = new(0.267f, 0.267f, 0.267f);
        static readonly Color BtnOrange = new(0.85f, 0.55f, 0.1f);
        static readonly Color TextGray  = new(0.6f, 0.6f, 0.6f);
        static readonly Color TextWhite = Color.white;

        [MenuItem("OpenDesk/Patch Onboarding Scene (추가분만)")]
        public static void PatchOnboardingScene()
        {
            int added = 0;

            // ── WizardContainer 찾기 ──────────────────────────
            var wizard = GameObject.Find("WizardContainer");
            if (wizard == null)
            {
                Debug.LogError("[Patcher] WizardContainer를 찾을 수 없습니다. Onboarding 씬을 먼저 열어주세요.");
                return;
            }

            // ── 1. NodeUpgradePanel ───────────────────────────
            if (wizard.transform.Find("NodeUpgradePanel") == null)
            {
                var panel = CreateStepPanel(wizard, "NodeUpgradePanel");
                var vlg = panel.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 14;
                vlg.padding = new RectOffset(20, 20, 10, 10);
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                CreateTMP(panel, "NodeVersionText",
                    "현재 설치된 버전: 확인 중...", 22, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.4f));
                var projList = CreateTMP(panel, "NodeProjectListText",
                    "프로젝트 스캔 중...", 16, TextAlignmentOptions.Left, TextGray);
                projList.GetComponent<LayoutElement>().preferredHeight = 100;

                var safeBtn = CreateButton(panel, "NodeSafeInstallButton",
                    "안전하게 설치 (추천)\n기존 프로그램에 영향 없음", BtnGreen);
                safeBtn.GetComponent<LayoutElement>().preferredHeight = 100;

                var overBtn = CreateButton(panel, "NodeOverwriteButton",
                    "기존 버전 업그레이드\n기존 프로그램에 영향 있을 수 있음", BtnOrange);
                overBtn.GetComponent<LayoutElement>().preferredHeight = 100;

                var skipBtn = CreateButton(panel, "NodeSkipButton",
                    "건너뛰기\nAI 비서 일부 기능이 제한될 수 있음", BtnGray);
                skipBtn.GetComponent<LayoutElement>().preferredHeight = 80;

                panel.SetActive(false);
                added++;
                Debug.Log("[Patcher] NodeUpgradePanel 추가 완료");
            }

            // ── 2. Wsl2Panel 내부: RebootNowButton / RebootLaterButton ──
            var wsl2Panel = wizard.transform.Find("Wsl2Panel");
            if (wsl2Panel != null)
            {
                if (wsl2Panel.Find("RebootNowButton") == null)
                {
                    var btn = CreateButton(wsl2Panel.gameObject, "RebootNowButton", "지금 재시작", new Color(0.231f, 0.510f, 0.965f));
                    btn.GetComponent<LayoutElement>().preferredWidth = 400;
                    btn.GetComponent<LayoutElement>().preferredHeight = 100;
                    btn.SetActive(false);
                    added++;
                    Debug.Log("[Patcher] RebootNowButton 추가 완료");
                }
                if (wsl2Panel.Find("RebootLaterButton") == null)
                {
                    var btn = CreateButton(wsl2Panel.gameObject, "RebootLaterButton", "나중에 할게요", BtnGray);
                    btn.GetComponent<LayoutElement>().preferredWidth = 400;
                    btn.GetComponent<LayoutElement>().preferredHeight = 100;
                    btn.SetActive(false);
                    added++;
                    Debug.Log("[Patcher] RebootLaterButton 추가 완료");
                }
            }

            // ── 3. ErrorPanel 내부: ErrorDetailToggle / ErrorDetailPanel ──
            var errorPanel = wizard.transform.Find("ErrorPanel");
            if (errorPanel != null)
            {
                if (errorPanel.Find("ErrorDetailToggle") == null)
                {
                    var btn = CreateButton(errorPanel.gameObject, "ErrorDetailToggle", "상세 보기", BtnGray);
                    btn.GetComponent<LayoutElement>().preferredWidth = 300;
                    btn.GetComponent<LayoutElement>().preferredHeight = 100;
                    added++;
                    Debug.Log("[Patcher] ErrorDetailToggle 추가 완료");
                }
                if (errorPanel.Find("ErrorDetailPanel") == null)
                {
                    var go = new GameObject("ErrorDetailPanel");
                    go.transform.SetParent(errorPanel, false);
                    go.AddComponent<RectTransform>();
                    go.AddComponent<LayoutElement>().preferredHeight = 100;
                    go.SetActive(false);
                    added++;
                    Debug.Log("[Patcher] ErrorDetailPanel 추가 완료");
                }
            }

            // ── 4. WorkspacePanel 내부: ConfirmButton ──
            var workspacePanel = wizard.transform.Find("WorkspacePanel");
            if (workspacePanel != null && workspacePanel.Find("ConfirmButton") == null)
            {
                var btn = CreateButton(workspacePanel.gameObject, "ConfirmButton", "이 폴더로 시작하기", BtnGreen);
                btn.GetComponent<LayoutElement>().preferredWidth = 500;
                btn.GetComponent<LayoutElement>().preferredHeight = 100;
                added++;
                Debug.Log("[Patcher] WorkspacePanel > ConfirmButton 추가 완료");
            }

            // ── 5. DescriptionArea (공통 설명 영역) ──
            if (wizard.transform.Find("DescriptionArea") == null)
            {
                var descArea = new GameObject("DescriptionArea");
                descArea.transform.SetParent(wizard.transform, false);
                var rt = descArea.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.offsetMin = new Vector2(30, 80);
                rt.offsetMax = new Vector2(-30, 280);
                var vlg = descArea.AddComponent<VerticalLayoutGroup>();
                vlg.spacing = 10;
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                var desc = CreateTMP(descArea, "DescriptionText", "", 22, TextAlignmentOptions.Center, new Color(0.85f, 0.85f, 0.9f));
                desc.GetComponent<LayoutElement>().preferredHeight = 80;
                CreateTMP(descArea, "EstimatedTimeText", "", 18, TextAlignmentOptions.Center, TextGray);

                var whyBtn = CreateButton(descArea, "WhyNeededToggle", "왜 필요한가요?", new Color(0.2f, 0.2f, 0.3f));
                whyBtn.GetComponent<LayoutElement>().preferredWidth = 440;
                whyBtn.GetComponent<LayoutElement>().preferredHeight = 80;

                var whyPanel = new GameObject("WhyNeededPanel");
                whyPanel.transform.SetParent(descArea.transform, false);
                whyPanel.AddComponent<RectTransform>();
                var whyImg = whyPanel.AddComponent<Image>();
                whyImg.color = new Color(0.12f, 0.12f, 0.18f);
                whyPanel.AddComponent<LayoutElement>();
                var whyVlg = whyPanel.AddComponent<VerticalLayoutGroup>();
                whyVlg.padding = new RectOffset(16, 16, 12, 12);
                CreateTMP(whyPanel, "WhyNeededText", "", 17, TextAlignmentOptions.Left, new Color(0.7f, 0.7f, 0.8f));
                whyPanel.SetActive(false);

                added++;
                Debug.Log("[Patcher] DescriptionArea 추가 완료");
            }

            // ── 6. StepCountText ──
            if (wizard.transform.Find("StepCountText") == null)
            {
                var sct = CreateTMP(wizard, "StepCountText", "", 18, TextAlignmentOptions.Center, TextGray);
                sct.GetComponent<LayoutElement>().preferredHeight = 28;
                added++;
                Debug.Log("[Patcher] StepCountText 추가 완료");
            }

            // ── 7. Inspector 자동 바인딩 ──────────────────────
            BindController(wizard);

            // ── 완료 ──────────────────────────────────────────
            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Debug.Log($"[Patcher] 완료! {added}개 요소 추가됨. Ctrl+S로 씬 저장하세요.");
            }
            else
            {
                Debug.Log("[Patcher] 추가할 요소가 없습니다. 모두 이미 존재합니다.");
                // 바인딩만 다시 실행
                BindController(wizard);
            }
        }

        // ══════════════════════════════════════════════════════
        //  Inspector 자동 바인딩
        // ══════════════════════════════════════════════════════

        static void BindController(GameObject wizard)
        {
            var canvas = wizard.transform.parent?.gameObject;
            if (canvas == null) return;

            var ctrlType = System.Type.GetType(
                "OpenDesk.Presentation.UI.Onboarding.OnboardingUIController, Assembly-CSharp");
            if (ctrlType == null)
            {
                Debug.LogWarning("[Patcher] OnboardingUIController 타입 미발견 — 컴파일 후 다시 실행하세요.");
                return;
            }

            var ctrl = canvas.GetComponent(ctrlType);
            if (ctrl == null)
                ctrl = canvas.AddComponent(ctrlType);

            var so = new SerializedObject(ctrl);

            // 안전 바인딩 헬퍼: 이미 연결된 필드는 건드리지 않음
            void BindIfNull(string prop, Object value)
            {
                if (value == null) return;
                var sp = so.FindProperty(prop);
                if (sp != null && sp.objectReferenceValue == null)
                    sp.objectReferenceValue = value;
            }

            // 전체 진행률
            BindIfNull("_progressBar", FindComp<Slider>(wizard, "ProgressBar"));
            BindIfNull("_stepTitle", FindComp<TMP_Text>(wizard, "StepTitle"));
            BindIfNull("_stepCountText", FindComp<TMP_Text>(wizard, "StepCountText"));

            // 설명 영역
            var descArea = wizard.transform.Find("DescriptionArea");
            if (descArea != null)
            {
                BindIfNull("_descriptionText", FindComp<TMP_Text>(descArea.gameObject, "DescriptionText"));
                BindIfNull("_estimatedTimeText", FindComp<TMP_Text>(descArea.gameObject, "EstimatedTimeText"));
                BindIfNull("_whyNeededToggle", FindComp<Button>(descArea.gameObject, "WhyNeededToggle"));
                BindIfNull("_whyNeededPanel", FindGO(descArea.gameObject, "WhyNeededPanel"));
                BindIfNull("_whyNeededText", FindComp<TMP_Text>(descArea.gameObject, "WhyNeededPanel/WhyNeededText"));
            }

            // 패널
            BindIfNull("_scanningPanel", FindGO(wizard, "ScanningPanel"));
            BindIfNull("_nodeUpgradePanel", FindGO(wizard, "NodeUpgradePanel"));
            BindIfNull("_installingNodePanel", FindGO(wizard, "InstallingNodePanel"));
            BindIfNull("_wsl2Panel", FindGO(wizard, "Wsl2Panel"));
            BindIfNull("_detectingPanel", FindGO(wizard, "DetectingPanel"));
            BindIfNull("_installingClawPanel", FindGO(wizard, "InstallingClawPanel"));
            BindIfNull("_gatewayPanel", FindGO(wizard, "GatewayPanel"));
            BindIfNull("_agentsPanel", FindGO(wizard, "AgentsPanel"));
            BindIfNull("_workspacePanel", FindGO(wizard, "WorkspacePanel"));
            BindIfNull("_completePanel", FindGO(wizard, "CompletePanel"));
            BindIfNull("_errorPanel", FindGO(wizard, "ErrorPanel"));

            // Node.js 버전 충돌
            var nodePanel = wizard.transform.Find("NodeUpgradePanel");
            if (nodePanel != null)
            {
                BindIfNull("_nodeVersionText", FindComp<TMP_Text>(nodePanel.gameObject, "NodeVersionText"));
                BindIfNull("_nodeProjectListText", FindComp<TMP_Text>(nodePanel.gameObject, "NodeProjectListText"));
                BindIfNull("_nodeSafeInstallButton", FindComp<Button>(nodePanel.gameObject, "NodeSafeInstallButton"));
                BindIfNull("_nodeOverwriteButton", FindComp<Button>(nodePanel.gameObject, "NodeOverwriteButton"));
                BindIfNull("_nodeSkipButton", FindComp<Button>(nodePanel.gameObject, "NodeSkipButton"));
            }

            // 공통 버튼
            BindIfNull("_retryButton", FindComp<Button>(wizard, "FooterButtons/RetryButton"));
            BindIfNull("_offlineButton", FindComp<Button>(wizard, "FooterButtons/OfflineButton"));

            // Gateway
            BindIfNull("_gatewayUrlInput", FindComp<TMP_InputField>(wizard, "GatewayPanel/UrlInputField"));
            BindIfNull("_gatewayConnectButton", FindComp<Button>(wizard, "GatewayPanel/ConnectButton"));

            // Workspace
            BindIfNull("_workspacePathInput", FindComp<TMP_InputField>(wizard, "WorkspacePanel/PathRow/PathInputField"));
            BindIfNull("_workspaceBrowseButton", FindComp<Button>(wizard, "WorkspacePanel/PathRow/BrowseButton"));
            BindIfNull("_workspaceSkipButton", FindComp<Button>(wizard, "WorkspacePanel/WorkspaceSkipButton"));
            BindIfNull("_workspaceConfirmButton", FindComp<Button>(wizard, "WorkspacePanel/ConfirmButton"));

            // WSL2 재시작
            BindIfNull("_rebootNowButton", FindComp<Button>(wizard, "Wsl2Panel/RebootNowButton"));
            BindIfNull("_rebootLaterButton", FindComp<Button>(wizard, "Wsl2Panel/RebootLaterButton"));

            // 완료
            BindIfNull("_enterOfficeButton", FindComp<Button>(wizard, "CompletePanel/EnterButton"));

            // 에러
            BindIfNull("_errorText", FindComp<TMP_Text>(wizard, "ErrorPanel/ErrorText"));
            BindIfNull("_errorDetailToggle", FindComp<Button>(wizard, "ErrorPanel/ErrorDetailToggle"));
            BindIfNull("_errorDetailPanel", FindGO(wizard, "ErrorPanel/ErrorDetailPanel"));

            // 설치 진행
            BindIfNull("_installProgressSlider", FindComp<Slider>(wizard, "InstallingNodePanel/InstallProgress"));
            BindIfNull("_installStatusText", FindComp<TMP_Text>(wizard, "InstallingNodePanel/InstallStatus"));

            so.ApplyModifiedPropertiesWithoutUndo();

            // 바인딩 결과 리포트
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
            Debug.Log($"[Patcher] Inspector 바인딩: {bound}/{total} 연결됨");
        }

        // ══════════════════════════════════════════════════════
        //  유틸리티
        // ══════════════════════════════════════════════════════

        static T FindComp<T>(GameObject root, string path) where T : Component
        {
            var t = root.transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        static GameObject FindGO(GameObject root, string path)
        {
            var t = root.transform.Find(path);
            return t != null ? t.gameObject : null;
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

        static GameObject CreateStepPanel(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(30, 280);
            rt.offsetMax = new Vector2(-30, -150);
            return go;
        }

        static TMP_Text CreateTMP(GameObject parent, string name, string text, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.Left, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color ?? TextWhite;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            if (NotoSansKR != null) tmp.font = NotoSansKR;
            go.AddComponent<LayoutElement>();
            return tmp;
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

            go.AddComponent<LayoutElement>().preferredHeight = 60;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 52;
            tmp.color = TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            if (NotoSansKR != null) tmp.font = NotoSansKR;

            return go;
        }
    }
}
#endif
