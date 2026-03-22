#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// Unity 메뉴에서 실행: OpenDesk > Generate All Prefabs
    /// 9개 UI 프리팹을 자동 생성하여 05.Prefabs/UI/ 에 저장
    /// </summary>
    public static class PrefabGenerator
    {
        private const string ChatPath = "Assets/05.Prefabs/UI/Chat";
        private const string CardPath = "Assets/05.Prefabs/UI/Cards";

        // 색상 상수
        private static readonly Color UserBgColor    = new(0.231f, 0.510f, 0.965f, 1f); // #3B82F6
        private static readonly Color AgentBgColor   = new(0.216f, 0.255f, 0.318f, 1f); // #374151
        private static readonly Color SystemColor    = new(1f, 0.843f, 0f, 1f);          // 노랑
        private static readonly Color ChannelBgColor = new(0.020f, 0.588f, 0.412f, 1f); // #059669
        private static readonly Color CardBgColor    = new(0.15f, 0.15f, 0.18f, 1f);
        private static readonly Color TextWhite      = Color.white;
        private static readonly Color TextGray       = new(0.7f, 0.7f, 0.7f, 1f);

        // NotoSansKR 폰트 캐시
        private static TMP_FontAsset _notoFont;
        static TMP_FontAsset NotoFont
        {
            get
            {
                if (_notoFont == null)
                    _notoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                        "Assets/NotoSansKR-VariableFont_wght.asset");
                return _notoFont;
            }
        }

        static void ApplyFont(TMP_Text tmp)
        {
            if (NotoFont != null) tmp.font = NotoFont;
        }

        [MenuItem("OpenDesk/Generate All Prefabs")]
        public static void GenerateAll()
        {
            EnsureDirectories();

            CreateMessageItem_User();
            CreateMessageItem_Agent();
            CreateMessageItem_System();
            CreateMessageItem_Channel();
            CreateChannelCard();
            CreateApiProviderCard();
            CreateSkillCard();
            CreateAuditItem();
            CreateAgentListItem();

            AssetDatabase.Refresh();
            Debug.Log("[PrefabGen] 9개 프리팹 생성 완료!");
        }

        // ═══════════════════════════════════════════════════════════════
        //  채팅 메시지 프리팹 (4종)
        // ═══════════════════════════════════════════════════════════════

        [MenuItem("OpenDesk/Generate Prefabs/Chat - User Message")]
        public static void CreateMessageItem_User()
        {
            var root = CreateMessageBubble("MessageItem_User", UserBgColor, TextAnchor.UpperRight, true);
            SavePrefab(root, $"{ChatPath}/MessageItem_User.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Chat - Agent Message")]
        public static void CreateMessageItem_Agent()
        {
            var root = CreateMessageBubble("MessageItem_Agent", AgentBgColor, TextAnchor.UpperLeft, false);
            SavePrefab(root, $"{ChatPath}/MessageItem_Agent.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Chat - System Message")]
        public static void CreateMessageItem_System()
        {
            var root = new GameObject("MessageItem_System");
            var rt = AddRectTransform(root);
            rt.sizeDelta = new Vector2(0, 30);

            var layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 25;
            layout.flexibleWidth = 1;

            // 텍스트만 (배경 없음)
            var textObj = CreateChild(root, "MessageText");
            var textRt = AddRectTransform(textObj);
            StretchFull(textRt);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "[시스템 메시지]";
            tmp.fontSize = 12;
            tmp.color = SystemColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Italic;

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            SavePrefab(root, $"{ChatPath}/MessageItem_System.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Chat - Channel Message")]
        public static void CreateMessageItem_Channel()
        {
            var root = CreateMessageBubble("MessageItem_Channel", ChannelBgColor, TextAnchor.UpperLeft, false);
            SavePrefab(root, $"{ChatPath}/MessageItem_Channel.prefab");
        }

        private static GameObject CreateMessageBubble(string name, Color bgColor, TextAnchor anchor, bool rightAlign)
        {
            var root = new GameObject(name);
            var rootRt = AddRectTransform(root);
            rootRt.sizeDelta = new Vector2(0, 50);

            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = rightAlign ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(rightAlign ? 100 : 5, rightAlign ? 5 : 100, 2, 2);

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = root.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;

            // 말풍선 배경
            var bubble = CreateChild(root, "Bubble");
            var bubbleRt = AddRectTransform(bubble);

            var bubbleImg = bubble.AddComponent<Image>();
            bubbleImg.color = bgColor;
            bubbleImg.type = Image.Type.Sliced;

            var bubbleLayout = bubble.AddComponent<LayoutElement>();
            bubbleLayout.flexibleWidth = 1;

            var bubbleVlg = bubble.AddComponent<VerticalLayoutGroup>();
            bubbleVlg.padding = new RectOffset(10, 10, 6, 6);
            bubbleVlg.childForceExpandWidth = true;

            var bubbleCsf = bubble.AddComponent<ContentSizeFitter>();
            bubbleCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bubbleCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 메시지 텍스트
            var textObj = CreateChild(bubble, "MessageText");
            AddRectTransform(textObj);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "[메시지]";
            tmp.fontSize = 14;
            tmp.color = TextWhite;
            tmp.alignment = rightAlign ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;

            var textLe = textObj.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1;
            textLe.preferredWidth = 300;

            return root;
        }

        // ═══════════════════════════════════════════════════════════════
        //  카드 프리팹 (5종)
        // ═══════════════════════════════════════════════════════════════

        [MenuItem("OpenDesk/Generate Prefabs/Card - Channel")]
        public static void CreateChannelCard()
        {
            var root = CreateCardBase("ChannelCard", 200);

            AddCardLabel(root, "NameText", "Telegram", 16, FontStyles.Bold);
            AddCardStatusRow(root);
            AddCardInputField(root, "TokenInput", "봇 토큰 입력...");
            AddCardButton(root, "ConnectButton", "연결", new Color(0.2f, 0.7f, 0.3f));
            AddCardButton(root, "DisconnectButton", "해제", new Color(0.7f, 0.2f, 0.2f));
            AddCardButton(root, "TestButton", "테스트", new Color(0.3f, 0.5f, 0.8f));
            AddCardButton(root, "GuideButton", "설정 가이드 >", new Color(0.4f, 0.4f, 0.4f));

            SavePrefab(root, $"{CardPath}/ChannelCard.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Card - ApiProvider")]
        public static void CreateApiProviderCard()
        {
            var root = CreateCardBase("ApiProviderCard", 180);

            AddCardLabel(root, "NameText", "Anthropic (Claude)", 14, FontStyles.Bold);
            AddCardLabel(root, "HintText", "sk-ant-...", 11, FontStyles.Italic, TextGray);
            AddCardStatusRow(root);
            AddCardInputField(root, "KeyInput", "API 키 입력...");

            // 로컬 뱃지
            var badge = CreateChild(root, "LocalBadge");
            AddRectTransform(badge);
            var badgeImg = badge.AddComponent<Image>();
            badgeImg.color = new Color(0.1f, 0.6f, 0.3f, 0.8f);
            var badgeLe = badge.AddComponent<LayoutElement>();
            badgeLe.preferredHeight = 20;
            var badgeTmp = CreateChild(badge, "Text");
            AddRectTransform(badgeTmp);
            StretchFull(badgeTmp.GetComponent<RectTransform>());
            var bt = badgeTmp.AddComponent<TextMeshProUGUI>();
            bt.text = "로컬 (무료)";
            bt.fontSize = 10;
            bt.color = TextWhite;
            bt.alignment = TextAlignmentOptions.Center;
            badge.SetActive(false);

            AddCardButton(root, "SaveButton", "저장", new Color(0.2f, 0.7f, 0.3f));
            AddCardButton(root, "DeleteButton", "삭제", new Color(0.7f, 0.2f, 0.2f));
            AddCardButton(root, "SignupButton", "키 발급 >", new Color(0.4f, 0.4f, 0.4f));
            AddCardLabel(root, "LastVerifiedText", "", 10, FontStyles.Normal, TextGray);

            SavePrefab(root, $"{CardPath}/ApiProviderCard.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Card - Skill")]
        public static void CreateSkillCard()
        {
            var root = CreateCardBase("SkillCard", 220);

            // 썸네일 영역
            var thumb = CreateChild(root, "Thumbnail");
            var thumbRt = AddRectTransform(thumb);
            var thumbImg = thumb.AddComponent<Image>();
            thumbImg.color = new Color(0.25f, 0.25f, 0.3f);
            var thumbLe = thumb.AddComponent<LayoutElement>();
            thumbLe.preferredHeight = 50;

            AddCardLabel(root, "NameText", "Google Calendar", 14, FontStyles.Bold);
            AddCardLabel(root, "AuthorText", "by openclaw", 10, FontStyles.Normal, TextGray);
            AddCardLabel(root, "DescriptionText", "일정 조회/생성/수정", 11);
            AddCardLabel(root, "CategoryText", "생산성", 10, FontStyles.Normal, TextGray);

            // 평점 + 다운로드 행
            var statsRow = CreateChild(root, "StatsRow");
            AddRectTransform(statsRow);
            var statsHlg = statsRow.AddComponent<HorizontalLayoutGroup>();
            statsHlg.spacing = 10;
            var statsLe = statsRow.AddComponent<LayoutElement>();
            statsLe.preferredHeight = 18;

            AddCardLabel(statsRow, "RatingText", "4.5★", 11, FontStyles.Normal, new Color(1f, 0.8f, 0f));
            AddCardLabel(statsRow, "DownloadsText", "12,000", 11, FontStyles.Normal, TextGray);

            // 액션 버튼 + 샌드박스 토글
            AddCardButton(root, "ActionButton", "설치", new Color(0.2f, 0.7f, 0.3f));

            var toggleObj = CreateChild(root, "SandboxToggle");
            AddRectTransform(toggleObj);
            var toggle = toggleObj.AddComponent<Toggle>();
            toggle.isOn = true;
            var toggleLe = toggleObj.AddComponent<LayoutElement>();
            toggleLe.preferredHeight = 25;

            var toggleLabel = CreateChild(toggleObj, "Label");
            var toggleLabelRt = AddRectTransform(toggleLabel);
            StretchFull(toggleLabelRt);
            var toggleTmp = toggleLabel.AddComponent<TextMeshProUGUI>();
            toggleTmp.text = "🔒 샌드박스";
            toggleTmp.fontSize = 11;
            toggleTmp.color = TextWhite;

            SavePrefab(root, $"{CardPath}/SkillCard.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Card - AuditItem")]
        public static void CreateAuditItem()
        {
            var root = new GameObject("AuditItem");
            var rootRt = AddRectTransform(root);
            rootRt.sizeDelta = new Vector2(0, 60);

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.15f, 0.9f);

            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.padding = new RectOffset(8, 8, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;

            var le = root.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = 60;

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 심각도 아이콘
            var icon = CreateChild(root, "SeverityIcon");
            var iconRt = AddRectTransform(icon);
            var iconImg = icon.AddComponent<Image>();
            iconImg.color = Color.green;
            var iconLe = icon.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 16;
            iconLe.preferredHeight = 16;

            // 텍스트 컨테이너
            var textContainer = CreateChild(root, "TextContainer");
            AddRectTransform(textContainer);
            var vlg = textContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            var tcLe = textContainer.AddComponent<LayoutElement>();
            tcLe.flexibleWidth = 1;

            // 제목
            var title = CreateChild(textContainer, "TitleText");
            AddRectTransform(title);
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "점검 항목";
            titleTmp.fontSize = 13;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = TextWhite;

            // 설명
            var desc = CreateChild(textContainer, "DescriptionText");
            AddRectTransform(desc);
            var descTmp = desc.AddComponent<TextMeshProUGUI>();
            descTmp.text = "상세 설명";
            descTmp.fontSize = 11;
            descTmp.color = TextGray;
            descTmp.enableWordWrapping = true;

            // 수정 가능 뱃지
            var fixBadge = CreateChild(root, "FixBadge");
            AddRectTransform(fixBadge);
            var fixImg = fixBadge.AddComponent<Image>();
            fixImg.color = new Color(0.2f, 0.6f, 0.9f, 0.8f);
            var fixLe = fixBadge.AddComponent<LayoutElement>();
            fixLe.preferredWidth = 50;
            fixLe.preferredHeight = 20;

            var fixText = CreateChild(fixBadge, "Text");
            var fixTextRt = AddRectTransform(fixText);
            StretchFull(fixTextRt);
            var fixTmp = fixText.AddComponent<TextMeshProUGUI>();
            fixTmp.text = "수정가능";
            fixTmp.fontSize = 9;
            fixTmp.color = TextWhite;
            fixTmp.alignment = TextAlignmentOptions.Center;

            SavePrefab(root, $"{CardPath}/AuditItem.prefab");
        }

        [MenuItem("OpenDesk/Generate Prefabs/Card - AgentListItem")]
        public static void CreateAgentListItem()
        {
            var root = new GameObject("AgentListItem");
            var rootRt = AddRectTransform(root);
            rootRt.sizeDelta = new Vector2(0, 50);

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.22f, 0.9f);

            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10;
            hlg.padding = new RectOffset(10, 10, 5, 5);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;

            var le = root.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = 50;

            // 아이콘
            var icon = CreateChild(root, "AgentIcon");
            AddRectTransform(icon);
            var iconImg = icon.AddComponent<Image>();
            iconImg.color = new Color(0.3f, 0.6f, 1f);
            var iconLe = icon.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 36;
            iconLe.preferredHeight = 36;

            // 이름
            var nameObj = CreateChild(root, "AgentName");
            AddRectTransform(nameObj);
            var nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
            nameTmp.text = "팀장";
            nameTmp.fontSize = 14;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.color = TextWhite;
            var nameLe = nameObj.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1;

            // 역할
            var roleObj = CreateChild(root, "AgentRole");
            AddRectTransform(roleObj);
            var roleTmp = roleObj.AddComponent<TextMeshProUGUI>();
            roleTmp.text = "main";
            roleTmp.fontSize = 12;
            roleTmp.color = TextGray;
            var roleLe = roleObj.AddComponent<LayoutElement>();
            roleLe.preferredWidth = 60;

            // API 상태 아이콘
            var statusIcon = CreateChild(root, "ApiStatusIcon");
            AddRectTransform(statusIcon);
            var statusImg = statusIcon.AddComponent<Image>();
            statusImg.color = Color.green;
            var statusLe = statusIcon.AddComponent<LayoutElement>();
            statusLe.preferredWidth = 14;
            statusLe.preferredHeight = 14;

            SavePrefab(root, $"{CardPath}/AgentListItem.prefab");
        }

        // ═══════════════════════════════════════════════════════════════
        //  유틸리티
        // ═══════════════════════════════════════════════════════════════

        private static GameObject CreateCardBase(string name, float height)
        {
            var root = new GameObject(name);
            var rt = AddRectTransform(root);
            rt.sizeDelta = new Vector2(280, height);

            var bg = root.AddComponent<Image>();
            bg.color = CardBgColor;

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = root.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth = 280;

            return root;
        }

        private static void AddCardLabel(GameObject parent, string objName, string text,
            float fontSize, FontStyles style = FontStyles.Normal, Color? color = null)
        {
            var obj = CreateChild(parent, objName);
            AddRectTransform(obj);

            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color ?? TextWhite;
            tmp.enableWordWrapping = true;
            ApplyFont(tmp);

            var le = obj.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
        }

        private static void AddCardStatusRow(GameObject parent)
        {
            var row = CreateChild(parent, "StatusRow");
            AddRectTransform(row);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 5;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 20;

            // 상태 아이콘
            var icon = CreateChild(row, "StatusIcon");
            AddRectTransform(icon);
            var iconImg = icon.AddComponent<Image>();
            iconImg.color = Color.gray;
            var iconLe = icon.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 12;
            iconLe.preferredHeight = 12;

            // 상태 텍스트
            var text = CreateChild(row, "StatusText");
            AddRectTransform(text);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.text = "미설정";
            tmp.fontSize = 11;
            tmp.color = TextGray;
        }

        private static void AddCardInputField(GameObject parent, string objName, string placeholder)
        {
            var obj = CreateChild(parent, objName);
            var rt = AddRectTransform(obj);

            var bg = obj.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.12f);

            var input = obj.AddComponent<TMP_InputField>();
            input.contentType = TMP_InputField.ContentType.Password;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 30;

            // 입력 텍스트
            var textArea = CreateChild(obj, "Text Area");
            var textAreaRt = AddRectTransform(textArea);
            StretchFull(textAreaRt, new RectOffset(8, 8, 4, 4));
            textArea.AddComponent<RectMask2D>();

            var textObj = CreateChild(textArea, "Text");
            var textObjRt = AddRectTransform(textObj);
            StretchFull(textObjRt);
            var textTmp = textObj.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = 12;
            textTmp.color = TextWhite;
            ApplyFont(textTmp);

            var phObj = CreateChild(textArea, "Placeholder");
            var phRt = AddRectTransform(phObj);
            StretchFull(phRt);
            var phTmp = phObj.AddComponent<TextMeshProUGUI>();
            phTmp.text = placeholder;
            phTmp.fontSize = 12;
            phTmp.color = new Color(0.5f, 0.5f, 0.5f);
            phTmp.fontStyle = FontStyles.Italic;
            ApplyFont(phTmp);

            input.textViewport = textAreaRt;
            input.textComponent = textTmp;
            input.placeholder = phTmp;
        }

        private static void AddCardButton(GameObject parent, string objName, string label, Color btnColor)
        {
            var obj = CreateChild(parent, objName);
            AddRectTransform(obj);

            var img = obj.AddComponent<Image>();
            img.color = btnColor;

            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = btnColor;
            colors.highlightedColor = btnColor * 1.2f;
            colors.pressedColor = btnColor * 0.8f;
            btn.colors = colors;

            var le = obj.AddComponent<LayoutElement>();
            le.preferredHeight = 28;

            var textObj = CreateChild(obj, "Text");
            var textRt = AddRectTransform(textObj);
            StretchFull(textRt);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.color = TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            ApplyFont(tmp);
        }

        // ── 기본 유틸 ────────────────────────────────────────────────────

        private static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static RectTransform AddRectTransform(GameObject obj)
        {
            var rt = obj.GetComponent<RectTransform>();
            if (rt == null)
                rt = obj.AddComponent<RectTransform>();
            return rt;
        }

        private static void StretchFull(RectTransform rt, RectOffset padding = null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            if (padding != null)
            {
                rt.offsetMin = new Vector2(padding.left, padding.bottom);
                rt.offsetMax = new Vector2(-padding.right, -padding.top);
            }
            else
            {
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        private static void SavePrefab(GameObject obj, string path)
        {
            // 저장 전 모든 TMP에 NotoSansKR 폰트 일괄 적용
            foreach (var tmp in obj.GetComponentsInChildren<TMP_Text>(true))
                ApplyFont(tmp);

            PrefabUtility.SaveAsPrefabAsset(obj, path);
            Object.DestroyImmediate(obj);
            Debug.Log($"[PrefabGen] 생성: {path}");
        }

        private static void EnsureDirectories()
        {
            if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs"))
                AssetDatabase.CreateFolder("Assets", "05.Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs/UI"))
                AssetDatabase.CreateFolder("Assets/05.Prefabs", "UI");
            if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs/UI/Chat"))
                AssetDatabase.CreateFolder("Assets/05.Prefabs/UI", "Chat");
            if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs/UI/Cards"))
                AssetDatabase.CreateFolder("Assets/05.Prefabs/UI", "Cards");
        }
    }
}
#endif
