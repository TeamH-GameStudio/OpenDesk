using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>보안 감사 패널 — 방패 아이콘 + 스캔 + 자동 수정</summary>
    public class SecurityPanelController : MonoBehaviour
    {
        [Header("상태")]
        [SerializeField] private Image    _shieldIcon;
        [SerializeField] private TMP_Text _statusText;

        [Header("버튼")]
        [SerializeField] private Button _quickScanButton;
        [SerializeField] private Button _deepScanButton;
        [SerializeField] private Button _autoFixButton;

        [Header("진행률")]
        [SerializeField] private Slider   _progressSlider;
        [SerializeField] private TMP_Text _progressText;

        [Header("결과")]
        [SerializeField] private RectTransform _resultsContainer;
        [SerializeField] private GameObject    _auditItemPrefab;
        [SerializeField] private TMP_Text      _summaryText;

        [Header("색상")]
        [SerializeField] private Color _passColor     = Color.green;
        [SerializeField] private Color _warnColor     = Color.yellow;
        [SerializeField] private Color _criticalColor = Color.red;
        [SerializeField] private Color _neutralColor  = Color.gray;

        [Inject] private ISecurityAuditService _auditService;

        private void Start()
        {
            if (_auditService == null) return;

            _quickScanButton?.onClick.AddListener(() => RunScan(false));
            _deepScanButton?.onClick.AddListener(() => RunScan(true));
            _autoFixButton?.onClick.AddListener(RunAutoFix);

            _auditService.Progress.Subscribe(p =>
            {
                if (_progressSlider != null) _progressSlider.value = p;
            }).AddTo(this);

            _auditService.StatusText.Subscribe(t =>
            {
                if (_progressText != null) _progressText.text = t;
            }).AddTo(this);

            // 초기 상태
            if (_autoFixButton != null) _autoFixButton.gameObject.SetActive(false);
            if (_progressSlider != null) _progressSlider.gameObject.SetActive(false);
            SetShieldColor(_neutralColor);
            if (_statusText != null) _statusText.text = "스캔을 실행하세요";
        }

        private async void RunScan(bool deep)
        {
            if (_progressSlider != null) _progressSlider.gameObject.SetActive(true);

            var report = await _auditService.RunAuditAsync(deep);
            DisplayReport(report);

            if (_progressSlider != null) _progressSlider.gameObject.SetActive(false);
        }

        private async void RunAutoFix()
        {
            if (_progressSlider != null) _progressSlider.gameObject.SetActive(true);

            var report = await _auditService.RunAutoFixAsync();
            DisplayReport(report);

            if (_progressSlider != null) _progressSlider.gameObject.SetActive(false);
        }

        private void DisplayReport(AuditReport report)
        {
            // 방패 색상
            if (report.IsClean)
                SetShieldColor(_passColor);
            else if (report.CriticalCount > 0)
                SetShieldColor(_criticalColor);
            else
                SetShieldColor(_warnColor);

            // 상태 텍스트
            if (_statusText != null)
                _statusText.text = report.IsClean
                    ? "✓ 안전 — 취약점 없음"
                    : $"⚠ {report.CriticalCount} 치명적, {report.WarnCount} 경고";

            // 자동 수정 버튼 표시
            var hasFixable = report.Items.Exists(i => i.CanAutoFix && !i.IsFixed && i.Severity != AuditSeverity.Pass);
            if (_autoFixButton != null)
                _autoFixButton.gameObject.SetActive(hasFixable);

            // 요약
            if (_summaryText != null)
                _summaryText.text = $"통과 {report.PassCount} | 경고 {report.WarnCount} | 치명적 {report.CriticalCount}";

            // 결과 목록
            if (_resultsContainer != null)
            {
                foreach (Transform child in _resultsContainer)
                    Destroy(child.gameObject);

                foreach (var item in report.Items)
                    CreateAuditItemUI(item);
            }
        }

        private void CreateAuditItemUI(AuditItem item)
        {
            if (_auditItemPrefab == null || _resultsContainer == null) return;

            var obj = Instantiate(_auditItemPrefab, _resultsContainer);

            var severityIcon = obj.transform.Find("SeverityIcon")?.GetComponent<Image>();
            var titleText    = obj.transform.Find("TitleText")?.GetComponent<TMP_Text>();
            var descText     = obj.transform.Find("DescriptionText")?.GetComponent<TMP_Text>();
            var fixBadge     = obj.transform.Find("FixBadge")?.gameObject;

            if (severityIcon != null)
            {
                severityIcon.color = item.Severity switch
                {
                    AuditSeverity.Pass     => _passColor,
                    AuditSeverity.Warn     => _warnColor,
                    AuditSeverity.Critical => _criticalColor,
                    _                      => _neutralColor,
                };
            }

            if (titleText != null)  titleText.text = item.Title;
            if (descText != null)   descText.text  = item.Description;
            if (fixBadge != null)   fixBadge.SetActive(item.CanAutoFix && !item.IsFixed);
        }

        private void SetShieldColor(Color color)
        {
            if (_shieldIcon != null) _shieldIcon.color = color;
        }
    }
}
