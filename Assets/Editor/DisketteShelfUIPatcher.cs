using OpenDesk.Pipeline;
using OpenDesk.SkillDiskette;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// 디스켓 선반 UI + 크래프팅 토글 버튼을 씬에 패치.
    /// 메뉴: Tools > OpenDesk > Patch Diskette Shelf UI
    ///
    /// 기존 3D 선반(PresetShelf)은 사용하지 않음.
    /// Canvas_DisketteShelf 생성 → DiskettePrinterController에 바인딩.
    /// </summary>
    public static class DisketteShelfUIPatcher
    {
        private static TMP_FontAsset _font;

        private static readonly Color ColPanelBg  = new(0.10f, 0.11f, 0.15f, 0.92f);
        private static readonly Color ColCardBg   = new(0.18f, 0.19f, 0.24f, 1f);
        private static readonly Color ColBtnCraft = new(0.30f, 0.55f, 1.00f, 1f);
        private static readonly Color ColBtnClose = new(0.40f, 0.42f, 0.50f, 1f);
        private static readonly Color ColGhostBg  = new(0.30f, 0.55f, 1.00f, 0.85f);

        [MenuItem("Tools/OpenDesk/Patch Diskette Shelf UI", false, 141)]
        public static void Patch()
        {
            LoadFont();

            // 기존 캔버스가 있으면 제거 후 재생성
            var existing = GameObject.Find("Canvas_DisketteShelf");
            if (existing != null)
                Object.DestroyImmediate(existing);

            // ══════════════════════════════════════
            //  Canvas
            // ══════════════════════════════════════
            var canvasObj = new GameObject("Canvas_DisketteShelf");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            // ══════════════════════════════════════
            //  선반 패널 (우측)
            // ══════════════════════════════════════
            var shelfPanel = CreateUI<Image>(canvasObj, "ShelfPanel");
            shelfPanel.color = ColPanelBg;
            var shelfRect = shelfPanel.rectTransform;
            shelfRect.anchorMin = new Vector2(1f, 0.15f);
            shelfRect.anchorMax = new Vector2(1f, 0.85f);
            shelfRect.pivot = new Vector2(1f, 0.5f);
            shelfRect.anchoredPosition = new Vector2(-10f, 0f);
            shelfRect.sizeDelta = new Vector2(200f, 0f);

            // 선반 제목
            var shelfTitle = CreateTMP(shelfPanel.gameObject, "Title", "디스켓 선반", 14f, Color.white);
            var titleRect = shelfTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(0f, 30f);
            shelfTitle.alignment = TextAlignmentOptions.Center;
            shelfTitle.fontStyle = FontStyles.Bold;

            // ScrollRect + Content
            var scrollObj = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObj.transform.SetParent(shelfPanel.transform, false);
            var scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(5f, 5f);
            scrollRect.offsetMax = new Vector2(-5f, -35f);

            var viewportObj = new GameObject("Viewport", typeof(RectTransform));
            viewportObj.transform.SetParent(scrollObj.transform, false);
            var viewRect = viewportObj.GetComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;
            viewportObj.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewportObj.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // ══════════════════════════════════════
            //  카드 프리팹 (비활성 템플릿)
            // ══════════════════════════════════════
            var cardPrefab = CreateCardTemplate(canvasObj);
            cardPrefab.SetActive(false);

            // ══════════════════════════════════════
            //  드래그 고스트 (비활성)
            // ══════════════════════════════════════
            var ghostObj = CreateUI<Image>(canvasObj, "DragGhost");
            ghostObj.color = ColGhostBg;
            ghostObj.raycastTarget = false;
            var ghostRect = ghostObj.rectTransform;
            ghostRect.sizeDelta = new Vector2(160f, 36f);
            ghostRect.pivot = new Vector2(0.5f, 0.5f);

            var ghostLabel = CreateTMP(ghostObj.gameObject, "Label", "", 12f, Color.white);
            ghostLabel.raycastTarget = false;
            ghostLabel.alignment = TextAlignmentOptions.Center;
            var glRect = ghostLabel.rectTransform;
            glRect.anchorMin = Vector2.zero;
            glRect.anchorMax = Vector2.one;
            glRect.offsetMin = new Vector2(5f, 0f);
            glRect.offsetMax = new Vector2(-5f, 0f);

            ghostObj.gameObject.SetActive(false);

            // ══════════════════════════════════════
            //  토글 버튼 (우하단)
            // ══════════════════════════════════════
            var toggleBtn = CreateUI<Image>(canvasObj, "CraftToggleButton");
            toggleBtn.color = ColBtnCraft;
            var togRect = toggleBtn.rectTransform;
            togRect.anchorMin = new Vector2(1f, 0f);
            togRect.anchorMax = new Vector2(1f, 0f);
            togRect.pivot = new Vector2(1f, 0f);
            togRect.anchoredPosition = new Vector2(-10f, 20f);
            togRect.sizeDelta = new Vector2(130f, 40f);
            var togButton = toggleBtn.gameObject.AddComponent<Button>();

            var togLabel = CreateTMP(toggleBtn.gameObject, "Label", "크래프팅", 13f, Color.white);
            togLabel.alignment = TextAlignmentOptions.Center;
            var tlRect = togLabel.rectTransform;
            tlRect.anchorMin = Vector2.zero;
            tlRect.anchorMax = Vector2.one;
            tlRect.offsetMin = Vector2.zero;
            tlRect.offsetMax = Vector2.zero;

            // ══════════════════════════════════════
            //  DisketteShelfUI 컴포넌트
            // ══════════════════════════════════════
            var shelfUI = canvasObj.AddComponent<DisketteShelfUI>();
            var shelfSo = new SerializedObject(shelfUI);
            shelfSo.FindProperty("_cardContainer").objectReferenceValue = contentRect.transform;
            shelfSo.FindProperty("_cardPrefab").objectReferenceValue = cardPrefab;
            shelfSo.FindProperty("_scrollRect").objectReferenceValue = scroll;
            shelfSo.FindProperty("_dragGhost").objectReferenceValue = ghostRect;
            shelfSo.FindProperty("_dragGhostLabel").objectReferenceValue = ghostLabel;
            shelfSo.FindProperty("_dragGhostBg").objectReferenceValue = ghostObj;
            shelfSo.ApplyModifiedPropertiesWithoutUndo();

            // ══════════════════════════════════════
            //  DiskettePrinterController 바인딩
            // ══════════════════════════════════════
            var printer = Object.FindFirstObjectByType<DiskettePrinterController>();
            if (printer != null)
            {
                var printerSo = new SerializedObject(printer);
                printerSo.FindProperty("_shelfUI").objectReferenceValue = shelfUI;
                printerSo.FindProperty("_toggleButton").objectReferenceValue = togButton;
                printerSo.FindProperty("_toggleLabel").objectReferenceValue = togLabel;
                printerSo.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[DisketteShelfUI] DiskettePrinterController 바인딩 완료");
            }
            else
            {
                Debug.LogWarning("[DisketteShelfUI] DiskettePrinterController 못 찾음 — 수동 바인딩 필요");
            }

            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[DisketteShelfUI] 선반 UI 패치 완료");
        }

        // ══════════════════════════════════════
        //  카드 템플릿
        // ══════════════════════════════════════

        private static GameObject CreateCardTemplate(GameObject canvas)
        {
            var card = new GameObject("CardTemplate", typeof(RectTransform));
            card.transform.SetParent(canvas.transform, false);

            var cardImg = card.AddComponent<Image>();
            cardImg.color = ColCardBg;

            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.minHeight = 50f;

            // 좌측 색상 바
            var colorBar = CreateUI<Image>(card, "ColorBar");
            colorBar.color = Color.cyan;
            var cbRect = colorBar.rectTransform;
            cbRect.anchorMin = new Vector2(0f, 0f);
            cbRect.anchorMax = new Vector2(0f, 1f);
            cbRect.pivot = new Vector2(0f, 0.5f);
            cbRect.anchoredPosition = Vector2.zero;
            cbRect.sizeDelta = new Vector2(6f, 0f);

            // 이름
            var nameLabel = CreateTMP(card, "NameLabel", "디스켓 이름", 13f, Color.white);
            nameLabel.fontStyle = FontStyles.Bold;
            var nlRect = nameLabel.rectTransform;
            nlRect.anchorMin = new Vector2(0f, 0.5f);
            nlRect.anchorMax = new Vector2(1f, 1f);
            nlRect.offsetMin = new Vector2(14f, 0f);
            nlRect.offsetMax = new Vector2(-8f, -4f);

            // 카테고리
            var catLabel = CreateTMP(card, "CategoryLabel", "General", 10f,
                new Color(0.6f, 0.6f, 0.65f));
            var clRect = catLabel.rectTransform;
            clRect.anchorMin = new Vector2(0f, 0f);
            clRect.anchorMax = new Vector2(1f, 0.5f);
            clRect.offsetMin = new Vector2(14f, 4f);
            clRect.offsetMax = new Vector2(-8f, 0f);

            return card;
        }

        // ══════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════

        private static T CreateUI<T>(GameObject parent, string name) where T : Graphic
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent.transform, false);
            return obj.AddComponent<T>();
        }

        private static TextMeshProUGUI CreateTMP(GameObject parent, string name,
            string text, float size, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent.transform, false);
            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            if (_font != null) tmp.font = _font;
            return tmp;
        }

        private static void LoadFont()
        {
            var guids = AssetDatabase.FindAssets("NotoSansKR t:TMP_FontAsset");
            if (guids.Length > 0)
                _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
