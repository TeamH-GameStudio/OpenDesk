using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OpenDesk.Pipeline
{
    /// <summary>
    /// Out-box: 에이전트 작업 결과를 로컬 파일로 배출하는 출력 포인트.
    /// 결과 수신 → 데스크톱 폴더에 저장 → 클릭 시 열기.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class OutboxController : MonoBehaviour
    {
        [Header("비주얼")]
        [SerializeField] private Transform _resultSpawnPoint;
        [SerializeField] private TextMeshPro _resultLabel;

        [Header("설정")]
        [SerializeField] private string _outputFolder = "OpenDesk_Output";

        // ── 내부 ──
        private readonly List<string> _outputFiles = new();
        private Camera _mainCamera;

        public IReadOnlyList<string> OutputFiles => _outputFiles;

        // ══════════════════════════════════════════════
        //  클릭 감지 (New Input System)
        // ══════════════════════════════════════════════

        private void Update()
        {
            if (_outputFiles.Count == 0) return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // UI 위 클릭이면 무시
            if (EventSystem.current != null)
            {
                var pointerData = new PointerEventData(EventSystem.current)
                    { position = mouse.position.ReadValue() };
                var results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, results);
                if (results.Count > 0) return;
            }

            var ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                if (hit.collider.gameObject == gameObject ||
                    hit.collider.transform.IsChildOf(transform))
                {
                    var latest = _outputFiles[^1];
                    if (System.IO.File.Exists(latest))
                    {
                        Application.OpenURL(latest);
                        Debug.Log($"[Outbox] 파일 열기: {latest}");
                    }
                }
            }
        }

        // ══════════════════════════════════════════════
        //  결과 수신
        // ══════════════════════════════════════════════

        public string ReceiveResult(string content, string suggestedFileName = null)
        {
            var folder = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop),
                _outputFolder);

            try
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Outbox] 폴더 생성 실패: {e.Message}");
                return null;
            }

            var fileName = suggestedFileName
                ?? $"result_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = System.IO.Path.Combine(folder, fileName);

            try
            {
                var cleanContent = StripTmpTags(content);
                System.IO.File.WriteAllText(filePath, cleanContent, System.Text.Encoding.UTF8);
                _outputFiles.Add(filePath);
                RefreshVisual(fileName);
                Debug.Log($"[Outbox] 결과 저장: {filePath}");
                return filePath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Outbox] 파일 저장 실패: {e.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════
        //  비주얼
        // ══════════════════════════════════════════════

        /// <summary>TMP 리치텍스트 태그 제거 → 깨끗한 텍스트</summary>
        private static string StripTmpTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // <color=#xxx>, </color>, <b>, </b>, <size=xxx>, </size>, <i>, </i> 등 제거
            var stripped = Regex.Replace(text, @"<[^>]+>", "");

            // "--- json ---" 등 formatter 라벨 → 원래 ``` 블록으로 복원
            stripped = Regex.Replace(stripped, @"--- (\w+) ---", "```$1");
            stripped = Regex.Replace(stripped, @"---------", "```");

            // 연속 빈 줄 정리
            stripped = Regex.Replace(stripped, @"\n{3,}", "\n\n");

            return stripped.Trim();
        }

        private void RefreshVisual(string fileName)
        {
            if (_resultLabel != null)
                _resultLabel.SetText(fileName);
        }
    }
}
