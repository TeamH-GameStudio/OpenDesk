using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.TopBar
{
    /// <summary>
    /// 상단 바 — 연결 상태 + 설정 버튼
    /// </summary>
    public class TopBarController : MonoBehaviour
    {
        [SerializeField] private Image    _connectionIcon;
        [SerializeField] private TMP_Text _connectionText;
        [SerializeField] private Button   _settingsButton;
        [SerializeField] private Color    _connectedColor   = Color.green;
        [SerializeField] private Color    _disconnectedColor = Color.red;

        // DEPRECATED 2026-04-27: OpenClaw bridge 의존 제거.
        // [Inject] private IOpenClawBridgeService _bridge;

        private void Start()
        {
            // DEPRECATED: Gateway 연결 상태 표시는 IClaudeService 경로로 재구성 예정.
            if (_connectionText != null) _connectionText.text = "—";
            if (_connectionIcon != null) _connectionIcon.color = _disconnectedColor;
            // if (_bridge == null) return;
            // _bridge.ConnectionState.Subscribe(connected =>
            // {
            //     if (_connectionIcon != null)
            //         _connectionIcon.color = connected ? _connectedColor : _disconnectedColor;
            //     if (_connectionText != null)
            //         _connectionText.text = connected ? "연결됨" : "연결 끊김";
            // }).AddTo(this);
        }
    }
}
