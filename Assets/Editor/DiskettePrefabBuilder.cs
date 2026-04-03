using UnityEngine;
using UnityEditor;
using TMPro;

namespace OpenDesk.Editor
{
    /// <summary>
    /// 디스켓 프리팹 자동 생성.
    /// 메뉴: OpenDesk > Build Diskette Prefab
    /// </summary>
    public static class DiskettePrefabBuilder
    {
        private const string PrefabPath = "Assets/05.Prefabs/SkillDiskette/";
        private const string PrefabName = "Diskette.prefab";

        [MenuItem("OpenDesk/Build Diskette Prefab")]
        public static void Build()
        {
            // 폴더 생성
            if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs/SkillDiskette"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/05.Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "05.Prefabs");
                AssetDatabase.CreateFolder("Assets/05.Prefabs", "SkillDiskette");
            }

            // 루트
            var root = new GameObject("Diskette");

            // Body (Cube 비주얼)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.3f, 0.4f, 0.05f);

            // Body의 Collider 제거 (루트에 별도 BoxCollider 사용)
            var bodyColl = body.GetComponent<Collider>();
            if (bodyColl != null) Object.DestroyImmediate(bodyColl);

            // 머테리얼 설정
            var renderer = body.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.5f, 0.9f, 1.0f));
            renderer.sharedMaterial = mat;
            AssetDatabase.CreateAsset(mat, PrefabPath + "DisketteMaterial.mat");

            // Label (TextMeshPro)
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(root.transform);
            labelObj.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            labelObj.transform.localScale = Vector3.one;

            var tmp = labelObj.AddComponent<TextMeshPro>();
            tmp.text = "Diskette";
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(0.28f, 0.15f);

            // NotoSansKR 폰트 탐색
            var fonts = AssetDatabase.FindAssets("NotoSansKR t:TMP_FontAsset");
            if (fonts.Length > 0)
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    AssetDatabase.GUIDToAssetPath(fonts[0]));
                if (fontAsset != null) tmp.font = fontAsset;
            }

            // BoxCollider (루트에 — 드래그용)
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, 0.4f, 0.05f);
            collider.center = Vector3.zero;

            // SkillDisketteView 컴포넌트
            var view = root.AddComponent<SkillDiskette.SkillDisketteView>();

            // SerializedObject로 private 필드 바인딩
            var so = new SerializedObject(view);
            so.FindProperty("_nameLabel").objectReferenceValue = tmp;
            so.FindProperty("_bodyRenderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 프리팹 저장
            var fullPath = PrefabPath + PrefabName;
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (existingPrefab != null)
                AssetDatabase.DeleteAsset(fullPath);

            PrefabUtility.SaveAsPrefabAsset(root, fullPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DiskettePrefabBuilder] 프리팹 생성 완료: {fullPath}");
        }
    }
}
