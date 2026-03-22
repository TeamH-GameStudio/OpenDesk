using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Onboarding
{
    /// <summary>
    /// 온보딩 전체 UI 컨트롤러
    /// OnboardingService 상태에 따라 패널 전환
    /// </summary>
    public class OnboardingUIController : MonoBehaviour
    {
        [Header("진행률")]
        [SerializeField] private Slider   _progressBar;
        [SerializeField] private TMP_Text _stepTitle;

        [Header("스텝 패널 (순서대로)")]
        [SerializeField] private GameObject _scanningPanel;
        [SerializeField] private GameObject _installingNodePanel;
        [SerializeField] private GameObject _wsl2Panel;
        [SerializeField] private GameObject _detectingPanel;
        [SerializeField] private GameObject _installingClawPanel;
        [SerializeField] private GameObject _gatewayPanel;
        [SerializeField] private GameObject _agentsPanel;
        [SerializeField] private GameObject _workspacePanel;
        [SerializeField] private GameObject _completePanel;
        [SerializeField] private GameObject _errorPanel;

        [Header("공통 버튼")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _offlineButton;
        [SerializeField] private Button _skipButton;

        [Header("Gateway 패널")]
        [SerializeField] private TMP_InputField _gatewayUrlInput;
        [SerializeField] private Button         _gatewayConnectButton;

        [Header("Workspace 패널")]
        [SerializeField] private TMP_InputField _workspacePathInput;
        [SerializeField] private Button         _workspaceBrowseButton;
        [SerializeField] private Button         _workspaceSkipButton;

        [Header("완료 패널")]
        [SerializeField] private Button _enterOfficeButton;

        [Header("에러")]
        [SerializeField] private TMP_Text _errorText;

        [Header("설치 진행")]
        [SerializeField] private Slider   _installProgressSlider;
        [SerializeField] private TMP_Text _installStatusText;

        [Inject] private IOnboardingService    _onboarding;
        [Inject] private IOpenClawInstaller    _installer;
        [Inject] private INodeEnvironmentService _nodeEnv;

        private GameObject[] _allPanels;

        private void Start()
        {
            _allPanels = new[]
            {
                _scanningPanel, _installingNodePanel, _wsl2Panel,
                _detectingPanel, _installingClawPanel, _gatewayPanel,
                _agentsPanel, _workspacePanel, _completePanel, _errorPanel
            };

            HideAllPanels();

            if (_onboarding == null) return;

            // 상태 변경 구독
            _onboarding.StateChanged.Subscribe(OnStateChanged).AddTo(this);

            // 버튼 바인딩
            _retryButton?.onClick.AddListener(() => _onboarding.RetryCurrentStepAsync().Forget());
            _offlineButton?.onClick.AddListener(() => _onboarding.EnterOfflineMode().Forget());
            _skipButton?.onClick.AddListener(() => _onboarding.SkipWorkspaceSetupAsync().Forget());

            _gatewayConnectButton?.onClick.AddListener(() =>
            {
                var url = _gatewayUrlInput?.text ?? "";
                _onboarding.SubmitGatewayUrlAsync(url).Forget();
            });

            _workspaceSkipButton?.onClick.AddListener(() =>
                _onboarding.SkipWorkspaceSetupAsync().Forget());

            // 설치 진행률 바인딩
            if (_installer != null)
            {
                _installer.Progress.Subscribe(p =>
                {
                    if (_installProgressSlider != null) _installProgressSlider.value = p;
                }).AddTo(this);

                _installer.StatusText.Subscribe(t =>
                {
                    if (_installStatusText != null) _installStatusText.text = t;
                }).AddTo(this);
            }

            if (_nodeEnv != null)
            {
                _nodeEnv.Progress.Subscribe(p =>
                {
                    if (_installProgressSlider != null) _installProgressSlider.value = p;
                }).AddTo(this);

                _nodeEnv.StatusText.Subscribe(t =>
                {
                    if (_installStatusText != null) _installStatusText.text = t;
                }).AddTo(this);
            }
        }

        private void OnStateChanged(OnboardingState state)
        {
            HideAllPanels();

            // 진행률 업데이트
            var (progress, title, panel) = GetStateInfo(state);
            if (_progressBar != null) _progressBar.value = progress;
            if (_stepTitle != null)   _stepTitle.text     = title;
            if (panel != null)        panel.SetActive(true);

            // 에러 텍스트
            if (state == OnboardingState.FatalError ||
                state == OnboardingState.InstallFailed ||
                state == OnboardingState.NodeJsFailed)
            {
                if (_errorText != null)
                    _errorText.text = _onboarding.Context.LastErrorMessage;
            }

            // 버튼 가시성
            var canRetry = state == OnboardingState.InstallFailed ||
                           state == OnboardingState.NodeJsFailed ||
                           state == OnboardingState.OpenClawNotFound ||
                           state == OnboardingState.GatewayFailed;

            _retryButton?.gameObject.SetActive(canRetry);
            _offlineButton?.gameObject.SetActive(state == OnboardingState.GatewayFailed);
        }

        private (float progress, string title, GameObject panel) GetStateInfo(OnboardingState state)
        {
            return state switch
            {
                OnboardingState.Init or OnboardingState.CheckingFirstRun
                    => (0.05f, "초기화 중...", _scanningPanel),
                OnboardingState.ScanningEnvironment
                    => (0.1f, "시스템 환경 스캔 중...", _scanningPanel),
                OnboardingState.InstallingNodeJs or OnboardingState.NodeJsFailed
                    => (0.2f, "Node.js 설치", _installingNodePanel),
                OnboardingState.CheckingWsl2 or OnboardingState.InstallingWsl2 or OnboardingState.Wsl2NeedsReboot
                    => (0.3f, "WSL2 설정", _wsl2Panel),
                OnboardingState.DetectingOpenClaw
                    => (0.4f, "OpenClaw 감지 중...", _detectingPanel),
                OnboardingState.OpenClawNotFound or OnboardingState.InstallingOpenClaw or OnboardingState.InstallFailed
                    => (0.5f, "OpenClaw 설치", _installingClawPanel),
                OnboardingState.ConnectingGateway or OnboardingState.GatewayFailed or OnboardingState.WaitingForManualUrl
                    => (0.65f, "Gateway 연결", _gatewayPanel),
                OnboardingState.ParsingAgents or OnboardingState.NoAgentsFound
                    => (0.8f, "에이전트 감지", _agentsPanel),
                OnboardingState.WorkspaceSetup
                    => (0.9f, "워크스페이스 설정", _workspacePanel),
                OnboardingState.ReadyToEnter or OnboardingState.Completed
                    => (1f, "설정 완료!", _completePanel),
                OnboardingState.FatalError
                    => (0f, "오류 발생", _errorPanel),
                _ => (0f, "", null),
            };
        }

        private void HideAllPanels()
        {
            foreach (var panel in _allPanels)
            {
                if (panel != null) panel.SetActive(false);
            }
        }
    }
}
