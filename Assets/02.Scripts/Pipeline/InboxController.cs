using System.Collections.Generic;
using System.Text;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OpenDesk.Pipeline
{
    /// <summary>
    /// In-box: 로컬 파일을 파이프라인에 투입하는 입력 포인트.
    /// 클릭 → 파일 다이얼로그 → 파일 경로 등록 → system prompt 컨텍스트에 포함.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class InboxController : MonoBehaviour
    {
        [Header("비주얼")]
        [SerializeField] private Transform _fileIconSpawnPoint;
        [SerializeField] private TextMeshPro _fileCountLabel;

        // ── 내부 ──
        private readonly List<string> _filePaths = new();
        private Camera _mainCamera;

        // ── R3 이벤트 ──
        private readonly Subject<string> _onFileAdded = new();
        public Observable<string> OnFileAdded => _onFileAdded;
        public IReadOnlyList<string> FilePaths => _filePaths;

        // ══════════════════════════════════════════════
        //  클릭 감지 (New Input System)
        // ══════════════════════════════════════════════

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

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
                    OpenFileDialog();
                }
            }
        }

        // ══════════════════════════════════════════════
        //  파일 추가/제거
        // ══════════════════════════════════════════════

        private void OpenFileDialog()
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel(
                "파일 선택", "", "");
            if (!string.IsNullOrEmpty(path))
                AddFile(path);
#else
            Debug.Log("[Inbox] 빌드 환경 파일 다이얼로그 미구현");
#endif
        }

        public void AddFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (_filePaths.Contains(filePath)) return;

            _filePaths.Add(filePath);
            _onFileAdded.OnNext(filePath);
            RefreshVisual();

            Debug.Log($"[Inbox] 파일 추가: {System.IO.Path.GetFileName(filePath)} (총 {_filePaths.Count}개)");
        }

        public void Clear()
        {
            _filePaths.Clear();
            RefreshVisual();
            Debug.Log("[Inbox] 파일 전부 제거");
        }

        // ══════════════════════════════════════════════
        //  System Prompt 컨텍스트 빌드
        // ══════════════════════════════════════════════

        public string BuildFileContext()
        {
            if (_filePaths.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("<attached_files>");

            foreach (var path in _filePaths)
            {
                var fileName = System.IO.Path.GetFileName(path);
                sb.AppendLine($"<file name=\"{fileName}\">");

                try
                {
                    var content = System.IO.File.ReadAllText(path, Encoding.UTF8);
                    if (content.Length > 5000)
                        content = content[..5000] + "\n... (truncated)";
                    sb.AppendLine(content);
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"(파일 읽기 실패: {e.Message})");
                    sb.AppendLine($"(경로: {path})");
                }

                sb.AppendLine("</file>");
            }

            sb.AppendLine("</attached_files>");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════
        //  비주얼
        // ══════════════════════════════════════════════

        private void RefreshVisual()
        {
            if (_fileCountLabel != null)
                _fileCountLabel.SetText(_filePaths.Count > 0
                    ? $"{_filePaths.Count}개 파일"
                    : "IN");
        }

        private void OnDestroy() => _onFileAdded.Dispose();
    }
}
