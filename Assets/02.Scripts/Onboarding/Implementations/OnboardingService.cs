using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// DEPRECATED 2026-04-27.
    ///
    /// 원래 OpenClaw 자동설치 + Gateway 연결 오케스트레이터였으나,
    /// Anthropic API 직접 호출(IClaudeService) 경로로 전환되며 모든 OpenClaw 흐름이
    /// 비활성화됐다. OnboardingScene 셸은 사용자 결정으로 유지하되 본 서비스는
    /// 호출 시 즉시 ReadyToEnter 상태로 전환하는 stub로 동작한다.
    ///
    /// 원본 600여 줄 본문(환경 스캔/Node.js/WSL2/OpenClaw 감지·설치/Gateway 연결/
    /// 에이전트 파싱/워크스페이스 설정)은 파일 하단 거대 주석(LEGACY)에 보존.
    /// 추후 Anthropic API 키 입력 등 새 온보딩 단계로 재구성될 때 참고.
    /// </summary>
    [Obsolete("OpenClaw legacy. Replaced by Claude CLI/Anthropic API path (IClaudeService). Will be removed once Anthropic-direct migration is fully verified.", error: false)]
    public class OnboardingService : IOnboardingService
    {
        private readonly ReactiveProperty<OnboardingState> _state =
            new(OnboardingState.Init);

        public OnboardingState CurrentState => _state.Value;
        public ReadOnlyReactiveProperty<OnboardingState> StateChanged => _state;
        public OnboardingContext Context { get; } = new();

        public OnboardingService()
        {
            // DEPRECATED: 원래 생성자는 다음 의존성을 받았다.
            //   IOpenClawDetector detector, IOpenClawInstaller installer, IAgentConfigParser parser,
            //   IOnboardingSettings settings, IOpenClawBridgeService bridge, IWorkspaceService workspace,
            //   INodeEnvironmentService nodeEnv, IAdminPrivilegeService admin, IRollbackService rollback,
            //   IWsl2Service wsl2 = null
            // OpenClaw 3종(detector/installer/bridge)은 DI 등록 해제됨.
            // Stub 단계에서는 어떤 의존성도 필요 없음.
        }

        // ── Stub 진입점 ─────────────────────────────────────────────────

        public UniTask StartAsync(CancellationToken ct = default)
        {
            Debug.Log("[Onboarding] DEPRECATED: OpenClaw 온보딩 비활성. ReadyToEnter로 즉시 전환.");
            TransitionTo(OnboardingState.ReadyToEnter);
            return UniTask.CompletedTask;
        }

        public UniTask RestartAsync(CancellationToken ct = default)
        {
            Context.Reset();
            _state.Value = OnboardingState.Init;
            return StartAsync(ct);
        }

        public UniTask RetryCurrentStepAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask SubmitGatewayUrlAsync(string url, CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask SkipWorkspaceSetupAsync()
        {
            Context.WorkspaceSkipped = true;
            TransitionTo(OnboardingState.ReadyToEnter);
            return UniTask.CompletedTask;
        }

        public UniTask ConfirmWorkspacePathAsync(string path, CancellationToken ct = default)
        {
            Context.LocalWorkspacePath = path;
            TransitionTo(OnboardingState.ReadyToEnter);
            return UniTask.CompletedTask;
        }

        public UniTask EnterOfflineMode()
        {
            Context.IsOfflineMode = true;
            TransitionTo(OnboardingState.ReadyToEnter);
            return UniTask.CompletedTask;
        }

        public UniTask HandleNodeInstall_Nvm(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HandleNodeInstall_Direct(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HandleNodeInstall_Skip(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HandleNodeUpgrade_SafeInstall(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HandleNodeUpgrade_Overwrite(CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask HandleNodeUpgrade_Skip(CancellationToken ct = default) => UniTask.CompletedTask;

        public void RequestReboot()
        {
            Debug.Log("[Onboarding] DEPRECATED: WSL2/OpenClaw 재부팅 요청 비활성.");
        }

        // ── 내부 ────────────────────────────────────────────────────────

        private void TransitionTo(OnboardingState next)
        {
            Debug.Log($"[Onboarding] {_state.Value} → {next}");
            _state.Value = next;
        }

        // ─────────────────────────────────────────────────────────────────
        // LEGACY (DEPRECATED 2026-04-27): 원본 OpenClaw 온보딩 본문 보존.
        // 새 코드에서는 호출하지 말 것. 추후 새 온보딩 단계 설계 시 참고용.
        // ─────────────────────────────────────────────────────────────────
        /*
        private const int MaxGatewayRetry = 3;
        private const string RebootPendingKey = "OpenDesk_RebootPending";

        private CancellationTokenSource _flowCts;

        public async UniTask RestartAsync_Legacy(CancellationToken ct = default)
        {
            _flowCts?.Cancel();
            _flowCts?.Dispose();
            try { await _bridge.DisconnectAsync(); } catch { }
            Context.Reset();
            PlayerPrefs.SetInt("OpenDesk_IsFirstRun", 1);
            PlayerPrefs.DeleteKey(RebootPendingKey);
            PlayerPrefs.Save();
            _settings.ClearAll();
            await StartAsync(ct);
        }

        public async UniTask StartAsync_Legacy(CancellationToken ct = default)
        {
            _flowCts?.Cancel();
            _flowCts?.Dispose();
            _flowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var flowCt = _flowCts.Token;
            TransitionTo(OnboardingState.CheckingFirstRun);

            if (PlayerPrefs.GetInt(RebootPendingKey, 0) == 1)
            {
                PlayerPrefs.DeleteKey(RebootPendingKey);
                PlayerPrefs.Save();
                await RunPostRebootFlowAsync(flowCt);
                return;
            }
            if (!_settings.IsFirstRun)
            {
                await ResumeFromSavedSettingsAsync(flowCt);
                return;
            }
            await RunFullOnboardingAsync(flowCt);
        }

        // RunPostRebootFlowAsync, ResumeFromSavedSettingsAsync, RunFullOnboardingAsync,
        // RunMockOnboardingAsync, RunEnvironmentScanAsync, RunDetectStepAsync,
        // RunInstallStepAsync, RunGatewayStepAsync, SubmitGatewayUrlAsync,
        // RunParseAgentsStepAsync, HandleNodeInstall_*, HandleNodeUpgrade_*,
        // ContinueAfterNodeResolvedAsync, RequestReboot, CompleteOnboarding
        // — 모두 _bridge/_detector/_installer/_workspace/_settings/_nodeEnv/_admin/_rollback/_wsl2/_parser
        // 의존. git history (HEAD~1: 5df4c18 이전)에서 원문 확인 가능.
        */
    }
}
