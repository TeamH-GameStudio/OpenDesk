using System.Text;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.Dashboard
{
    /// <summary>
    /// 실시간 콘솔 로그 뷰어 (프로그램 하단 패널)
    /// - 접기/펼치기 토글
    /// - 로그 레벨 필터 (Info/Warning/Error/AgentAction)
    /// - 자동 스크롤
    /// </summary>
    public class ConsoleLogController : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject   _panelRoot;       // 전체 콘솔 패널
        [SerializeField] private RectTransform _contentArea;     // ScrollView Content
        [SerializeField] private ScrollRect    _scrollRect;

        [Header("로그 표시")]
        [SerializeField] private TMP_Text     _logText;          // 텍스트 기반 로그 표시
        [SerializeField] private int          _maxDisplayLines = 100;

        [Header("컨트롤")]
        [SerializeField] private Button       _toggleButton;     // 접기/펼치기
        [SerializeField] private TMP_Text     _toggleButtonText;
        [SerializeField] private Button       _clearButton;
        [SerializeField] private Button       _filterInfoButton;
        [SerializeField] private Button       _filterWarnButton;
        [SerializeField] private Button       _filterErrorButton;
        [SerializeField] private Button       _filterAllButton;

        [Inject] private IConsoleLogService _logService;

        private bool _isExpanded = false;
        private readonly StringBuilder _sb = new();

        private void Start()
        {
            if (_logService == null) return;

            // 초기 상태: 접힌 상태
            SetExpanded(false);

            // 로그 수신 → 텍스트 업데이트
            _logService.OnLogReceived.Subscribe(OnLogReceived).AddTo(this);

            // 버튼 바인딩
            if (_toggleButton != null)
                _toggleButton.onClick.AddListener(() => SetExpanded(!_isExpanded));

            if (_clearButton != null)
                _clearButton.onClick.AddListener(() =>
                {
                    _logService.Clear();
                    if (_logText != null) _logText.text = "";
                });

            // 필터 버튼
            if (_filterAllButton != null)
                _filterAllButton.onClick.AddListener(() => ApplyFilter(LogLevel.Info));
            if (_filterInfoButton != null)
                _filterInfoButton.onClick.AddListener(() => ApplyFilter(LogLevel.Info));
            if (_filterWarnButton != null)
                _filterWarnButton.onClick.AddListener(() => ApplyFilter(LogLevel.Warning));
            if (_filterErrorButton != null)
                _filterErrorButton.onClick.AddListener(() => ApplyFilter(LogLevel.Error));
        }

        private void OnLogReceived(ConsoleLogEntry entry)
        {
            if (_logText == null) return;

            var color = entry.Level switch
            {
                LogLevel.Warning     => "#FFD700",
                LogLevel.Error       => "#FF4444",
                LogLevel.AgentAction => "#44AAFF",
                _                    => "#CCCCCC",
            };

            var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            _sb.AppendLine($"<color={color}>[{time}] {entry.DisplayMessage}</color>");

            // 최대 줄 수 제한
            var lines = _sb.ToString().Split('\n');
            if (lines.Length > _maxDisplayLines)
            {
                _sb.Clear();
                for (int i = lines.Length - _maxDisplayLines; i < lines.Length; i++)
                    _sb.AppendLine(lines[i]);
            }

            _logText.text = _sb.ToString();

            // 자동 스크롤
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;

            if (_panelRoot != null)
            {
                var rt = _panelRoot.GetComponent<RectTransform>();
                if (rt != null)
                {
                    var size = rt.sizeDelta;
                    size.y = expanded ? 300f : 40f; // 펼침: 300px, 접힘: 40px (타이틀바만)
                    rt.sizeDelta = size;
                }
            }

            if (_toggleButtonText != null)
                _toggleButtonText.text = expanded ? "▼ 콘솔" : "▲ 콘솔";

            if (_contentArea != null)
                _contentArea.gameObject.SetActive(expanded);
        }

        private void ApplyFilter(LogLevel level)
        {
            _logService.SetFilter(level);
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_logText == null || _logService == null) return;

            var logs = _logService.GetRecentLogs(_maxDisplayLines);
            _sb.Clear();

            foreach (var entry in logs)
            {
                var color = entry.Level switch
                {
                    LogLevel.Warning     => "#FFD700",
                    LogLevel.Error       => "#FF4444",
                    LogLevel.AgentAction => "#44AAFF",
                    _                    => "#CCCCCC",
                };
                var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                _sb.AppendLine($"<color={color}>[{time}] {entry.DisplayMessage}</color>");
            }

            _logText.text = _sb.ToString();
        }
    }
}
