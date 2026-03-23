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
    /// 온보딩 전체 UI 컨트롤러 — 비전공자 친화 버전
    /// OnboardingService 상태에 따라 패널 전환 + 친절한 설명 제공
    /// </summary>
    public class OnboardingUIController : MonoBehaviour
    {
        // ── 전체 진행률 ────────────────────────────────────────────────
        [Header("전체 진행률")]
        [SerializeField] private Slider   _progressBar;
        [SerializeField] private TMP_Text _stepTitle;
        [SerializeField] private TMP_Text _stepCountText;     // "Step 2 / 5"

        // ── 단계 설명 영역 ─────────────────────────────────────────────
        [Header("단계 설명")]
        [SerializeField] private TMP_Text   _descriptionText;
        [SerializeField] private TMP_Text   _estimatedTimeText;
        [SerializeField] private Button     _whyNeededToggle;
        [SerializeField] private GameObject _whyNeededPanel;
        [SerializeField] private TMP_Text   _whyNeededText;

        // ── 스텝 패널 (순서대로) ───────────────────────────────────────
        [Header("스텝 패널")]
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

        // ── Node.js 버전 충돌 패널 ────────────────────────────────────
        [Header("Node.js 버전 충돌")]
        [SerializeField] private GameObject _nodeUpgradePanel;
        [SerializeField] private TMP_Text   _nodeVersionText;
        [SerializeField] private TMP_Text   _nodeProjectListText;
        [SerializeField] private Button     _nodeSafeInstallButton;
        [SerializeField] private Button     _nodeOverwriteButton;
        [SerializeField] private Button     _nodeSkipButton;

        // ── 공통 버튼 ─────────────────────────────────────────────────
        [Header("공통 버튼")]
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _offlineButton;

        // ── Gateway 패널 ──────────────────────────────────────────────
        [Header("Gateway 패널")]
        [SerializeField] private TMP_InputField _gatewayUrlInput;
        [SerializeField] private Button         _gatewayConnectButton;

        // ── Workspace 패널 ────────────────────────────────────────────
        [Header("Workspace 패널")]
        [SerializeField] private TMP_InputField _workspacePathInput;
        [SerializeField] private Button         _workspaceBrowseButton;
        [SerializeField] private Button         _workspaceSkipButton;
        [SerializeField] private Button         _workspaceConfirmButton;

        // ── WSL2 재시작 패널 ──────────────────────────────────────────
        [Header("WSL2 재시작")]
        [SerializeField] private Button _rebootNowButton;
        [SerializeField] private Button _rebootLaterButton;

        // ── 완료 패널 ─────────────────────────────────────────────────
        [Header("완료 패널")]
        [SerializeField] private Button _enterOfficeButton;

        // ── 에러 ──────────────────────────────────────────────────────
        [Header("에러")]
        [SerializeField] private TMP_Text   _errorText;
        [SerializeField] private Button     _errorDetailToggle;
        [SerializeField] private GameObject _errorDetailPanel;

        // ── 설치 진행 ─────────────────────────────────────────────────
        [Header("설치 진행")]
        [SerializeField] private Slider   _installProgressSlider;
        [SerializeField] private TMP_Text _installStatusText;

        // ── DI ────────────────────────────────────────────────────────
        [Inject] private IOnboardingService      _onboarding;
        [Inject] private IOpenClawInstaller      _installer;
        [Inject] private INodeEnvironmentService _nodeEnv;

        private GameObject[] _allPanels;
        private bool _whyNeededVisible;
        private bool _errorDetailVisible;

        // ================================================================
        //  초기화
        // ================================================================

        private void Start()
        {
            Debug.Log("[UI] OnboardingUIController.Start() 진입");

            _allPanels = new[]
            {
                _scanningPanel, _nodeUpgradePanel, _installingNodePanel, _wsl2Panel,
                _detectingPanel, _installingClawPanel, _gatewayPanel,
                _agentsPanel, _workspacePanel, _completePanel, _errorPanel
            };

            // Inspector 바인딩 상태 확인
            int nullCount = 0;
            for (int i = 0; i < _allPanels.Length; i++)
                if (_allPanels[i] == null) nullCount++;
            Debug.Log($"[UI] 패널 바인딩: {_allPanels.Length - nullCount}/{_allPanels.Length} 연결됨");
            Debug.Log($"[UI] _progressBar={(_progressBar != null ? "OK" : "NULL")}, _stepTitle={(_stepTitle != null ? "OK" : "NULL")}, _descriptionText={(_descriptionText != null ? "OK" : "NULL")}");

            HideAllPanels();
            if (_whyNeededPanel != null) _whyNeededPanel.SetActive(false);
            if (_errorDetailPanel != null) _errorDetailPanel.SetActive(false);

            // VContainer 주입 확인
            Debug.Log($"[UI] [Inject] _onboarding={(_onboarding != null ? "OK" : "NULL")}, _installer={(_installer != null ? "OK" : "NULL")}, _nodeEnv={(_nodeEnv != null ? "OK" : "NULL")}");

            if (_onboarding == null)
            {
                Debug.LogError("[UI] _onboarding이 NULL — VContainer 주입 실패! OnboardingInstaller에 RegisterComponentInHierarchy<OnboardingUIController>() 확인 필요");
                return;
            }

            Debug.Log("[UI] VContainer 주입 성공 — 상태 구독 시작");

            // 상태 변경 구독
            _onboarding.StateChanged.Subscribe(OnStateChanged).AddTo(this);

            // ── 버튼 바인딩 ────────────────────────────────────────────
            _retryButton?.onClick.AddListener(() =>
                _onboarding.RetryCurrentStepAsync().Forget());

            _offlineButton?.onClick.AddListener(() =>
                _onboarding.EnterOfflineMode().Forget());

            // Node.js 버전 충돌 선택 버튼
            _nodeSafeInstallButton?.onClick.AddListener(() =>
                _onboarding.HandleNodeUpgrade_SafeInstall().Forget());
            _nodeOverwriteButton?.onClick.AddListener(() =>
                _onboarding.HandleNodeUpgrade_Overwrite().Forget());
            _nodeSkipButton?.onClick.AddListener(() =>
                _onboarding.HandleNodeUpgrade_Skip().Forget());

            _gatewayConnectButton?.onClick.AddListener(() =>
            {
                var url = _gatewayUrlInput?.text ?? "";
                _onboarding.SubmitGatewayUrlAsync(url).Forget();
            });

            // 워크스페이스 — 폴더 선택
            _workspaceBrowseButton?.onClick.AddListener(OpenFolderDialog);

            _workspaceSkipButton?.onClick.AddListener(() =>
                _onboarding.SkipWorkspaceSetupAsync().Forget());

            _workspaceConfirmButton?.onClick.AddListener(() =>
            {
                var path = _workspacePathInput?.text ?? "";
                if (!string.IsNullOrWhiteSpace(path))
                    _onboarding.ConfirmWorkspacePathAsync(path).Forget();
            });

            // WSL2 재시작
            _rebootNowButton?.onClick.AddListener(() => _onboarding.RequestReboot());
            _rebootLaterButton?.onClick.AddListener(() =>
                Debug.Log("[UI] 사용자가 나중에 재시작 선택 — 앱 종료 안내"));

            // 완료 → Office 진입 (OnboardingBootstrapper가 처리하지만 버튼도 제공)
            _enterOfficeButton?.onClick.AddListener(() => { /* Bootstrapper가 ReadyToEnter 감지 */ });

            // "왜 필요한가요?" 토글
            _whyNeededToggle?.onClick.AddListener(ToggleWhyNeeded);

            // 에러 상세 토글
            _errorDetailToggle?.onClick.AddListener(ToggleErrorDetail);

            // ── 설치 진행률 바인딩 ─────────────────────────────────────
            BindInstallerProgress();
        }

        // ================================================================
        //  상태 변경 핸들러
        // ================================================================

        private void OnStateChanged(OnboardingState state)
        {
            HideAllPanels();
            _whyNeededVisible = false;
            if (_whyNeededPanel != null) _whyNeededPanel.SetActive(false);

            var info = GetStateInfo(state);

            // 전체 진행률
            if (_progressBar != null)    _progressBar.value   = info.Progress;
            if (_stepTitle != null)      _stepTitle.text      = info.Title;
            if (_stepCountText != null)  _stepCountText.text  = info.StepCount;

            // 설명 텍스트
            if (_descriptionText != null)   _descriptionText.text   = info.Description;
            if (_estimatedTimeText != null)
            {
                _estimatedTimeText.text = info.EstimatedTime;
                _estimatedTimeText.gameObject.SetActive(!string.IsNullOrEmpty(info.EstimatedTime));
            }

            // "왜 필요한가요?" 버튼 표시/숨김
            if (_whyNeededToggle != null)
                _whyNeededToggle.gameObject.SetActive(!string.IsNullOrEmpty(info.WhyNeeded));
            if (_whyNeededText != null)
                _whyNeededText.text = info.WhyNeeded;

            // 패널 활성화
            if (info.Panel != null) info.Panel.SetActive(true);

            // Node.js 버전 충돌 — 동적 콘텐츠 채우기
            if (state == OnboardingState.NodeUpgradeChoice)
            {
                var ctx = _onboarding.Context;
                if (_nodeVersionText != null)
                    _nodeVersionText.text = $"현재 설치된 버전: v{ctx.ExistingNodeVersion}";

                if (_nodeProjectListText != null)
                {
                    if (ctx.NodeProjectPaths.Count == 0)
                    {
                        _nodeProjectListText.text = "Node.js를 사용 중인 프로젝트를 찾지 못했어요.\n안심하고 업그레이드할 수 있습니다.";
                    }
                    else
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"이 컴퓨터에서 Node.js를 사용 중인 프로젝트 {ctx.NodeProjectPaths.Count}개를 발견했어요:\n");
                        foreach (var path in ctx.NodeProjectPaths)
                            sb.AppendLine($"  {path}");
                        sb.AppendLine("\n위 프로젝트들이 업그레이드 후 영향을 받을 수 있어요.");
                        _nodeProjectListText.text = sb.ToString();
                    }
                }
            }

            // 에러 텍스트
            if (state is OnboardingState.FatalError or OnboardingState.InstallFailed
                or OnboardingState.NodeJsFailed or OnboardingState.GatewayFailed)
            {
                if (_errorText != null)
                    _errorText.text = _onboarding.Context.LastErrorMessage;
            }

            // 버튼 가시성
            var canRetry = state is OnboardingState.InstallFailed
                or OnboardingState.NodeJsFailed
                or OnboardingState.GatewayFailed;

            _retryButton?.gameObject.SetActive(canRetry);
            _offlineButton?.gameObject.SetActive(state == OnboardingState.GatewayFailed);

            // 설치 진행바 표시 (설치 중인 상태에서만)
            var showInstallProgress = state is OnboardingState.InstallingNodeJs
                or OnboardingState.InstallingOpenClaw
                or OnboardingState.InstallingWsl2;

            if (_installProgressSlider != null)
                _installProgressSlider.gameObject.SetActive(showInstallProgress);
            if (_installStatusText != null)
                _installStatusText.gameObject.SetActive(showInstallProgress);

            // WSL2 재시작 버튼
            var showReboot = state == OnboardingState.Wsl2NeedsReboot;
            _rebootNowButton?.gameObject.SetActive(showReboot);
            _rebootLaterButton?.gameObject.SetActive(showReboot);
        }

        // ================================================================
        //  상태별 콘텐츠 정의 (비전공자 친화)
        // ================================================================

        private struct StateInfo
        {
            public float      Progress;
            public string     Title;
            public string     StepCount;
            public string     Description;
            public string     EstimatedTime;
            public string     WhyNeeded;
            public GameObject Panel;
        }

        private StateInfo GetStateInfo(OnboardingState state)
        {
            return state switch
            {
                OnboardingState.Init or OnboardingState.CheckingFirstRun => new StateInfo
                {
                    Progress      = 0.05f,
                    Title         = "준비하고 있어요",
                    StepCount     = "",
                    Description   = "AI 비서 환경을 확인하고 있습니다.",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = _scanningPanel,
                },

                OnboardingState.ScanningEnvironment => new StateInfo
                {
                    Progress      = 0.10f,
                    Title         = "컴퓨터 환경 확인 중",
                    StepCount     = "Step 1 / 5",
                    Description   = "AI 비서가 작동하기 위해 필요한 도구들이\n있는지 확인하고 있어요.",
                    EstimatedTime = "약 10초",
                    WhyNeeded     = "",
                    Panel         = _scanningPanel,
                },

                OnboardingState.NodeUpgradeChoice => new StateInfo
                {
                    Progress      = 0.15f,
                    Title         = "기존 도구 업그레이드 필요",
                    StepCount     = "Step 1 / 5",
                    Description   = "이 컴퓨터에 이미 설치된 도구의 버전이 낮아요.\nAI 비서를 사용하려면 최신 버전이 필요합니다.",
                    EstimatedTime = "약 2~3분",
                    WhyNeeded     = "Node.js는 AI 프로그램의 기반 도구예요.\n현재 설치된 버전이 오래되어 AI 비서가 제대로 작동하지 않을 수 있어요.\n\n'업그레이드'를 누르면 최신 버전으로 자동 교체됩니다.\n보안 확인 창이 뜨면 '예'를 눌러주세요.",
                    Panel         = _nodeUpgradePanel,
                },

                OnboardingState.InstallingNodeJs => new StateInfo
                {
                    Progress      = 0.20f,
                    Title         = "필수 도구 설치 중",
                    StepCount     = "Step 1 / 5",
                    Description   = "AI 비서가 작동하려면 기본 실행 환경이 필요해요.\n지금 자동으로 설치하고 있습니다.",
                    EstimatedTime = "약 2~3분",
                    WhyNeeded     = "Node.js는 AI 프로그램을 실행시키는 데 꼭 필요한 기반 도구예요.\n웹 브라우저처럼, AI가 돌아가는 '무대'를 만들어주는 역할을 합니다.\n한 번 설치하면 자동으로 관리되니 신경 쓸 필요 없어요.",
                    Panel         = _installingNodePanel,
                },

                OnboardingState.NodeJsFailed => new StateInfo
                {
                    Progress      = 0.20f,
                    Title         = "도구 설치 문제",
                    StepCount     = "Step 1 / 5",
                    Description   = "필수 도구 설치 중 문제가 발생했어요.\n인터넷 연결을 확인하고 아래 버튼을 눌러주세요.",
                    EstimatedTime = "",
                    WhyNeeded     = "Node.js는 AI 프로그램을 실행시키는 데 꼭 필요한 기반 도구예요.\n웹 브라우저처럼, AI가 돌아가는 '무대'를 만들어주는 역할을 합니다.",
                    Panel         = _installingNodePanel,
                },

                OnboardingState.CheckingWsl2 => new StateInfo
                {
                    Progress      = 0.30f,
                    Title         = "호환성 환경 확인 중",
                    StepCount     = "Step 2 / 5",
                    Description   = "Windows에서 AI를 안정적으로 실행하기 위한\n환경을 확인하고 있어요.",
                    EstimatedTime = "약 10초",
                    WhyNeeded     = "WSL은 Windows 안에서 AI 도구들이 안정적으로 돌아갈 수 있게\n해주는 호환 기능이에요. 마치 통역사처럼, AI 도구와 Windows가\n서로 원활하게 대화할 수 있도록 도와줍니다.",
                    Panel         = _wsl2Panel,
                },

                OnboardingState.InstallingWsl2 => new StateInfo
                {
                    Progress      = 0.30f,
                    Title         = "호환성 환경 설치 중",
                    StepCount     = "Step 2 / 5",
                    Description   = "AI가 안정적으로 작동하기 위한 환경을 설치하고 있어요.\nWindows 보안 확인 창이 뜨면 '예'를 눌러주세요.",
                    EstimatedTime = "약 3~5분",
                    WhyNeeded     = "WSL은 Windows 안에서 AI 도구들이 안정적으로 돌아갈 수 있게\n해주는 호환 기능이에요. 마치 통역사처럼, AI 도구와 Windows가\n서로 원활하게 대화할 수 있도록 도와줍니다.",
                    Panel         = _wsl2Panel,
                },

                OnboardingState.Wsl2NeedsReboot => new StateInfo
                {
                    Progress      = 0.35f,
                    Title         = "컴퓨터 재시작 필요",
                    StepCount     = "Step 2 / 5",
                    Description   = "방금 설치한 기능을 적용하려면 컴퓨터를 한 번 재시작해야 해요.\n재시작 후 이 프로그램을 다시 실행하면 이어서 진행됩니다.\n(이전 단계는 다시 하지 않아요!)",
                    EstimatedTime = "",
                    WhyNeeded     = "새로 설치한 기능은 컴퓨터를 껐다 켜야 적용됩니다.\n재시작 후에는 이 단계를 건너뛰고 바로 다음으로 넘어가요.",
                    Panel         = _wsl2Panel,
                },

                OnboardingState.DetectingOpenClaw => new StateInfo
                {
                    Progress      = 0.40f,
                    Title         = "AI 비서 확인 중",
                    StepCount     = "Step 3 / 5",
                    Description   = "AI 비서 프로그램이 이미 설치되어 있는지\n확인하고 있어요.",
                    EstimatedTime = "약 5초",
                    WhyNeeded     = "",
                    Panel         = _detectingPanel,
                },

                OnboardingState.OpenClawNotFound or OnboardingState.InstallingOpenClaw => new StateInfo
                {
                    Progress      = 0.50f,
                    Title         = "AI 비서 설치 중",
                    StepCount     = "Step 3 / 5",
                    Description   = "AI 비서 프로그램을 다운로드하고 설치하고 있어요.\n인터넷 속도에 따라 시간이 달라질 수 있습니다.",
                    EstimatedTime = "약 2~5분",
                    WhyNeeded     = "OpenClaw은 여러 AI 모델(ChatGPT, Claude 등)을 하나로 묶어서\n관리하는 프로그램이에요. 마치 비서실장처럼, 다양한 AI 비서들을\n관리하고 일을 나누어 주는 역할을 합니다.",
                    Panel         = _installingClawPanel,
                },

                OnboardingState.InstallFailed => new StateInfo
                {
                    Progress      = 0.50f,
                    Title         = "설치 문제 발생",
                    StepCount     = "Step 3 / 5",
                    Description   = "AI 비서 설치 중 문제가 발생했어요.\n인터넷 연결을 확인하고 아래 버튼을 눌러주세요.",
                    EstimatedTime = "",
                    WhyNeeded     = "OpenClaw은 여러 AI 모델(ChatGPT, Claude 등)을 하나로 묶어서\n관리하는 프로그램이에요. 마치 비서실장처럼, 다양한 AI 비서들을\n관리하고 일을 나누어 주는 역할을 합니다.",
                    Panel         = _installingClawPanel,
                },

                OnboardingState.ConnectingGateway => new StateInfo
                {
                    Progress      = 0.65f,
                    Title         = "AI 비서 연결 중",
                    StepCount     = "Step 4 / 5",
                    Description   = "설치된 AI 비서와 이 프로그램을 연결하고 있어요.\n잠시만 기다려주세요.",
                    EstimatedTime = "약 10초",
                    WhyNeeded     = "AI 비서가 이 프로그램과 대화하려면 전용 통신 채널이 필요해요.\n마치 전화선을 연결하는 것처럼, 지금 그 연결을 만들고 있습니다.",
                    Panel         = _gatewayPanel,
                },

                OnboardingState.GatewayFailed => new StateInfo
                {
                    Progress      = 0.65f,
                    Title         = "연결 문제",
                    StepCount     = "Step 4 / 5",
                    Description   = "AI 비서와의 연결에 실패했어요.\nAI 비서가 아직 준비되지 않았을 수 있습니다.",
                    EstimatedTime = "",
                    WhyNeeded     = "AI 비서가 이 프로그램과 대화하려면 전용 통신 채널이 필요해요.\n마치 전화선을 연결하는 것처럼, 지금 그 연결을 만들고 있습니다.",
                    Panel         = _gatewayPanel,
                },

                OnboardingState.WaitingForManualUrl => new StateInfo
                {
                    Progress      = 0.65f,
                    Title         = "연결 주소 입력",
                    StepCount     = "Step 4 / 5",
                    Description   = "AI 비서의 연결 주소를 직접 입력해주세요.\n일반적으로 이 단계는 필요하지 않습니다.",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = _gatewayPanel,
                },

                OnboardingState.ParsingAgents => new StateInfo
                {
                    Progress      = 0.80f,
                    Title         = "AI 비서 설정 확인 중",
                    StepCount     = "Step 5 / 5",
                    Description   = "AI 비서의 설정을 읽고 있어요.",
                    EstimatedTime = "약 3초",
                    WhyNeeded     = "",
                    Panel         = _agentsPanel,
                },

                OnboardingState.NoAgentsFound => new StateInfo
                {
                    Progress      = 0.80f,
                    Title         = "기본 설정 적용 중",
                    StepCount     = "Step 5 / 5",
                    Description   = "AI 비서 설정을 찾지 못했어요.\n기본 설정으로 시작합니다.",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = _agentsPanel,
                },

                OnboardingState.WorkspaceSetup => new StateInfo
                {
                    Progress      = 0.90f,
                    Title         = "작업 폴더 선택",
                    StepCount     = "Step 5 / 5",
                    Description   = "AI 비서가 작업할 폴더를 선택해주세요.\nAI가 이 폴더의 파일을 읽고 관리할 수 있게 됩니다.\n나중에 설정에서 언제든 변경할 수 있어요.",
                    EstimatedTime = "",
                    WhyNeeded     = "작업 폴더는 AI 비서의 '책상' 같은 곳이에요.\n여기에 놓인 파일들을 AI가 읽고, 정리하고, 수정할 수 있습니다.\n지정하지 않아도 기본 기능은 사용할 수 있어요.",
                    Panel         = _workspacePanel,
                },

                OnboardingState.ReadyToEnter or OnboardingState.Completed => new StateInfo
                {
                    Progress      = 1f,
                    Title         = "모든 준비가 완료되었어요!",
                    StepCount     = "",
                    Description   = "AI 비서 환경이 성공적으로 구축되었습니다.\n아래 버튼을 눌러 시작하세요!",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = _completePanel,
                },

                OnboardingState.FatalError => new StateInfo
                {
                    Progress      = 0f,
                    Title         = "문제가 발생했어요",
                    StepCount     = "",
                    Description   = "예상치 못한 문제가 생겼어요.\n프로그램을 닫고 다시 실행해주세요.",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = _errorPanel,
                },

                _ => new StateInfo
                {
                    Progress      = 0f,
                    Title         = "",
                    StepCount     = "",
                    Description   = "",
                    EstimatedTime = "",
                    WhyNeeded     = "",
                    Panel         = null,
                },
            };
        }

        // ================================================================
        //  유틸리티
        // ================================================================

        private void ToggleWhyNeeded()
        {
            _whyNeededVisible = !_whyNeededVisible;
            if (_whyNeededPanel != null)
                _whyNeededPanel.SetActive(_whyNeededVisible);
        }

        private void ToggleErrorDetail()
        {
            _errorDetailVisible = !_errorDetailVisible;
            if (_errorDetailPanel != null)
                _errorDetailPanel.SetActive(_errorDetailVisible);
        }

        private void OpenFolderDialog()
        {
#if UNITY_STANDALONE_WIN
            // Windows 네이티브 폴더 다이얼로그 (SHBrowseForFolder)
            // StandaloneFileBrowser 패키지가 있으면 사용, 없으면 기본 경로 제안
            try
            {
                // 방법 1: StandaloneFileBrowser (설치 시)
                // var paths = SFB.StandaloneFileBrowser.OpenFolderPanel("작업 폴더 선택", "", false);
                // if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
                // {
                //     _workspacePathInput.text = paths[0];
                //     return;
                // }

                // 방법 2: System.Windows.Forms 없이 PowerShell로 폴더 선택
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '작업 폴더를 선택해주세요'; if($f.ShowDialog() -eq 'OK'){Write-Output $f.SelectedPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var result = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(30_000);

                    if (!string.IsNullOrEmpty(result) && _workspacePathInput != null)
                    {
                        _workspacePathInput.text = result;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UI] 폴더 선택 다이얼로그 오류: {ex.Message}");
            }
#else
            Debug.Log("[UI] 폴더 선택: 비Windows 플랫폼은 직접 입력 필요");
#endif
        }

        private void BindInstallerProgress()
        {
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

        private void HideAllPanels()
        {
            foreach (var panel in _allPanels)
            {
                if (panel != null) panel.SetActive(false);
            }
        }
    }
}
