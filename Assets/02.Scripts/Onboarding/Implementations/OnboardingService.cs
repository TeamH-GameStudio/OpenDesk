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
    /// 온보딩 상태 머신 오케스트레이터
    ///
    /// 결합도 최소화 원칙:
    ///   - 코어 서비스(IOpenClawBridgeService 등)는 인터페이스로만 참조
    ///   - 각 스텝 서비스(Detector/Installer/Parser)도 인터페이스로만 참조
    ///   - Presentation(UI)은 이 서비스의 StateChanged만 구독
    /// </summary>
    public class OnboardingService : IOnboardingService
    {
        // ── 의존성 (인터페이스만) ─────────────────────────────────────────────
        private readonly IOpenClawDetector      _detector;
        private readonly IOpenClawInstaller     _installer;
        private readonly IAgentConfigParser     _parser;
        private readonly IOnboardingSettings    _settings;
        private readonly IOpenClawBridgeService _bridge;      // Core 서비스 연결
        private readonly IWorkspaceService      _workspace;   // Core 서비스 연결

        private const int MaxGatewayRetry = 3;

        // ── 상태 ──────────────────────────────────────────────────────────────
        private readonly ReactiveProperty<OnboardingState> _state =
            new(OnboardingState.Init);

        public OnboardingState CurrentState => _state.Value;
        public ReadOnlyReactiveProperty<OnboardingState> StateChanged => _state;
        public OnboardingContext Context { get; } = new();

        public OnboardingService(
            IOpenClawDetector      detector,
            IOpenClawInstaller     installer,
            IAgentConfigParser     parser,
            IOnboardingSettings    settings,
            IOpenClawBridgeService bridge,
            IWorkspaceService      workspace)
        {
            _detector  = detector;
            _installer = installer;
            _parser    = parser;
            _settings  = settings;
            _bridge    = bridge;
            _workspace = workspace;
        }

        // ── 진입점 ────────────────────────────────────────────────────────────

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

        // ── 재방문 복원 ───────────────────────────────────────────────────────

        private async UniTask ResumeFromSavedSettingsAsync(CancellationToken ct)
        {
            Context.GatewayUrl        = _settings.SavedGatewayUrl;
            Context.LocalWorkspacePath = _settings.SavedLocalPath;

            var connected = await TryConnectGatewayAsync(ct);
            if (!connected)
            {
                // 저장된 URL 실패 → 재탐색
                await RunGatewayStepAsync(ct);
                return;
            }

            // 워크스페이스 경로 유효성 재확인
            if (!string.IsNullOrEmpty(Context.LocalWorkspacePath))
                _workspace.SetLocalPath(Context.LocalWorkspacePath);

            TransitionTo(OnboardingState.Completed);
        }

        // ── 최초 실행 풀 플로우 ────────────────────────────────────────────────

        private async UniTask RunFullOnboardingAsync(CancellationToken ct)
        {
            var detectResult = await RunDetectStepAsync(ct);
            if (!detectResult.IsSuccess) return;

            var gatewayResult = await RunGatewayStepAsync(ct);
            if (!gatewayResult.IsSuccess) return;

            await RunParseAgentsStepAsync(ct);
            // WorkspaceSetup은 유저 액션 대기 (UI에서 트리거)
        }

        // ── Step 1: OpenClaw 감지 / 설치 ──────────────────────────────────────

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

            // 미설치 → 설치 시도
            TransitionTo(OnboardingState.OpenClawNotFound);
            return StepResult.Fail(OnboardingState.OpenClawNotFound, "OpenClaw 미설치");
        }

        // ── Step 2: OpenClaw 설치 (UI에서 "설치하기" 버튼 클릭 후 호출) ────────

        public async UniTask RetryCurrentStepAsync(CancellationToken ct = default)
        {
            switch (CurrentState)
            {
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
            await RunGatewayStepAsync(ct);
        }

        // ── Step 3: Gateway 연결 ──────────────────────────────────────────────

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

            // 3회 실패
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

        // ── Gateway 수동 URL 입력 (UI 액션) ──────────────────────────────────

        public async UniTask SubmitGatewayUrlAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            Context.GatewayUrl = url.Trim();
            TransitionTo(OnboardingState.ConnectingGateway);
            await RunGatewayStepAsync(ct);
        }

        // ── Step 4: 에이전트 파싱 ────────────────────────────────────────────

        private async UniTask RunParseAgentsStepAsync(CancellationToken ct)
        {
            TransitionTo(OnboardingState.ParsingAgents);

            // 파일 I/O → 스레드 풀
            var agents = await UniTask.RunOnThreadPool(() =>
                _parser.ParseFromFile(Context.OpenClawConfigPath),
                cancellationToken: ct
            );

            if (agents == null || agents.Count == 0)
            {
                // 에이전트 없음 → 기본 main 생성 후 계속
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
                // 최대 4명 제한 (현재 오피스 슬롯)
                foreach (var agent in agents)
                {
                    if (Context.DetectedAgents.Count >= 4) break;
                    if (agent.IsValid)
                        Context.DetectedAgents.Add(agent);
                }
                TransitionTo(OnboardingState.WorkspaceSetup);
            }
        }

        // ── Step 5: 워크스페이스 설정 (UI 액션) ──────────────────────────────

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

        // ── 오프라인 모드 (UI 액션) ───────────────────────────────────────────

        public UniTask EnterOfflineMode()
        {
            Context.IsOfflineMode = true;

            // 에이전트 없으면 기본 main 추가
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

        // ── 완료 처리 ─────────────────────────────────────────────────────────

        private void CompleteOnboarding()
        {
            _settings.MarkOnboardingComplete(
                Context.GatewayUrl,
                Context.LocalWorkspacePath
            );

            TransitionTo(OnboardingState.ReadyToEnter);
        }

        // ── 내부 상태 전환 ────────────────────────────────────────────────────

        private void TransitionTo(OnboardingState next)
        {
            Debug.Log($"[Onboarding] {_state.Value} → {next}");
            _state.Value = next;
        }
    }
}
