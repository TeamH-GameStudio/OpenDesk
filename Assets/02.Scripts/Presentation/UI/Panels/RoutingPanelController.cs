using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>라우팅 모드 선택 패널 — Free/Eco/Auto/Premium</summary>
    public class RoutingPanelController : MonoBehaviour
    {
        [Header("모드 토글 (순서: Free, Eco, Auto, Premium)")]
        [SerializeField] private Toggle[]   _modeToggles   = new Toggle[4];
        [SerializeField] private TMP_Text[] _modeCostTexts = new TMP_Text[4];

        [Header("예상 비용")]
        [SerializeField] private TMP_Text _estimatedCostText;
        [SerializeField] private Button   _applyButton;

        [Inject] private IClawRouterService _router;

        private readonly RoutingMode[] _modes = { RoutingMode.Free, RoutingMode.Eco, RoutingMode.Auto, RoutingMode.Premium };
        private RoutingMode _selectedMode = RoutingMode.Free;

        private async void Start()
        {
            if (_router == null) return;

            // 현재 설정 로드
            var config = await _router.GetCurrentConfigAsync();
            _selectedMode = config.Mode;

            // 비용 표시
            for (int i = 0; i < _modes.Length && i < _modeCostTexts.Length; i++)
            {
                var cost = await _router.GetEstimatedCostAsync(_modes[i]);
                if (_modeCostTexts[i] != null)
                    _modeCostTexts[i].text = cost == 0 ? "무료" : $"~${cost}/월";
            }

            // 토글 초기화
            for (int i = 0; i < _modeToggles.Length; i++)
            {
                if (_modeToggles[i] == null) continue;
                _modeToggles[i].isOn = _modes[i] == _selectedMode;
                int idx = i;
                _modeToggles[i].onValueChanged.AddListener(isOn =>
                {
                    if (isOn) OnModeSelected(idx);
                });
            }

            await UpdateEstimatedCost();

            if (_applyButton != null)
                _applyButton.onClick.AddListener(OnApplyClicked);

            _router.OnConfigChanged.Subscribe(cfg =>
            {
                if (_estimatedCostText != null)
                    _estimatedCostText.text = cfg.EstimatedMonthlyCost == 0
                        ? "예상 월 비용: 무료"
                        : $"예상 월 비용: ~${cfg.EstimatedMonthlyCost}";
            }).AddTo(this);
        }

        private async void OnModeSelected(int index)
        {
            _selectedMode = _modes[index];
            await UpdateEstimatedCost();
        }

        private async Cysharp.Threading.Tasks.UniTask UpdateEstimatedCost()
        {
            if (_estimatedCostText == null || _router == null) return;
            var cost = await _router.GetEstimatedCostAsync(_selectedMode);
            _estimatedCostText.text = cost == 0
                ? "예상 월 비용: 무료"
                : $"예상 월 비용: ~${cost}";
        }

        private async void OnApplyClicked()
        {
            if (_router == null) return;
            var success = await _router.SetRoutingModeAsync(_selectedMode);
            Debug.Log(success
                ? $"[Routing] {_selectedMode} 모드 적용 완료"
                : "[Routing] 모드 적용 실패 — API 키를 확인하세요");
        }
    }
}
