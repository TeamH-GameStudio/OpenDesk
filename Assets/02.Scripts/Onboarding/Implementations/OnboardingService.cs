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

        // WSL2는 Windows 전용이므로 nullable
        private readonly IWsl2Service _wsl2;

        private const int MaxGatewayRetry = 3;

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
            _wsl2      = wsl2;
        }

        // ── 진입점 ──────────────────────────────────────────────────────

        public async UniTask StartAsync(CancellationToken ct = default)
        {
            TransitionTo(OnboardingState.CheckingFirstRun);

            // 재방문: 저장된 설정으로 바로 복원 시도
            if (!_settings.IsFirstRun)
            {
                await ResumeFromSavedSettingsAsync(ct);
                return;
            }

            // 최초 실행: 정규 온보딩 플로우
            await RunFullOnboardingAsync(ct);
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

        // ── Step 0: 환경 스캔 (M1 핵심) ─────────────────────────────────

        private async UniTask<bool> RunEnvironmentScanAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.ScanningEnvironment);

            // ── Node.js 확인 ────────────────────────────────────────────
            var hasNode = await _nodeEnv.IsInstalledAsync(ct);

            if (!hasNode)
            {
                TransitionTo(OnboardingState.InstallingNodeJs);
                var nodeInstalled = await _nodeEnv.InstallAsync(ct);
                if (!nodeInstalled)
                {
                    Context.LastErrorMessage = "Node.js 설치에 실패했습니다.";
                    TransitionTo(OnboardingState.NodeJsFailed);
                    return false;
                }
            }
            else
            {
                var meetsMin = await _nodeEnv.MeetsMinVersionAsync("22.16.0", ct);
                if (!meetsMin)
                {
                    var ver = await _nodeEnv.GetVersionAsync(ct);
                    Context.LastErrorMessage = $"Node.js {ver} → 22.16 이상이 필요합니다.";
                    TransitionTo(OnboardingState.NodeJsFailed);
                    return false;
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

            TransitionTo(OnboardingState.OpenClawNotFound);
            return StepResult.Fail(OnboardingState.OpenClawNotFound, "OpenClaw 미설치");
        }

        // ── Step 2: OpenClaw 설치 (UI 트리거) ───────────────────────────

        public async UniTask RetryCurrentStepAsync(CancellationToken ct = default)
        {
            switch (CurrentState)
            {
                case OnboardingState.NodeJsFailed:
                    await RunEnvironmentScanAsync(ct);
                    if (CurrentState == OnboardingState.NodeJsFailed) return;
                    await RunDetectStepAsync(ct);
                    break;

                case OnboardingState.OpenClawNotFound:
                case OnboardingState.InstallFailed:
                    await RunInstallStepAsync(ct);
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
            await RunGatewayStepAsync(ct);
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
                    Name      = "팀장",
                    Role      = "main",
                });
                TransitionTo(OnboardingState.NoAgentsFound);
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
                    Name      = "팀장",
                    Role      = "main",
                });

            CompleteOnboarding();
            return UniTask.CompletedTask;
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
