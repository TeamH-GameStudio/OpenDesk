#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// OpenDesk > Fix Office Scene Missing Elements
    /// 씬에서 빠진 UI 요소를 자동으로 추가 (기존 오브젝트 유지)
    /// </summary>
    public static class SceneFixer
    {
        static readonly Color BtnBlue  = new(0.231f, 0.510f, 0.965f);
        static readonly Color BtnGray  = new(0.267f, 0.267f, 0.267f);
        static readonly Color BtnRed   = new(0.937f, 0.267f, 0.267f);
        static readonly Color TextGray = new(0.6f, 0.6f, 0.6f);

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

        [MenuItem("OpenDesk/Fix Office Scene Missing Elements")]
        public static void FixAll()
        {
            FixTerminalSessionDropdown();
            FixApiKeysOllamaStatus();
            FixSkillsPanelElements();
            FixSecurityPanelElements();
            FixSettingsPanelDropdowns();
            Debug.Log("[SceneFixer] 빠진 요소 전부 추가 완료! Ctrl+S로 씬 저장하세요.");
        }

        // ═══════════════════════════════════════════════════
        //  2번: SessionDropdown → TMP_Dropdown으로 교체
        // ═══════════════════════════════════════════════════
        [MenuItem("OpenDesk/Fix Missing/Terminal - SessionDropdown")]
        static void FixTerminalSessionDropdown()
        {
            var existing = GameObject.Find("SessionDropdown");
            if (existing == null)
            {
                Debug.LogWarning("[Fix] SessionDropdown 오브젝트를 찾을 수 없습니다. TerminalChatPanel > Header 안에 있어야 합니다.");
                return;
            }

            // 기존 자식 Label 제거 (텍스트만 있던 것)
            var oldLabel = existing.transform.Find("Label");
            if (oldLabel != null) Object.DestroyImmediate(oldLabel.gameObject);

            // TMP_Dropdown이 이미 있으면 스킵
            if (existing.GetComponent<TMP_Dropdown>() != null)
            {
                Debug.Log("[Fix] SessionDropdown에 이미 TMP_Dropdown 있음 - 스킵");
                return;
            }

            // Dropdown 구조 생성
            var dropdown = existing.AddComponent<TMP_Dropdown>();

            // Template 생성
            var template = CreateChild(existing, "Template");
            var templateRt = template.GetComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot = new Vector2(0.5f, 1);
            templateRt.sizeDelta = new Vector2(0, 150);
            var templateImg = template.AddComponent<Image>();
            templateImg.color = new Color(0.15f, 0.15f, 0.2f);
            template.AddComponent<ScrollRect>();
            template.SetActive(false);

            // Viewport
            var viewport = CreateChild(template, "Viewport");
            viewport.AddComponent<RectMask2D>();
            StretchFull(viewport.GetComponent<RectTransform>());

            // Content
            var content = CreateChild(viewport, "Content");
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 28);

            // Item
            var item = CreateChild(content, "Item");
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = Vector2.zero;
            itemRt.anchorMax = new Vector2(1, 0);
            itemRt.sizeDelta = new Vector2(0, 28);
            item.AddComponent<Toggle>();

            // Item > Item Label
            var itemLabel = CreateChild(item, "Item Label");
            StretchFull(itemLabel.GetComponent<RectTransform>(), 8);
            var itemTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemTmp.fontSize = 14;
            itemTmp.color = Color.white;
            if (Font) itemTmp.font = Font;

            // Caption (표시되는 텍스트)
            var captionGo = CreateChild(existing, "Caption");
            StretchFull(captionGo.GetComponent<RectTransform>(), 8);
            var captionTmp = captionGo.AddComponent<TextMeshProUGUI>();
            captionTmp.fontSize = 14;
            captionTmp.color = Color.white;
            captionTmp.text = "main";
            if (Font) captionTmp.font = Font;

            // Dropdown 연결
            dropdown.template = templateRt;
            dropdown.captionText = captionTmp;
            dropdown.itemText = itemTmp;
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string> { "main", "dev", "planner", "life" });

            // ScrollRect 연결
            var sr = template.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = contentRt;

            Debug.Log("[Fix] SessionDropdown → TMP_Dropdown 변환 완료");
        }

        // ═══════════════════════════════════════════════════
        //  5번: ApiKeysPanel에 OllamaStatus 추가
        // ═══════════════════════════════════════════════════
        [MenuItem("OpenDesk/Fix Missing/ApiKeys - OllamaStatus")]
        static void FixApiKeysOllamaStatus()
        {
            var panel = GameObject.Find("ApiKeysPanel");
            if (panel == null) { Debug.LogWarning("[Fix] ApiKeysPanel 못 찾음"); return; }

            if (panel.transform.Find("OllamaStatus") != null)
            {
                Debug.Log("[Fix] OllamaStatus 이미 존재 - 스킵");
                return;
            }

            // Description 다음에 삽입
            var desc = panel.transform.Find("Description");
            var ollamaGo = CreateChild(panel, "OllamaStatus");
            var ollamaTmp = ollamaGo.AddComponent<TextMeshProUGUI>();
            ollamaTmp.text = "Ollama 상태 확인 중...";
            ollamaTmp.fontSize = 14;
            ollamaTmp.color = TextGray;
            if (Font) ollamaTmp.font = Font;
            ollamaGo.AddComponent<LayoutElement>().preferredHeight = 30;

            // Description 바로 뒤로 이동
            if (desc != null)
                ollamaGo.transform.SetSiblingIndex(desc.GetSiblingIndex() + 1);

            Debug.Log("[Fix] OllamaStatus 추가 완료");
        }

        // ═══════════════════════════════════════════════════
        //  7번: SkillsPanel에 SearchField, SearchButton, TabButtons 추가
        // ═══════════════════════════════════════════════════
        [MenuItem("OpenDesk/Fix Missing/Skills - Search and Tabs")]
        static void FixSkillsPanelElements()
        {
            var panel = GameObject.Find("SkillsPanel");
            if (panel == null) { Debug.LogWarning("[Fix] SkillsPanel 못 찾음"); return; }

            // Divider 다음, Container 앞에 삽입
            var divider = panel.transform.Find("Divider");
            int insertIdx = divider != null ? divider.GetSiblingIndex() + 1 : 2;

            // SearchBar
            if (panel.transform.Find("SearchBar") == null)
            {
                var searchBar = CreateChild(panel, "SearchBar");
                var sbHlg = searchBar.AddComponent<HorizontalLayoutGroup>();
                sbHlg.spacing = 8;
                searchBar.AddComponent<LayoutElement>().preferredHeight = 42;
                searchBar.transform.SetSiblingIndex(insertIdx);
                insertIdx++;

                var searchField = CreateInputField(searchBar, "SearchField", "스킬 검색...");
                searchField.GetComponent<LayoutElement>().flexibleWidth = 1;

                var searchBtn = CreateButton(searchBar, "SearchButton", "검색", BtnBlue);
                searchBtn.GetComponent<LayoutElement>().preferredWidth = 80;

                Debug.Log("[Fix] SearchBar 추가 완료");
            }

            // TabButtons
            if (panel.transform.Find("TabButtons") == null)
            {
                var tabRow = CreateChild(panel, "TabButtons");
                var trHlg = tabRow.AddComponent<HorizontalLayoutGroup>();
                trHlg.spacing = 4;
                tabRow.AddComponent<LayoutElement>().preferredHeight = 36;
                tabRow.transform.SetSiblingIndex(insertIdx);

                CreateButton(tabRow, "FeaturedTab", "추천", BtnBlue);
                CreateButton(tabRow, "InstalledTab", "설치됨", BtnGray);
                CreateButton(tabRow, "AllTab", "전체", BtnGray);

                Debug.Log("[Fix] TabButtons 추가 완료");
            }

            // LoadingIndicator
            if (panel.transform.Find("LoadingIndicator") == null)
            {
                var loading = CreateChild(panel, "LoadingIndicator");
                loading.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.5f);
                loading.AddComponent<LayoutElement>().preferredHeight = 40;
                var loadTmp = CreateChild(loading, "Text");
                StretchFull(loadTmp.GetComponent<RectTransform>());
                var tmp = loadTmp.AddComponent<TextMeshProUGUI>();
                tmp.text = "로딩 중...";
                tmp.fontSize = 14;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = TextGray;
                if (Font) tmp.font = Font;
                loading.SetActive(false);

                Debug.Log("[Fix] LoadingIndicator 추가 완료");
            }
        }

        // ═══════════════════════════════════════════════════
        //  8번: SecurityPanel에 ShieldIcon, StatusText, ProgressText 추가
        // ═══════════════════════════════════════════════════
        [MenuItem("OpenDesk/Fix Missing/Security - Shield and Status")]
        static void FixSecurityPanelElements()
        {
            var panel = GameObject.Find("SecurityPanel");
            if (panel == null) { Debug.LogWarning("[Fix] SecurityPanel 못 찾음"); return; }

            var divider = panel.transform.Find("Divider");
            int insertIdx = divider != null ? divider.GetSiblingIndex() + 1 : 2;

            // ShieldIcon
            if (panel.transform.Find("ShieldIcon") == null)
            {
                var shield = CreateChild(panel, "ShieldIcon");
                var shieldImg = shield.AddComponent<Image>();
                shieldImg.color = TextGray;
                var sle = shield.AddComponent<LayoutElement>();
                sle.preferredWidth = 64;
                sle.preferredHeight = 64;
                shield.transform.SetSiblingIndex(insertIdx);
                insertIdx++;
                Debug.Log("[Fix] ShieldIcon 추가 완료");
            }

            // StatusText (보안 상태 전용)
            if (panel.transform.Find("SecurityStatusText") == null)
            {
                var statusGo = CreateChild(panel, "SecurityStatusText");
                var statusTmp = statusGo.AddComponent<TextMeshProUGUI>();
                statusTmp.text = "스캔을 실행하세요";
                statusTmp.fontSize = 18;
                statusTmp.fontStyle = FontStyles.Bold;
                statusTmp.color = TextGray;
                statusTmp.alignment = TextAlignmentOptions.Center;
                if (Font) statusTmp.font = Font;
                statusGo.AddComponent<LayoutElement>().preferredHeight = 30;
                statusGo.transform.SetSiblingIndex(insertIdx);
                insertIdx++;
                Debug.Log("[Fix] SecurityStatusText 추가 완료");
            }

            // ProgressText
            if (panel.transform.Find("ProgressText") == null)
            {
                var ptGo = CreateChild(panel, "ProgressText");
                var ptTmp = ptGo.AddComponent<TextMeshProUGUI>();
                ptTmp.text = "";
                ptTmp.fontSize = 14;
                ptTmp.color = TextGray;
                ptTmp.alignment = TextAlignmentOptions.Center;
                if (Font) ptTmp.font = Font;
                ptGo.AddComponent<LayoutElement>().preferredHeight = 24;

                // ProgressSlider 다음에 삽입
                var slider = panel.transform.Find("ProgressSlider");
                if (slider != null)
                    ptGo.transform.SetSiblingIndex(slider.GetSiblingIndex() + 1);

                Debug.Log("[Fix] ProgressText 추가 완료");
            }
        }

        // ═══════════════════════════════════════════════════
        //  9번: SettingsPanel에 LogLevelDropdown, ForceStateDropdown 추가
        // ═══════════════════════════════════════════════════
        [MenuItem("OpenDesk/Fix Missing/Settings - Dropdowns")]
        static void FixSettingsPanelDropdowns()
        {
            var panel = GameObject.Find("SettingsPanel");
            if (panel == null) { Debug.LogWarning("[Fix] SettingsPanel 못 찾음"); return; }

            // LogLevelDropdown
            if (panel.transform.Find("LogLevelDropdown") == null)
            {
                var logLabel = panel.transform.Find("LogLabel");
                var ddGo = CreateDropdown(panel, "LogLevelDropdown",
                    new[] { "Info", "Warning", "Error", "AgentAction" });
                if (logLabel != null)
                    ddGo.transform.SetSiblingIndex(logLabel.GetSiblingIndex() + 1);
                Debug.Log("[Fix] LogLevelDropdown 추가 완료");
            }

            // ForceStateDropdown
            if (panel.transform.Find("ForceStateDropdown") == null)
            {
                var debugLabel = panel.transform.Find("DebugLabel");
                var ddGo = CreateDropdown(panel, "ForceStateDropdown",
                    new[] { "Idle", "TaskStarted", "Thinking", "Planning", "Executing",
                            "Reviewing", "TaskCompleted", "TaskFailed", "Disconnected" });
                if (debugLabel != null)
                    ddGo.transform.SetSiblingIndex(debugLabel.GetSiblingIndex() + 1);
                Debug.Log("[Fix] ForceStateDropdown 추가 완료");
            }
        }

        // ═══════════════════════════════════════════════════
        //  유틸리티
        // ═══════════════════════════════════════════════════

        static GameObject CreateChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        static void StretchFull(RectTransform rt, float padding = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        static GameObject CreateButton(GameObject parent, string name, string label, Color color)
        {
            var go = CreateChild(parent, name);
            go.AddComponent<Image>().color = color;
            var btn = go.AddComponent<Button>();
            var c = btn.colors; c.normalColor = color; btn.colors = c;
            go.AddComponent<LayoutElement>().preferredHeight = 36;

            var txt = CreateChild(go, "Text");
            StretchFull(txt.GetComponent<RectTransform>());
            var tmp = txt.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 14; tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            if (Font) tmp.font = Font;

            return go;
        }

        static GameObject CreateInputField(GameObject parent, string name, string placeholder)
        {
            var go = CreateChild(parent, name);
            go.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f);
            go.AddComponent<LayoutElement>().preferredHeight = 40;

            var textArea = CreateChild(go, "Text Area");
            StretchFull(textArea.GetComponent<RectTransform>(), 8);
            textArea.AddComponent<RectMask2D>();

            var textGo = CreateChild(textArea, "Text");
            StretchFull(textGo.GetComponent<RectTransform>());
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = 14; textTmp.color = Color.white;
            if (Font) textTmp.font = Font;

            var phGo = CreateChild(textArea, "Placeholder");
            StretchFull(phGo.GetComponent<RectTransform>());
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder; phTmp.fontSize = 14;
            phTmp.color = new Color(0.4f, 0.4f, 0.4f);
            phTmp.fontStyle = FontStyles.Italic;
            if (Font) phTmp.font = Font;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = textTmp;
            input.placeholder = phTmp;

            return go;
        }

        static GameObject CreateDropdown(GameObject parent, string name, string[] options)
        {
            var go = CreateChild(parent, name);
            go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f);
            go.AddComponent<LayoutElement>().preferredHeight = 40;

            // Caption
            var captionGo = CreateChild(go, "Caption");
            StretchFull(captionGo.GetComponent<RectTransform>(), 10);
            var captionTmp = captionGo.AddComponent<TextMeshProUGUI>();
            captionTmp.text = options.Length > 0 ? options[0] : "";
            captionTmp.fontSize = 14; captionTmp.color = Color.white;
            if (Font) captionTmp.font = Font;

            // Template
            var template = CreateChild(go, "Template");
            var templateRt = template.GetComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot = new Vector2(0.5f, 1);
            templateRt.sizeDelta = new Vector2(0, 150);
            template.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);
            var sr = template.AddComponent<ScrollRect>();
            template.SetActive(false);

            var viewport = CreateChild(template, "Viewport");
            viewport.AddComponent<RectMask2D>();
            StretchFull(viewport.GetComponent<RectTransform>());

            var content = CreateChild(viewport, "Content");
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(0, 32);

            var item = CreateChild(content, "Item");
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = Vector2.zero;
            itemRt.anchorMax = new Vector2(1, 0);
            itemRt.sizeDelta = new Vector2(0, 32);
            item.AddComponent<Toggle>();

            var itemLabel = CreateChild(item, "Item Label");
            StretchFull(itemLabel.GetComponent<RectTransform>(), 10);
            var itemTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemTmp.fontSize = 14; itemTmp.color = Color.white;
            if (Font) itemTmp.font = Font;

            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = contentRt;

            var dd = go.AddComponent<TMP_Dropdown>();
            dd.template = templateRt;
            dd.captionText = captionTmp;
            dd.itemText = itemTmp;
            dd.ClearOptions();
            dd.AddOptions(new System.Collections.Generic.List<string>(options));

            return go;
        }
    }
}
#endif
