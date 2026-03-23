using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.UI.OfficeWizard;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>설정 패널 — Gateway URL, 로그 레벨, 디버그</summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("Gateway")]
        [SerializeField] private TMP_InputField _gatewayUrlInput;
        [SerializeField] private Button         _gatewaySaveButton;
        [SerializeField] private TMP_Text       _gatewayStatusText;

        [Header("로그")]
        [SerializeField] private TMP_Dropdown _logLevelDropdown;
        [SerializeField] private Button       _clearLogsButton;

        [Header("초기 설정")]
        [SerializeField] private Button _restartWizardButton;

        [Header("디버그 (에디터 전용)")]
        [SerializeField] private TMP_Dropdown _forceStateDropdown;
        [SerializeField] private Button       _applyStateButton;
        [SerializeField] private TMP_InputField _debugSessionInput;

        [Inject] private IOpenClawBridgeService _bridge;
        [Inject] private IConsoleLogService     _logService;
        [Inject] private IAgentStateService     _agentState;

        private void Start()
        {
            // Gateway URL
            if (_gatewayUrlInput != null)
                _gatewayUrlInput.text = PlayerPrefs.GetString("OpenDesk_GatewayUrl", "ws://localhost:18789/events");

            _gatewaySaveButton?.onClick.AddListener(OnGatewaySave);

            // 로그 레벨
            if (_logLevelDropdown != null)
            {
                _logLevelDropdown.ClearOptions();
                _logLevelDropdown.AddOptions(new System.Collections.Generic.List<string>
                    { "Info", "Warning", "Error", "AgentAction" });
                _logLevelDropdown.onValueChanged.AddListener(idx =>
                    _logService?.SetFilter((LogLevel)idx));
            }

            _clearLogsButton?.onClick.AddListener(() => _logService?.Clear());

            // 초기 설정 다시 하기
            _restartWizardButton?.onClick.AddListener(() =>
            {
                var wizard = FindObjectOfType<OfficeWizardController>();
                if (wizard != null)
                    wizard.RestartWizard();
                else
                    Debug.LogWarning("[Settings] OfficeWizardController를 찾을 수 없습니다.");
            });

            // 디버그 상태 강제 전환
            #if UNITY_EDITOR
            if (_forceStateDropdown != null)
            {
                _forceStateDropdown.ClearOptions();
                _forceStateDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "Idle", "TaskStarted", "Thinking", "Planning",
                    "Executing", "Reviewing", "TaskCompleted", "TaskFailed",
                    "Disconnected", "Connected"
                });
            }

            _applyStateButton?.onClick.AddListener(OnForceState);
            #else
            // 빌드에서는 디버그 섹션 숨김
            _forceStateDropdown?.gameObject.SetActive(false);
            _applyStateButton?.gameObject.SetActive(false);
            _debugSessionInput?.gameObject.SetActive(false);
            #endif
        }

        private async void OnGatewaySave()
        {
            if (_gatewayUrlInput == null || _bridge == null) return;

            var url = _gatewayUrlInput.text.Trim();
            PlayerPrefs.SetString("OpenDesk_GatewayUrl", url);
            PlayerPrefs.Save();

            if (_gatewayStatusText != null)
                _gatewayStatusText.text = "연결 시도 중...";

            try
            {
                await _bridge.DisconnectAsync();
                await _bridge.ConnectAsync(url);

                if (_gatewayStatusText != null)
                    _gatewayStatusText.text = "✓ 연결 성공";
            }
            catch (System.Exception ex)
            {
                if (_gatewayStatusText != null)
                    _gatewayStatusText.text = $"✗ 실패: {ex.Message}";
            }
        }

        #if UNITY_EDITOR
        private void OnForceState()
        {
            if (_forceStateDropdown == null || _agentState == null) return;

            var sessionId = _debugSessionInput != null ? _debugSessionInput.text : "main";
            if (string.IsNullOrEmpty(sessionId)) sessionId = "main";

            var state = (AgentActionType)_forceStateDropdown.value;
            _agentState.ForceState(sessionId, state);
            Debug.Log($"[Debug] 강제 상태 전환: {sessionId} → {state}");
        }
        #endif
    }
}
