using OpenDesk.Pipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// Office 씬에 파이프라인 요소 (3D Printer, In-box, Out-box, 프롬프트 바) 패치.
    /// 메뉴: Tools > OpenDesk > Patch Pipeline Objects
    ///
    /// 이미 존재하면 스킵. 누락된 것만 추가.
    /// </summary>
    public static class PipelineScenePatcher
    {
        private const string DiskettePrefabPath = "Assets/05.Prefabs/SkillDiskette/Diskette.prefab";

        // NotoSansKR 폰트
        private static TMP_FontAsset _font;

        [MenuItem("Tools/OpenDesk/Patch Pipeline Objects", false, 140)]
        public static void Patch()
        {
            LoadFont();
            var diskettePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DiskettePrefabPath);

            if (diskettePrefab == null)
            {
                EditorUtility.DisplayDialog("Error",
                    "Diskette.prefab이 없습니다.\n먼저 OpenDesk > Build Diskette Prefab 실행하세요.", "OK");
                return;
            }

            // ── 3D Printer ──
            var printer = FindOrCreate("3D_Printer", new Vector3(-3f, 0f, -2f));
            if (printer.GetComponent<DiskettePrinterController>() == null)
            {
                // 비주얼 (임시 Cube)
                var body = CreateCubeChild(printer, "PrinterBody",
                    Vector3.up * 0.5f, new Vector3(0.8f, 1.0f, 0.6f),
                    new Color(0.25f, 0.25f, 0.3f));

                // 배출구
                var spawnPoint = CreateEmpty(printer, "SpawnPoint",
                    new Vector3(0f, 1.2f, 0.5f));

                // 선반
                var shelf = CreateEmpty(printer, "PresetShelf",
                    new Vector3(1.5f, 0.5f, 0f));

                // 라벨
                CreateWorldLabel(printer, "PrinterLabel",
                    new Vector3(0f, 1.6f, 0f), "3D PRINTER", 3f);

                // 컴포넌트
                var ctrl = printer.AddComponent<DiskettePrinterController>();
                var so = new SerializedObject(ctrl);
                so.FindProperty("_disketteSpawnPoint").objectReferenceValue = spawnPoint.transform;
                so.FindProperty("_presetShelf").objectReferenceValue = shelf.transform;
                so.FindProperty("_diskettePrefab").objectReferenceValue = diskettePrefab;
                so.FindProperty("_presetSpacing").floatValue = 0.4f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ── In-box ──
            var inbox = FindOrCreate("InBox", new Vector3(3f, 0f, -2f));
            if (inbox.GetComponent<InboxController>() == null)
            {
                CreateCubeChild(inbox, "InboxBody",
                    Vector3.up * 0.3f, new Vector3(0.5f, 0.6f, 0.5f),
                    new Color(0.2f, 0.6f, 0.3f));

                var fileSpawn = CreateEmpty(inbox, "FileIconSpawn",
                    new Vector3(0f, 0.8f, 0f));

                CreateWorldLabel(inbox, "InboxLabel",
                    new Vector3(0f, 1.1f, 0f), "IN", 4f);

                var inCtrl = inbox.AddComponent<InboxController>();
                var inSo = new SerializedObject(inCtrl);
                inSo.FindProperty("_fileIconSpawnPoint").objectReferenceValue = fileSpawn.transform;
                inSo.ApplyModifiedPropertiesWithoutUndo();

                // BoxCollider (클릭용)
                var col = inbox.AddComponent<BoxCollider>();
                col.size = new Vector3(0.5f, 0.6f, 0.5f);
                col.center = Vector3.up * 0.3f;
            }

            // ── Out-box ──
            var outbox = FindOrCreate("OutBox", new Vector3(3f, 0f, -4f));
            if (outbox.GetComponent<OutboxController>() == null)
            {
                CreateCubeChild(outbox, "OutboxBody",
                    Vector3.up * 0.3f, new Vector3(0.5f, 0.6f, 0.5f),
                    new Color(0.6f, 0.3f, 0.2f));

                var resultSpawn = CreateEmpty(outbox, "ResultSpawn",
                    new Vector3(0f, 0.8f, 0f));

                CreateWorldLabel(outbox, "OutboxLabel",
                    new Vector3(0f, 1.1f, 0f), "OUT", 4f);

                var outCtrl = outbox.AddComponent<OutboxController>();
                var outSo = new SerializedObject(outCtrl);
                outSo.FindProperty("_resultSpawnPoint").objectReferenceValue = resultSpawn.transform;
                outSo.ApplyModifiedPropertiesWithoutUndo();

                var outCol = outbox.AddComponent<BoxCollider>();
                outCol.size = new Vector3(0.5f, 0.6f, 0.5f);
                outCol.center = Vector3.up * 0.3f;
            }

            // ── OfficePipelineManager ──
            var pipelineManager = FindOrCreate("PipelineManager", Vector3.zero);
            if (pipelineManager.GetComponent<OfficePipelineManager>() == null)
            {
                var mgr = pipelineManager.AddComponent<OfficePipelineManager>();
                var mgrSo = new SerializedObject(mgr);
                mgrSo.FindProperty("_inbox").objectReferenceValue =
                    inbox.GetComponent<InboxController>();
                mgrSo.FindProperty("_outbox").objectReferenceValue =
                    outbox.GetComponent<OutboxController>();
                mgrSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // ── 프롬프트 바 UI (Canvas) ──
            PatchPromptBarUI(printer);

            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[PipelineScenePatcher] 파이프라인 오브젝트 패치 완료");
        }

        // ══════════════════════════════════════════════
        //  프롬프트 바 UI
        // ══════════════════════════════════════════════

        private static void PatchPromptBarUI(GameObject printer)
        {
            // Screen Space Canvas 찾기 또는 생성
            var canvasName = "Canvas_PromptBar";
            var canvasObj = GameObject.Find(canvasName);
            if (canvasObj != null) return; // 이미 있으면 스킵

            canvasObj = new GameObject(canvasName);
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();

            // 패널 (하단)
            var panel = CreateUIObj(canvasObj, "PromptPanel", typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.2f, 0f);
            panelRect.anchorMax = new Vector2(0.8f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 20f);
            panelRect.sizeDelta = new Vector2(0f, 50f);
            panel.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.18f, 0.95f);

            // InputField
            var inputObj = CreateUIObj(panel, "PromptInput", typeof(Image));
            var inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(0.8f, 1f);
            inputRect.offsetMin = new Vector2(10f, 5f);
            inputRect.offsetMax = new Vector2(-5f, -5f);
            inputObj.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f);

            var inputField = inputObj.AddComponent<TMP_InputField>();

            // InputField 내부 텍스트
            var textArea = CreateUIObj(inputObj, "Text Area", typeof(RectMask2D));
            var textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(5f, 0f);
            textAreaRect.offsetMax = new Vector2(-5f, 0f);

            var placeholder = CreateTMPText(textArea, "Placeholder",
                "스킬 디스켓 크래프팅... (예: 마케팅 카피라이터)", 14f,
                new Color(0.5f, 0.5f, 0.55f));
            var plRect = placeholder.GetComponent<RectTransform>();
            plRect.anchorMin = Vector2.zero;
            plRect.anchorMax = Vector2.one;
            plRect.offsetMin = Vector2.zero;
            plRect.offsetMax = Vector2.zero;

            var inputText = CreateTMPText(textArea, "Text",
                "", 14f, Color.white);
            var itRect = inputText.GetComponent<RectTransform>();
            itRect.anchorMin = Vector2.zero;
            itRect.anchorMax = Vector2.one;
            itRect.offsetMin = Vector2.zero;
            itRect.offsetMax = Vector2.zero;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = inputText.GetComponent<TMP_Text>();
            inputField.placeholder = placeholder.GetComponent<TMP_Text>();

            // 버튼
            var btnObj = CreateUIObj(panel, "CraftButton", typeof(Image), typeof(Button));
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.8f, 0f);
            btnRect.anchorMax = new Vector2(1f, 1f);
            btnRect.offsetMin = new Vector2(5f, 5f);
            btnRect.offsetMax = new Vector2(-10f, -5f);
            btnObj.GetComponent<Image>().color = new Color(0.3f, 0.55f, 1f);

            var btnLabel = CreateTMPText(btnObj, "Label", "크래프팅", 13f, Color.white);
            var blRect = btnLabel.GetComponent<RectTransform>();
            blRect.anchorMin = Vector2.zero;
            blRect.anchorMax = Vector2.one;
            blRect.offsetMin = Vector2.zero;
            blRect.offsetMax = Vector2.zero;
            btnLabel.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;

            // 상태 텍스트 (패널 위)
            var statusObj = CreateTMPText(canvasObj, "CraftStatus", "", 12f,
                new Color(0.7f, 0.7f, 0.75f));
            var statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.2f, 0f);
            statusRect.anchorMax = new Vector2(0.8f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, 75f);
            statusRect.sizeDelta = new Vector2(0f, 20f);

            // DiskettePrinterController에 UI 바인딩
            var printerCtrl = printer.GetComponent<DiskettePrinterController>();
            if (printerCtrl != null)
            {
                var so = new SerializedObject(printerCtrl);
                so.FindProperty("_promptBarPanel").objectReferenceValue = panel;
                so.FindProperty("_promptInput").objectReferenceValue = inputField;
                so.FindProperty("_craftButton").objectReferenceValue =
                    btnObj.GetComponent<Button>();
                so.FindProperty("_statusText").objectReferenceValue =
                    statusObj.GetComponent<TMP_Text>();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ══════════════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════════════

        private static GameObject FindOrCreate(string name, Vector3 position)
        {
            var obj = GameObject.Find(name);
            if (obj != null) return obj;
            obj = new GameObject(name);
            obj.transform.position = position;
            return obj;
        }

        private static GameObject CreateEmpty(GameObject parent, string name, Vector3 localPos)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform);
            obj.transform.localPosition = localPos;
            return obj;
        }

        private static GameObject CreateCubeChild(GameObject parent, string name,
            Vector3 localPos, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent.transform);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = scale;

            var renderer = cube.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            renderer.sharedMaterial = mat;

            // Collider 제거 (부모에서 관리)
            var col = cube.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            return cube;
        }

        private static void CreateWorldLabel(GameObject parent, string name,
            Vector3 localPos, string text, float fontSize)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform);
            obj.transform.localPosition = localPos;

            var tmp = obj.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(1f, 0.3f);

            if (_font != null) tmp.font = _font;
        }

        private static GameObject CreateUIObj(GameObject parent, string name, params System.Type[] components)
        {
            var obj = new GameObject(name, components);
            obj.transform.SetParent(parent.transform, false);
            return obj;
        }

        private static GameObject CreateTMPText(GameObject parent, string name,
            string text, float fontSize, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent.transform, false);
            obj.AddComponent<RectTransform>();

            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            if (_font != null) tmp.font = _font;

            return obj;
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
