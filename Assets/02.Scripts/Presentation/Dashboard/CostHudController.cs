using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.Dashboard
{
    /// <summary>
    /// 비용 & 리소스 게이지 바 HUD (게임 스타일)
    /// Inspector에서 UI 요소 연결 후 사용
    /// </summary>
    public class CostHudController : MonoBehaviour
    {
        [Header("비용 게이지")]
        [SerializeField] private Slider    _costSlider;        // 0~1 범위
        [SerializeField] private TMP_Text  _costText;          // "$0.00"
        [SerializeField] private Image     _costFill;          // 색상 변경용

        [Header("토큰 카운터")]
        [SerializeField] private TMP_Text  _tokensUsedText;    // "12,345 토큰"
        [SerializeField] private TMP_Text  _tokensSavedText;   // "1,234 절약"

        [Header("리소스 게이지")]
        [SerializeField] private Slider    _cpuSlider;
        [SerializeField] private TMP_Text  _cpuText;
        [SerializeField] private Slider    _ramSlider;
        [SerializeField] private TMP_Text  _ramText;

        [Header("경고")]
        [SerializeField] private GameObject _alertPanel;       // 비용 초과 경고 패널
        [SerializeField] private TMP_Text   _alertText;

        [Header("Telemetry (hook chain — middleware)")]
        [SerializeField] private TMP_Text  _ttftText;           // "TTFT 412 ms"
        [SerializeField] private TMP_Text  _cacheHitText;       // "Cache 41%"
        [SerializeField] private TMP_Text  _retryText;          // "Retries 0"

        [Header("설정")]
        [SerializeField] private float _costMaxDisplay = 50f;  // 슬라이더 최대값 (USD)
        [SerializeField] private float _ramMaxMb       = 4096f; // RAM 최대 표시 (MB)

        [Inject] private ICostMonitorService _costMonitor;
        [Inject] private IAgentTelemetryService _telemetry;

        private void Start()
        {
            if (_costMonitor == null) return;

            if (_alertPanel != null)
                _alertPanel.SetActive(false);

            // 비용 바인딩
            _costMonitor.CurrentSessionCost.Subscribe(cost =>
            {
                var ratio = Mathf.Clamp01((float)cost / _costMaxDisplay);
                if (_costSlider != null) _costSlider.value = ratio;
                if (_costText != null)   _costText.text = $"${cost:F2}";

                // 색상: 초록 → 노랑 → 빨강
                if (_costFill != null)
                    _costFill.color = Color.Lerp(Color.green, Color.red, ratio);
            }).AddTo(this);

            // 토큰 바인딩
            _costMonitor.TotalTokensUsed.Subscribe(tokens =>
            {
                if (_tokensUsedText != null)
                    _tokensUsedText.text = $"{tokens:N0} 토큰";
            }).AddTo(this);

            _costMonitor.TokensSavedByCache.Subscribe(saved =>
            {
                if (_tokensSavedText != null)
                    _tokensSavedText.text = $"{saved:N0} 절약";
            }).AddTo(this);

            // CPU 바인딩
            _costMonitor.CpuUsage.Subscribe(cpu =>
            {
                if (_cpuSlider != null) _cpuSlider.value = cpu;
                if (_cpuText != null)   _cpuText.text = $"CPU {cpu * 100:F0}%";
            }).AddTo(this);

            // RAM 바인딩
            _costMonitor.RamUsageMb.Subscribe(ram =>
            {
                var ratio = Mathf.Clamp01(ram / _ramMaxMb);
                if (_ramSlider != null) _ramSlider.value = ratio;
                if (_ramText != null)   _ramText.text = $"RAM {ram:F0}MB";
            }).AddTo(this);

            // 비용 경고
            _costMonitor.OnCostAlert.Subscribe(cost =>
            {
                if (_alertPanel != null) _alertPanel.SetActive(true);
                if (_alertText != null)  _alertText.text = $"[!] API 비용 ${cost:F2} 초과!";
            }).AddTo(this);

            // Telemetry — 미들웨어 hook chain 측정값. ChatPanelView 또는 별도 디버그 HUD 에서 표시.
            if (_telemetry != null)
            {
                _telemetry.LastTtftMs.Subscribe(ms =>
                {
                    if (_ttftText != null)
                        _ttftText.text = ms > 0 ? $"TTFT {ms} ms" : "TTFT --";
                }).AddTo(this);

                _telemetry.LastCacheHitRatio.Subscribe(ratio =>
                {
                    if (_cacheHitText == null) return;
                    var available = _telemetry.TelemetryAvailable.CurrentValue;
                    _cacheHitText.text = available ? $"Cache {ratio * 100f:F0}%" : "Cache n/a";
                }).AddTo(this);

                _telemetry.LastRetryCount.Subscribe(retries =>
                {
                    if (_retryText != null)
                        _retryText.text = $"Retries {retries}";
                }).AddTo(this);
            }
        }
    }
}
