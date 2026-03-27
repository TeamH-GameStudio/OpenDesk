using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;
using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Onboarding
{
    /// <summary>
    /// 온보딩 UI — App UI 버전 (3스텝 간소화)
    /// Step 1: 환경 스캔 / Step 2: AI 비서 설치 / Step 3: Gateway 연결
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class OnboardingAppUIController : MonoBehaviour
    {
        [Inject] private IOnboardingService      _onboarding;
        [Inject] private IOpenClawInstaller      _installer;
        [Inject] private INodeEnvironmentService _nodeEnv;

        // ── App UI 루트 요소들 ────────────────────────────────────────
        private Panel           _panel;
        private LinearProgress  _topProgress;
        private Text            _stepCountLabel;
        private Text            _titleLabel;
        private Text            _descLabel;

        // Step 1 — 환경 스캔
        private VisualElement   _scanStep;
        private CircularProgress _scanSpinner;
        private Text            _scanStatusText;

        // Step 2 — 설치
        private VisualElement   _installStep;
        private LinearProgress  _installProgress;
        private Text            _installStatusText;

        // Step 3 — Gateway 연결
        private VisualElement   _gatewayStep;
        private Unity.AppUI.UI.TextField _gatewayUrlField;
        private ActionButton    _gatewayConnectBtn;
        private ActionButton    _gatewayRetryBtn;
        private ActionButton    _offlineModeBtn;

        // 완료 / 에러 공통
        private VisualElement   _completeStep;
        private VisualElement   _errorStep;
        private Text            _errorText;
        private ActionButton    _retryBtn;
        private ActionButton    _enterBtn;

        private VisualElement[] _allSteps;

        // ================================================================
        //  초기화
        // ================================================================

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            BuildUI(doc.rootVisualElement);

            if (_onboarding == null)
            {
                Debug.LogError("[AppUI] _onboarding NULL — VContainer 주입 실패");
                return;
            }

            // 설치 진행률 바인딩
            _installer?.Progress.Subscribe(p => _installProgress.value = p).AddTo(this);
            _installer?.StatusText.Subscribe(t => _installStatusText.text = t).AddTo(this);
            _nodeEnv?.Progress.Subscribe(p => _installProgress.value = p).AddTo(this);
            _nodeEnv?.StatusText.Subscribe(t => _installStatusText.text = t).AddTo(this);

            // 상태 구독
            _onboarding.StateChanged.Subscribe(OnStateChanged).AddTo(this);
        }

        // ================================================================
        //  UI 구성 (순수 C# 코드-비하인드)
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            // ── Panel (App UI 루트 — 테마/스케일 컨텍스트 제공) ───────
            _panel = new Panel();
            _panel.theme = "dark";
            _panel.scale = "medium";
            _panel.style.flexGrow = 1;
            root.Add(_panel);

            // ── 전체 레이아웃 컨테이너 ─────────────────────────────────
            var container = new VisualElement();
            container.name = "onboarding-container";
            container.style.flexGrow    = 1;
            container.style.flexDirection = FlexDirection.Column;
            container.style.paddingTop    = new StyleLength(new Length(5, LengthUnit.Percent));
            container.style.paddingBottom = new StyleLength(new Length(5, LengthUnit.Percent));
            container.style.paddingLeft   = new StyleLength(new Length(10, LengthUnit.Percent));
            container.style.paddingRight  = new StyleLength(new Length(10, LengthUnit.Percent));
            _panel.Add(container);

            // ── 상단: 스텝 카운터 + 전체 진행바 ───────────────────────
            var headerRow = new VisualElement();
            headerRow.style.flexDirection  = FlexDirection.Row;
            headerRow.style.alignItems     = Align.Center;
            headerRow.style.marginBottom   = 16;

            _stepCountLabel = new Text { text = "" };
            _stepCountLabel.name = "step-count";
            _stepCountLabel.style.marginRight = 12;

            _topProgress = new LinearProgress();
            _topProgress.name = "top-progress";
            _topProgress.style.flexGrow = 1;
            _topProgress.value = 0f;

            headerRow.Add(_stepCountLabel);
            headerRow.Add(_topProgress);
            container.Add(headerRow);

            // ── 타이틀 ─────────────────────────────────────────────────
            _titleLabel = new Text { text = "준비하고 있어요" };
            _titleLabel.name = "onboarding-title";
            _titleLabel.size = TextSize.XL;
            _titleLabel.primary = true;
            _titleLabel.style.marginBottom = 8;
            container.Add(_titleLabel);

            // ── 설명 ───────────────────────────────────────────────────
            _descLabel = new Text { text = "" };
            _descLabel.name = "onboarding-desc";
            _descLabel.style.marginBottom = 32;
            _descLabel.style.whiteSpace   = WhiteSpace.Normal;
            container.Add(_descLabel);

            // ── Step 패널들 ────────────────────────────────────────────
            _scanStep    = BuildScanStep();
            _installStep = BuildInstallStep();
            _gatewayStep = BuildGatewayStep();
            _completeStep = BuildCompleteStep();
            _errorStep   = BuildErrorStep();

            container.Add(_scanStep);
            container.Add(_installStep);
            container.Add(_gatewayStep);
            container.Add(_completeStep);
            container.Add(_errorStep);

            _allSteps = new[] { _scanStep, _installStep, _gatewayStep, _completeStep, _errorStep };
            HideAllSteps();
        }

        // ── Step 1: 환경 스캔 ──────────────────────────────────────────
        private VisualElement BuildScanStep()
        {
            var step = new VisualElement();
            step.name = "scan-step";
            step.style.flexDirection = FlexDirection.Column;
            step.style.alignItems    = Align.Center;

            _scanSpinner = new CircularProgress();
            _scanSpinner.name = "scan-spinner";
            _scanSpinner.style.width  = 64;
            _scanSpinner.style.height = 64;
            _scanSpinner.style.marginBottom = 16;

            _scanStatusText = new Text { text = "시스템 환경을 확인하고 있어요..." };
            _scanStatusText.name = "scan-status";

            step.Add(_scanSpinner);
            step.Add(_scanStatusText);
            return step;
        }

        // ── Step 2: 설치 진행 ──────────────────────────────────────────
        private VisualElement BuildInstallStep()
        {
            var step = new VisualElement();
            step.name = "install-step";
            step.style.flexDirection = FlexDirection.Column;

            var progressLabel = new Text { text = "설치 진행률" };
            progressLabel.style.marginBottom = 8;

            _installProgress = new LinearProgress();
            _installProgress.name = "install-progress";
            _installProgress.style.marginBottom = 12;

            _installStatusText = new Text { text = "준비 중..." };
            _installStatusText.name = "install-status";

            step.Add(progressLabel);
            step.Add(_installProgress);
            step.Add(_installStatusText);
            return step;
        }

        // ── Step 3: Gateway 연결 ───────────────────────────────────────
        private VisualElement BuildGatewayStep()
        {
            var step = new VisualElement();
            step.name = "gateway-step";
            step.style.flexDirection = FlexDirection.Column;

            // 자동 연결 중 스피너 (ConnectingGateway)
            var connectingRow = new VisualElement();
            connectingRow.name = "gateway-connecting";
            connectingRow.style.flexDirection = FlexDirection.Row;
            connectingRow.style.alignItems    = Align.Center;
            connectingRow.style.marginBottom  = 16;

            var spinner = new CircularProgress();
            spinner.style.width  = 24;
            spinner.style.height = 24;
            spinner.style.marginRight = 8;

            var connectingText = new Text { text = "AI 비서와 연결하는 중..." };
            connectingRow.Add(spinner);
            connectingRow.Add(connectingText);
            step.Add(connectingRow);

            // URL 직접 입력 (GatewayFailed / WaitingForManualUrl)
            _gatewayUrlField = new Unity.AppUI.UI.TextField
            {
                placeholder = "http://localhost:3000"
            };
            _gatewayUrlField.name = "gateway-url";
            _gatewayUrlField.style.marginBottom = 12;
            step.Add(_gatewayUrlField);

            // 버튼 행
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;

            _gatewayConnectBtn = new ActionButton { label = "연결" };
            _gatewayConnectBtn.name   = "gateway-connect";
            _gatewayConnectBtn.accent = true;
            _gatewayConnectBtn.style.marginRight = 8;
            _gatewayConnectBtn.clicked += () =>
                _onboarding.SubmitGatewayUrlAsync(_gatewayUrlField.value).Forget();

            _gatewayRetryBtn = new ActionButton { label = "다시 시도" };
            _gatewayRetryBtn.name = "gateway-retry";
            _gatewayRetryBtn.style.marginRight = 8;
            _gatewayRetryBtn.clicked += () =>
                _onboarding.RetryCurrentStepAsync().Forget();

            _offlineModeBtn = new ActionButton { label = "오프라인으로 시작" };
            _offlineModeBtn.name   = "offline-mode";
            _offlineModeBtn.accent = true;
            _offlineModeBtn.clicked += () =>
                _onboarding.EnterOfflineMode().Forget();

            btnRow.Add(_gatewayConnectBtn);
            btnRow.Add(_gatewayRetryBtn);
            btnRow.Add(_offlineModeBtn);
            step.Add(btnRow);

            return step;
        }

        // ── 완료 ───────────────────────────────────────────────────────
        private VisualElement BuildCompleteStep()
        {
            var step = new VisualElement();
            step.name = "complete-step";
            step.style.flexDirection = FlexDirection.Column;
            step.style.alignItems    = Align.Center;

            var icon = new Icon { iconName = "check_circle" };
            icon.style.width  = 64;
            icon.style.height = 64;
            icon.style.marginBottom = 16;

            _enterBtn = new ActionButton { label = "시작하기" };
            _enterBtn.name   = "enter-btn";
            _enterBtn.accent = true;
            _enterBtn.clicked += () =>
                _onboarding.SkipWorkspaceSetupAsync().Forget();

            step.Add(icon);
            step.Add(_enterBtn);
            return step;
        }

        // ── 에러 ───────────────────────────────────────────────────────
        private VisualElement BuildErrorStep()
        {
            var step = new VisualElement();
            step.name = "error-step";
            step.style.flexDirection = FlexDirection.Column;

            _errorText = new Text { text = "" };
            _errorText.name = "error-text";
            _errorText.style.marginBottom = 16;
            _errorText.style.whiteSpace   = WhiteSpace.Normal;

            _retryBtn = new ActionButton { label = "다시 시도" };
            _retryBtn.name = "retry-btn";
            _retryBtn.clicked += () =>
                _onboarding.RetryCurrentStepAsync().Forget();

            step.Add(_errorText);
            step.Add(_retryBtn);
            return step;
        }

        // ================================================================
        //  상태 변경 핸들러
        // ================================================================

        private void OnStateChanged(OnboardingState state)
        {
            HideAllSteps();
            ResetGatewayStepVisibility();

            var info = GetStepInfo(state);
            _topProgress.value    = info.Progress;
            _stepCountLabel.text  = info.StepCount;
            _titleLabel.text      = info.Title;
            _descLabel.text       = info.Description;

            switch (state)
            {
                // ── Step 1: 환경 스캔 ──────────────────────────────
                case OnboardingState.Init:
                case OnboardingState.CheckingFirstRun:
                case OnboardingState.ScanningEnvironment:
                case OnboardingState.DetectingOpenClaw:
                case OnboardingState.CheckingWsl2:
                    _scanStatusText.text = GetScanStatusText(state);
                    _scanStep.style.display = DisplayStyle.Flex;
                    break;

                // ── Step 2: 설치 진행 ───────────────────────────────
                case OnboardingState.InstallingNodeJs:
                case OnboardingState.InstallingWsl2:
                case OnboardingState.OpenClawNotFound:
                case OnboardingState.InstallingOpenClaw:
                    _installStep.style.display = DisplayStyle.Flex;
                    break;

                // ── Step 3: Gateway 연결 ────────────────────────────
                case OnboardingState.ConnectingGateway:
                    _gatewayStep.style.display = DisplayStyle.Flex;
                    SetGatewayMode(connecting: true, failed: false, manualUrl: false);
                    break;

                case OnboardingState.GatewayFailed:
                    _gatewayStep.style.display = DisplayStyle.Flex;
                    SetGatewayMode(connecting: false, failed: true, manualUrl: false);
                    ShowErrorToast("AI 비서 연결에 실패했어요.");
                    break;

                case OnboardingState.WaitingForManualUrl:
                    _gatewayStep.style.display = DisplayStyle.Flex;
                    SetGatewayMode(connecting: false, failed: false, manualUrl: true);
                    break;

                // ── 완료 ────────────────────────────────────────────
                case OnboardingState.ParsingAgents:
                case OnboardingState.NoAgentsFound:
                case OnboardingState.WorkspaceSetup:
                case OnboardingState.ReadyToEnter:
                case OnboardingState.Completed:
                    _completeStep.style.display = DisplayStyle.Flex;
                    if (state == OnboardingState.ReadyToEnter || state == OnboardingState.Completed)
                        ShowSuccessToast("모든 준비가 완료되었어요!");
                    break;

                // ── 에러 ────────────────────────────────────────────
                case OnboardingState.InstallFailed:
                case OnboardingState.NodeJsFailed:
                case OnboardingState.FatalError:
                    _errorText.text = _onboarding.Context.LastErrorMessage;
                    _errorStep.style.display = DisplayStyle.Flex;
                    ShowErrorToast("문제가 발생했어요. 다시 시도해주세요.");
                    break;
            }
        }

        // ================================================================
        //  Gateway 스텝 가시성 제어
        // ================================================================

        private void ResetGatewayStepVisibility()
        {
            var connectingRow = _gatewayStep.Q("gateway-connecting");
            if (connectingRow != null) connectingRow.style.display = DisplayStyle.None;
            _gatewayUrlField.style.display    = DisplayStyle.None;
            _gatewayConnectBtn.style.display  = DisplayStyle.None;
            _gatewayRetryBtn.style.display    = DisplayStyle.None;
            _offlineModeBtn.style.display     = DisplayStyle.None;
        }

        private void SetGatewayMode(bool connecting, bool failed, bool manualUrl)
        {
            var connectingRow = _gatewayStep.Q("gateway-connecting");
            if (connectingRow != null)
                connectingRow.style.display = connecting ? DisplayStyle.Flex : DisplayStyle.None;

            _gatewayUrlField.style.display   = (failed || manualUrl) ? DisplayStyle.Flex : DisplayStyle.None;
            _gatewayConnectBtn.style.display = (failed || manualUrl) ? DisplayStyle.Flex : DisplayStyle.None;
            _gatewayRetryBtn.style.display   = failed ? DisplayStyle.Flex : DisplayStyle.None;
            _offlineModeBtn.style.display    = failed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        //  Toast 알림
        // ================================================================

        private void ShowSuccessToast(string message)
        {
            Toast.Build(_panel, message, NotificationDuration.Long)
                .SetStyle(NotificationStyle.Positive)
                .Show();
        }

        private void ShowErrorToast(string message)
        {
            Toast.Build(_panel, message, NotificationDuration.Long)
                .SetStyle(NotificationStyle.Negative)
                .Show();
        }

        // ================================================================
        //  스텝 정보
        // ================================================================

        private struct StepInfo
        {
            public float  Progress;
            public string StepCount;
            public string Title;
            public string Description;
        }

        private StepInfo GetStepInfo(OnboardingState state) => state switch
        {
            OnboardingState.Init or OnboardingState.CheckingFirstRun => new StepInfo
            {
                Progress    = 0.05f,
                StepCount   = "",
                Title       = "준비하고 있어요",
                Description = "AI 비서 환경을 확인하고 있습니다.",
            },
            OnboardingState.ScanningEnvironment
            or OnboardingState.CheckingWsl2
            or OnboardingState.DetectingOpenClaw => new StepInfo
            {
                Progress    = 0.15f,
                StepCount   = "Step 1 / 3",
                Title       = "컴퓨터 환경 확인 중",
                Description = "AI 비서가 작동하기 위해 필요한 도구들이 있는지 확인하고 있어요.\n약 10초 소요됩니다.",
            },
            OnboardingState.InstallingNodeJs
            or OnboardingState.InstallingWsl2
            or OnboardingState.OpenClawNotFound
            or OnboardingState.InstallingOpenClaw => new StepInfo
            {
                Progress    = 0.50f,
                StepCount   = "Step 2 / 3",
                Title       = "AI 비서 설치 중",
                Description = "AI 비서 프로그램을 다운로드하고 설치하고 있어요.\n인터넷 속도에 따라 2~5분 소요될 수 있습니다.",
            },
            OnboardingState.ConnectingGateway
            or OnboardingState.GatewayFailed
            or OnboardingState.WaitingForManualUrl => new StepInfo
            {
                Progress    = 0.75f,
                StepCount   = "Step 3 / 3",
                Title       = "AI 비서 연결 중",
                Description = "설치된 AI 비서와 이 프로그램을 연결하고 있어요.",
            },
            OnboardingState.ParsingAgents
            or OnboardingState.NoAgentsFound
            or OnboardingState.WorkspaceSetup
            or OnboardingState.ReadyToEnter
            or OnboardingState.Completed => new StepInfo
            {
                Progress    = 1f,
                StepCount   = "",
                Title       = "모든 준비가 완료되었어요!",
                Description = "AI 비서 환경이 성공적으로 구축되었습니다.\n아래 버튼을 눌러 시작하세요!",
            },
            OnboardingState.InstallFailed
            or OnboardingState.NodeJsFailed
            or OnboardingState.FatalError => new StepInfo
            {
                Progress    = 0f,
                StepCount   = "",
                Title       = "문제가 발생했어요",
                Description = "예상치 못한 문제가 생겼어요.\n아래 버튼을 눌러 다시 시도해주세요.",
            },
            _ => new StepInfo { Progress = 0f, StepCount = "", Title = "", Description = "" },
        };

        private static string GetScanStatusText(OnboardingState state) => state switch
        {
            OnboardingState.ScanningEnvironment => "시스템 환경을 확인하고 있어요...",
            OnboardingState.CheckingWsl2        => "WSL2 설치 여부를 확인하고 있어요...",
            OnboardingState.DetectingOpenClaw   => "AI 비서 프로그램을 찾고 있어요...",
            _                                   => "준비 중...",
        };

        private void HideAllSteps()
        {
            if (_allSteps == null) return;
            foreach (var s in _allSteps)
                s.style.display = DisplayStyle.None;
        }
    }
}
