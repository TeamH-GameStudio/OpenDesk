using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// 온보딩 상태 머신 오케스트레이터 (명세서 1단계 기준)
    ///
    /// 풀 플로우:
    ///   환경 스캔(Node.js/WSL2) → OpenClaw 감지/설치 → Gateway 연결
    ///   → 에이전트 파싱 → 워크스페이스 → 완료
    /// </summary>
    public class OnboardingService : IOnboardingService
    {
        // ── 의존성 ──────────────────────────────────────────────────────
        private readonly IOpenClawDetector       _detector;
        private readonly IOpenClawInstaller      _installer;
        private readonly IAgentConfigParser      _parser;
        private readonly IOnboardingSettings     _settings;
        private readonly IOpenClawBridgeService  _bridge;
        private readonly IWorkspaceService       _workspace;
        private readonly INodeEnvironmentService _nodeEnv;
        private readonly IAdminPrivilegeService  _admin;
        private readonly IRollbackService        _rollback;

        // WSL2는 Windows 전용이므로 nullable
        private readonly IWsl2Service _wsl2;

        private const int MaxGatewayRetry = 3;
        private const string RebootPendingKey = "OpenDesk_RebootPending";

        // ── 상태 ────────────────────────────────────────────────────────
        private readonly ReactiveProperty<OnboardingState> _state =
            new(OnboardingState.Init);

        public OnboardingState CurrentState => _state.Value;
        public ReadOnlyReactiveProperty<OnboardingState> StateChanged => _state;
        public OnboardingContext Context { get; } = new();

        public OnboardingService(
            IOpenClawDetector       detector,
            IOpenClawInstaller      installer,
            IAgentConfigParser      parser,
            IOnboardingSettings     settings,
            IOpenClawBridgeService  bridge,
            IWorkspaceService       workspace,
            INodeEnvironmentService nodeEnv,
            IAdminPrivilegeService  admin,
            IRollbackService        rollback,
            IWsl2Service            wsl2 = null)  // Windows 아닐 때 null 허용
        {
            _detector  = detector;
            _installer = installer;
            _parser    = parser;
            _settings  = settings;
            _bridge    = bridge;
            _workspace = workspace;
            _nodeEnv   = nodeEnv;
            _admin     = admin;
            _rollback  = rollback;
            _wsl2      = wsl2;
        }

        // ── 진입점 ──────────────────────────────────────────────────────

        public async UniTask StartAsync(CancellationToken ct = default)
        {
            TransitionTo(OnboardingState.CheckingFirstRun);

            // 재부팅 복귀 감지 (WSL2 설치 후 재시작한 경우)
            if (PlayerPrefs.GetInt(RebootPendingKey, 0) == 1)
            {
                PlayerPrefs.DeleteKey(RebootPendingKey);
                PlayerPrefs.Save();
                Debug.Log("[Onboarding] 재부팅 복귀 감지 — WSL2 이후 단계부터 재개");
                await RunPostRebootFlowAsync(ct);
                return;
            }

            // 재방문: 저장된 설정으로 바로 복원 시도
            if (!_settings.IsFirstRun)
            {
                await ResumeFromSavedSettingsAsync(ct);
                return;
            }

            // 최초 실행: 정규 온보딩 플로우
            await RunFullOnboardingAsync(ct);
        }

        /// <summary>
        /// WSL2 재부팅 후 복귀: 환경 스캔(WSL2)을 건너뛰고 OpenClaw 감지부터 재개
        /// </summary>
        private async UniTask RunPostRebootFlowAsync(CancellationToken ct)
        {
            // WSL2 활성화 확인만 간단히
            if (_wsl2 != null)
            {
                TransitionTo(OnboardingState.CheckingWsl2);
                var wslEnabled = await _wsl2.IsEnabledAsync(ct);
                if (!wslEnabled)
                {
                    Context.LastErrorMessage = "재시작 후에도 호환성 환경이 활성화되지 않았습니다.";
                    TransitionTo(OnboardingState.InstallFailed);
                    return;
                }
            }

            // OpenClaw 감지/설치 → Gateway → 에이전트 → 워크스페이스
            var detectResult = await RunDetectStepAsync(ct);
            if (!detectResult.IsSuccess) return;

            var gatewayResult = await RunGatewayStepAsync(ct);
            if (!gatewayResult.IsSuccess) return;

            await RunParseAgentsStepAsync(ct);
        }

        // ── 재방문 복원 ─────────────────────────────────────────────────

        private async UniTask ResumeFromSavedSettingsAsync(CancellationToken ct)
        {
            Context.GatewayUrl        = _settings.SavedGatewayUrl;
            Context.LocalWorkspacePath = _settings.SavedLocalPath;

            var connected = await TryConnectGatewayAsync(ct);
            if (!connected)
            {
                await RunGatewayStepAsync(ct);
                return;
            }

            if (!string.IsNullOrEmpty(Context.LocalWorkspacePath))
                _workspace.SetLocalPath(Context.LocalWorkspacePath);

            TransitionTo(OnboardingState.Completed);
        }

        // ── 최초 실행 풀 플로우 ──────────────────────────────────────────

        private async UniTask RunFullOnboardingAsync(CancellationToken ct)
        {
            // Mock 모드: 실제 설치 없이 UI 플로우만 시뮬레이션 (에디터 테스트용)
            if (IsMockMode)
            {
                await RunMockOnboardingAsync(ct);
                return;
            }

            // Step 0: 환경 스캔 (Node.js, WSL2)
            var envOk = await RunEnvironmentScanAsync(ct);
            if (!envOk) return;

            // Step 1: OpenClaw 감지/설치
            var detectResult = await RunDetectStepAsync(ct);
            if (!detectResult.IsSuccess) return;

            // Step 2: Gateway 연결
            var gatewayResult = await RunGatewayStepAsync(ct);
            if (!gatewayResult.IsSuccess) return;

            // Step 3: 에이전트 파싱
            await RunParseAgentsStepAsync(ct);
            // Step 4: WorkspaceSetup은 유저 액션 대기 (UI에서 트리거)
        }

        // ── Mock 온보딩 (에디터 테스트용) ──────────────────────────────

        private static bool IsMockMode => PlayerPrefs.GetInt("OpenDesk_MockMode", 0) == 1;

        private async UniTask RunMockOnboardingAsync(CancellationToken ct)
        {
            Debug.Log("[Onboarding] ★ Mock 모드 — 실제 설치 없이 UI 플로우 시뮬레이션");

            // Step 0: 환경 스캔 시뮬레이션
            TransitionTo(OnboardingState.ScanningEnvironment);
            await UniTask.Delay(1500, cancellationToken: ct);

            // Node.js 미설치 시나리오 시뮬레이션 — 설치 방법 선택 대기
            TransitionTo(OnboardingState.NodeInstallChoice);
            await UniTask.WaitUntil(() =>
                CurrentState != OnboardingState.NodeInstallChoice || Context.NodeInstallSkipped,
                cancellationToken: ct);

            // 건너뛰기가 아닌 경우 설치 시뮬레이션
            if (!Context.NodeInstallSkipped)
            {
                if (CurrentState == OnboardingState.NodeInstallChoice)
                    TransitionTo(OnboardingState.InstallingNodeJs);
                await UniTask.Delay(2000, cancellationToken: ct);
            }

            // Node.js 버전 충돌 시뮬레이션 — 버튼 클릭까지 대기
            Context.ExistingNodeVersion = "20.14.0";
            Context.NodeProjectPaths.Add("C:\\Users\\user\\Documents\\MyWebApp");
            Context.NodeProjectPaths.Add("C:\\Users\\user\\Documents\\GitHub\\ProjectX");
            TransitionTo(OnboardingState.NodeUpgradeChoice);
            // NodeUpgradeSkipped도 체크: Skip 핸들러는 상태를 바꾸지 않고 플래그만 설정
            await UniTask.WaitUntil(() =>
                CurrentState != OnboardingState.NodeUpgradeChoice || Context.NodeUpgradeSkipped,
                cancellationToken: ct);

            // 건너뛰기가 아닌 경우 설치 시뮬레이션
            if (!Context.NodeUpgradeSkipped)
            {
                if (CurrentState == OnboardingState.NodeUpgradeChoice)
                    TransitionTo(OnboardingState.InstallingNodeJs);
                await UniTask.Delay(2000, cancellationToken: ct);
            }

            // WSL2 체크 시뮬레이션
            TransitionTo(OnboardingState.CheckingWsl2);
            await UniTask.Delay(1000, cancellationToken: ct);

            // Step 1: OpenClaw 감지 시뮬레이션
            TransitionTo(OnboardingState.DetectingOpenClaw);
            await UniTask.Delay(1000, cancellationToken: ct);

            // OpenClaw 설치 시뮬레이션
            TransitionTo(OnboardingState.InstallingOpenClaw);
            await UniTask.Delay(2500, cancellationToken: ct);

            // Step 2: Gateway 연결 시뮬레이션
            TransitionTo(OnboardingState.ConnectingGateway);
            await UniTask.Delay(1500, cancellationToken: ct);

            // Step 3: 에이전트 파싱 시뮬레이션
            TransitionTo(OnboardingState.ParsingAgents);
            await UniTask.Delay(1000, cancellationToken: ct);

            Context.DetectedAgents.Add(new AgentConfig
            {
                SessionId = "main",
                Name      = "AI 비서",
                Role      = "main",
            });

            // Step 4: 워크스페이스
            TransitionTo(OnboardingState.WorkspaceSetup);
            // UI에서 사용자 액션 대기
        }

        // ── Step 0: 환경 스캔 (M1 핵심) ─────────────────────────────────

        private async UniTask<bool> RunEnvironmentScanAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.ScanningEnvironment);

            // ── Node.js 확인 ────────────────────────────────────────────
            var hasNode = await _nodeEnv.IsInstalledAsync(ct);

            if (!hasNode)
            {
                // Node.js 없음 → 설치 방법 선택 UI로 전환 (자동 설치 X)
                TransitionTo(OnboardingState.NodeInstallChoice);
                return false; // UI에서 사용자 선택 대기
            }
            else
            {
                var meetsMin = await _nodeEnv.MeetsMinVersionAsync("22.16.0", ct);
                if (!meetsMin)
                {
                    // 버전 부족 → 기존 사용처 스캔 후 사용자에게 선택지 제공
                    Context.ExistingNodeVersion = await _nodeEnv.GetVersionAsync(ct) ?? "알 수 없음";

                    Debug.Log($"[Onboarding] Node.js {Context.ExistingNodeVersion} 감지 — 기존 프로젝트 스캔 시작");
                    var projects = await _nodeEnv.ScanExistingProjectsAsync(ct);
                    Context.NodeProjectPaths.Clear();
                    foreach (var p in projects)
                        Context.NodeProjectPaths.Add(p);

                    Debug.Log($"[Onboarding] 기존 Node.js 프로젝트 {projects.Count}개 발견");
                    TransitionTo(OnboardingState.NodeUpgradeChoice);
                    return false; // UI에서 사용자 선택 대기
                }
            }

            // ── WSL2 확인 (Windows 전용) ─────────────────────────────────
            if (_wsl2 != null)
            {
                TransitionTo(OnboardingState.CheckingWsl2);
                var wslEnabled = await _wsl2.IsEnabledAsync(ct);

                if (!wslEnabled)
                {
                    TransitionTo(OnboardingState.InstallingWsl2);
                    var wslResult = await _wsl2.EnableAsync(ct);

                    if (!wslResult.Success)
                    {
                        Context.LastErrorMessage = wslResult.Message;
                        TransitionTo(OnboardingState.InstallFailed);
                        return false;
                    }

                    _rollback.RecordInstall(new InstalledItem
                    {
                        Id = "wsl2", DisplayName = "WSL2 (호환성 환경)",
                        PreviousState = "미설치",
                        InstalledState = "활성화됨",
                        Method = "wsl_install",
                        CanRollback = true,
                        RollbackDescription = "WSL2 호환성 환경을 비활성화합니다. 재부팅이 필요할 수 있어요.",
                    });

                    if (wslResult.NeedsReboot)
                    {
                        Context.LastErrorMessage = wslResult.Message;
                        TransitionTo(OnboardingState.Wsl2NeedsReboot);
                        return false; // 재부팅 후 재시작 필요
                    }
                }
            }

            return true;
        }

        // ── Step 1: OpenClaw 감지/설치 ───────────────────────────────────

        private async UniTask<StepResult> RunDetectStepAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.DetectingOpenClaw);

            var isInstalled = await _detector.IsInstalledAsync(ct);
            if (isInstalled)
            {
                Context.IsOpenClawInstalled = true;
                Context.OpenClawVersion     = await _detector.GetVersionAsync(ct) ?? "";
                Context.OpenClawConfigPath  = await _detector.GetInstallPathAsync(ct) ?? "";
                return StepResult.Success(OnboardingState.ConnectingGateway);
            }

            // 미설치 → 자동 설치 바로 진행 (사용자 Retry 대기 제거)
            Debug.Log("[Onboarding] OpenClaw 미설치 — 자동 설치 시작");
            await RunInstallStepAsync(ct);

            if (Context.IsOpenClawInstalled)
                return StepResult.Success(OnboardingState.ConnectingGateway);

            return StepResult.Fail(OnboardingState.InstallFailed,
                "AI 비서 설치에 실패했습니다.");
        }

        // ── Step 2: OpenClaw 설치 (UI 트리거) ───────────────────────────

        public async UniTask RetryCurrentStepAsync(CancellationToken ct = default)
        {
            switch (CurrentState)
            {
                case OnboardingState.NodeJsFailed:
                case OnboardingState.NodeUpgradeChoice:
                    await RunEnvironmentScanAsync(ct);
                    if (CurrentState == OnboardingState.NodeJsFailed) return;
                    if (CurrentState == OnboardingState.NodeUpgradeChoice) return;
                    await RunDetectStepAsync(ct);
                    break;

                case OnboardingState.OpenClawNotFound:
                case OnboardingState.InstallFailed:
                    await RunInstallStepAsync(ct);
                    if (Context.IsOpenClawInstalled)
                    {
                        var gw = await RunGatewayStepAsync(ct);
                        if (gw.IsSuccess) await RunParseAgentsStepAsync(ct);
                    }
                    break;

                case OnboardingState.GatewayFailed:
                    await RunGatewayStepAsync(ct);
                    break;

                default:
                    Debug.LogWarning($"[Onboarding] 재시도 불가 상태: {CurrentState}");
                    break;
            }
        }

        private async UniTask RunInstallStepAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.InstallingOpenClaw);

            var success = await _installer.InstallAsync(ct);
            if (!success)
            {
                Context.LastErrorMessage = "OpenClaw 설치에 실패했습니다.";
                TransitionTo(OnboardingState.InstallFailed);
                return;
            }

            Context.IsOpenClawInstalled = true;
            Context.OpenClawConfigPath  = await _detector.GetInstallPathAsync(ct) ?? "";

            _rollback.RecordInstall(new InstalledItem
            {
                Id = "openclaw", DisplayName = "OpenClaw (AI 비서)",
                PreviousState = "미설치", InstalledState = "설치됨", Method = "npm",
                CanRollback = true,
                RollbackDescription = "OpenClaw AI 비서 프로그램과 설정 파일을 모두 제거합니다.",
            });
            _rollback.RecordInstall(new InstalledItem
            {
                Id = "openclaw_daemon", DisplayName = "OpenClaw 데몬 (백그라운드 서비스)",
                PreviousState = "미설치", InstalledState = "등록됨", Method = "daemon",
                CanRollback = true,
                RollbackDescription = "OpenClaw 백그라운드 서비스를 중지하고 등록을 해제합니다.",
            });
        }

        // ── Step 3: Gateway 연결 ────────────────────────────────────────

        private async UniTask<StepResult> RunGatewayStepAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.ConnectingGateway);
            Context.GatewayRetryCount = 0;

            while (Context.GatewayRetryCount < MaxGatewayRetry)
            {
                var connected = await TryConnectGatewayAsync(ct);
                if (connected)
                {
                    await RunParseAgentsStepAsync(ct);
                    return StepResult.Success(OnboardingState.ParsingAgents);
                }

                Context.GatewayRetryCount++;
                await UniTask.Delay(1000, cancellationToken: ct);
            }

            Context.LastErrorMessage = $"Gateway 연결 실패 ({Context.GatewayUrl})";
            TransitionTo(OnboardingState.GatewayFailed);
            return StepResult.Fail(OnboardingState.GatewayFailed, Context.LastErrorMessage);
        }

        private async UniTask<bool> TryConnectGatewayAsync(CancellationToken ct)
        {
            try
            {
                await _bridge.ConnectAsync(Context.GatewayUrl, ct);
                Context.IsGatewayConnected = _bridge.IsConnected;
                return _bridge.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        // ── Gateway 수동 URL 입력 (UI 액션) ─────────────────────────────

        public async UniTask SubmitGatewayUrlAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            url = url.Trim();

            // URL 유효성 검증
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                Context.LastErrorMessage = "유효한 WebSocket URL이 필요합니다 (ws:// 또는 wss://)";
                TransitionTo(OnboardingState.GatewayFailed);
                return;
            }

            Context.GatewayUrl = url;
            TransitionTo(OnboardingState.ConnectingGateway);
            await RunGatewayStepAsync(ct);
        }

        // ── Step 4: 에이전트 파싱 ───────────────────────────────────────

        private async UniTask RunParseAgentsStepAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.ParsingAgents);

            var agents = await UniTask.RunOnThreadPool(() =>
                _parser.ParseFromFile(Context.OpenClawConfigPath),
                cancellationToken: ct
            );

            if (agents == null || agents.Count == 0)
            {
                Context.DetectedAgents.Add(new AgentConfig
                {
                    SessionId = "main",
                    Name      = "AI 비서",
                    Role      = "main",
                });
                TransitionTo(OnboardingState.NoAgentsFound);

                // 기본 에이전트 생성 후 3초 대기 → 자동으로 다음 단계
                await UniTask.Delay(3000, cancellationToken: ct);
                TransitionTo(OnboardingState.WorkspaceSetup);
            }
            else
            {
                foreach (var agent in agents)
                {
                    if (Context.DetectedAgents.Count >= 4) break;
                    if (agent.IsValid)
                        Context.DetectedAgents.Add(agent);
                }
                TransitionTo(OnboardingState.WorkspaceSetup);
            }
        }

        // ── Step 5: 워크스페이스 설정 (UI 액션) ─────────────────────────

        public UniTask SkipWorkspaceSetupAsync()
        {
            Context.WorkspaceSkipped = true;
            CompleteOnboarding();
            return UniTask.CompletedTask;
        }

        public async UniTask ConfirmWorkspacePathAsync(string path, CancellationToken ct = default)
        {
            _workspace.SetLocalPath(path);
            Context.LocalWorkspacePath = path;
            CompleteOnboarding();
            await UniTask.CompletedTask;
        }

        // ── 오프라인 모드 (UI 액션) ─────────────────────────────────────

        public UniTask EnterOfflineMode()
        {
            Context.IsOfflineMode = true;

            if (Context.DetectedAgents.Count == 0)
                Context.DetectedAgents.Add(new AgentConfig
                {
                    SessionId = "main",
                    Name      = "AI 비서",
                    Role      = "main",
                });

            CompleteOnboarding();
            return UniTask.CompletedTask;
        }

        // ── Node.js 신규 설치 — 사용자 선택 처리 ──────────────────────

        public async UniTask HandleNodeInstall_Nvm(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: NVM으로 Node.js 설치");
            TransitionTo(OnboardingState.InstallingNodeJs);

            if (IsMockMode)
            {
                await UniTask.Delay(2000, cancellationToken: ct);
                await ContinueAfterNodeResolvedAsync(ct);
                return;
            }

            var success = await _nodeEnv.InstallViaNvmAsync(ct);
            if (!success)
            {
                Context.LastErrorMessage = "NVM 설치에 실패했습니다. 인터넷 연결을 확인해주세요.";
                TransitionTo(OnboardingState.NodeJsFailed);
                return;
            }

            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nvm", DisplayName = "nvm (버전 관리 도구)",
                PreviousState = "미설치", InstalledState = "설치됨", Method = "nvm-install",
                CanRollback = true, RollbackDescription = "nvm 및 설치된 Node.js 버전을 제거합니다.",
            });
            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nodejs", DisplayName = "Node.js (nvm 설치)",
                PreviousState = "미설치",
                InstalledState = await _nodeEnv.GetVersionAsync(ct) ?? "24.1.0",
                Method = "nvm", CanRollback = true,
                RollbackDescription = "Node.js를 제거합니다.",
            });

            await ContinueAfterNodeResolvedAsync(ct);
        }

        public async UniTask HandleNodeInstall_Direct(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: 직접 설치 (pkg/msi)");
            TransitionTo(OnboardingState.InstallingNodeJs);

            if (IsMockMode)
            {
                await UniTask.Delay(2000, cancellationToken: ct);
                await ContinueAfterNodeResolvedAsync(ct);
                return;
            }

            var success = await _nodeEnv.InstallAsync(ct);
            if (!success)
            {
                Context.LastErrorMessage = "Node.js 설치에 실패했습니다. 관리자 권한이 필요합니다.";
                TransitionTo(OnboardingState.NodeJsFailed);
                return;
            }

            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nodejs", DisplayName = "Node.js (직접 설치)",
                PreviousState = "미설치",
                InstalledState = await _nodeEnv.GetVersionAsync(ct) ?? "24.1.0",
                Method = "pkg", CanRollback = true,
                RollbackDescription = "Node.js를 컴퓨터에서 완전히 제거합니다.",
            });

            await ContinueAfterNodeResolvedAsync(ct);
        }

        public UniTask HandleNodeInstall_Skip(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: Node.js 설치 건너뛰기");
            Context.NodeInstallSkipped = true;

            if (IsMockMode)
                return UniTask.CompletedTask; // Mock에서는 WaitUntil이 플래그 감지하여 다음으로 진행

            return ContinueAfterNodeResolvedAsync(ct);
        }

        // ── Node.js 버전 충돌 — 사용자 선택 처리 ──────────────────────

        public async UniTask HandleNodeUpgrade_SafeInstall(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: 안전 설치 (nvm)");
            TransitionTo(OnboardingState.InstallingNodeJs);

            if (IsMockMode)
            {
                Debug.Log("[Onboarding] ★ Mock — 실제 설치 건너뜀");
                await UniTask.Delay(2000, cancellationToken: ct);
                return;
            }

            var success = await _nodeEnv.InstallViaNvmAsync(ct);
            if (!success)
            {
                Context.LastErrorMessage = "안전 설치에 실패했습니다. 인터넷 연결을 확인해주세요.";
                TransitionTo(OnboardingState.NodeJsFailed);
                return;
            }

            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nvm", DisplayName = "nvm (버전 관리 도구)",
                PreviousState = "미설치", InstalledState = "설치됨", Method = "installer",
                CanRollback = true,
                RollbackDescription = "nvm 버전 관리 도구를 제거합니다.",
            });
            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nodejs_nvm", DisplayName = "Node.js (nvm 안전 설치)",
                PreviousState = Context.ExistingNodeVersion,
                InstalledState = await _nodeEnv.GetVersionAsync(ct) ?? "24.1.0",
                Method = "nvm",
                CanRollback = true,
                RollbackDescription = "새로 설치한 Node.js 버전을 제거하고 이전 버전으로 돌아갑니다.",
            });

            await ContinueAfterNodeResolvedAsync(ct);
        }

        public async UniTask HandleNodeUpgrade_Overwrite(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: 기존 버전 업그레이드");
            TransitionTo(OnboardingState.InstallingNodeJs);

            if (IsMockMode)
            {
                Debug.Log("[Onboarding] ★ Mock — 실제 설치 건너뜀");
                await UniTask.Delay(2000, cancellationToken: ct);
                return;
            }

            var prevVersion = Context.ExistingNodeVersion;
            var success = await _nodeEnv.InstallAsync(ct);
            if (!success)
            {
                Context.LastErrorMessage = "업그레이드에 실패했습니다. 인터넷 연결을 확인해주세요.";
                TransitionTo(OnboardingState.NodeJsFailed);
                return;
            }

            _rollback.RecordInstall(new InstalledItem
            {
                Id = "nodejs", DisplayName = "Node.js (업그레이드)",
                PreviousState = prevVersion,
                InstalledState = await _nodeEnv.GetVersionAsync(ct) ?? "24.1.0",
                Method = "msi",
                CanRollback = true,
                RollbackDescription = $"Node.js를 이전 버전(v{prevVersion})으로 되돌립니다.",
            });

            await ContinueAfterNodeResolvedAsync(ct);
        }

        public async UniTask HandleNodeUpgrade_Skip(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] 사용자 선택: 업그레이드 건너뛰기");
            Context.NodeUpgradeSkipped = true;

            if (IsMockMode)
                return; // Mock에서는 WaitUntil이 감지하여 다음으로 진행

            await ContinueAfterNodeResolvedAsync(ct);
        }

        private async UniTask ContinueAfterNodeResolvedAsync(CancellationToken ct)
        {
            // WSL2 체크
            if (_wsl2 != null)
            {
                TransitionTo(OnboardingState.CheckingWsl2);
                var wslEnabled = await _wsl2.IsEnabledAsync(ct);
                if (!wslEnabled)
                {
                    TransitionTo(OnboardingState.InstallingWsl2);
                    var wslResult = await _wsl2.EnableAsync(ct);
                    if (!wslResult.Success)
                    {
                        Context.LastErrorMessage = wslResult.Message;
                        TransitionTo(OnboardingState.InstallFailed);
                        return;
                    }
                    if (wslResult.NeedsReboot)
                    {
                        Context.LastErrorMessage = wslResult.Message;
                        TransitionTo(OnboardingState.Wsl2NeedsReboot);
                        return;
                    }
                }
            }

            // OpenClaw 감지/설치 → Gateway → 에이전트 → 워크스페이스
            var detectResult = await RunDetectStepAsync(ct);
            if (!detectResult.IsSuccess) return;

            var gatewayResult = await RunGatewayStepAsync(ct);
            if (!gatewayResult.IsSuccess) return;

            await RunParseAgentsStepAsync(ct);
        }

        // ── 재시작 요청 (WSL2 설치 후) ─────────────────────────────────

        /// <summary>
        /// WSL2 설치 후 재부팅 플래그를 저장하고 시스템 재시작을 요청합니다.
        /// UI에서 [지금 재시작] 버튼이 이 메서드를 호출합니다.
        /// </summary>
        public void RequestReboot()
        {
            PlayerPrefs.SetInt(RebootPendingKey, 1);
            PlayerPrefs.Save();
            Debug.Log("[Onboarding] 재부팅 플래그 저장 완료 — 시스템 재시작 요청");

#if UNITY_STANDALONE_WIN
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "shutdown",
                    Arguments       = "/r /t 10 /c \"OpenDesk 설치를 위해 재시작합니다.\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Onboarding] 재시작 명령 실패: {ex.Message}");
            }
#endif
        }

        // ── 완료 처리 ───────────────────────────────────────────────────

        private void CompleteOnboarding()
        {
            _settings.MarkOnboardingComplete(
                Context.GatewayUrl,
                Context.LocalWorkspacePath
            );

            TransitionTo(OnboardingState.ReadyToEnter);
        }

        // ── 내부 상태 전환 ──────────────────────────────────────────────

        private void TransitionTo(OnboardingState next)
        {
            Debug.Log($"[Onboarding] {_state.Value} → {next}");
            _state.Value = next;
        }
    }
}
