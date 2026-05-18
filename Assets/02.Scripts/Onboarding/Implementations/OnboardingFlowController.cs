using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using OpenDesk.Presentation.SceneLoading;
using UnityEngine;
using VContainer;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// 온보딩 플로우 상태 머신 + 씬 라우팅. MonoBehaviour로 동작하며 OnboardingScene 루트에 배치된다.
    /// </summary>
    public sealed class OnboardingFlowController : MonoBehaviour, IOnboardingFlowController
    {
        private const string AgentCreationSceneName = "AgentCreationScene";
        private const string AgentOfficeSceneName = "AgentOfficeScene_Moon";

        private IOnboardingShellView _shellView;
        private IAgentHandoffService _handoffService;
        private IGameSceneLoader _sceneLoader;
        private bool _hasStarted;

        public OnboardingFlowStep CurrentStep { get; private set; } = OnboardingFlowStep.Welcome;

        public event Action<OnboardingFlowStep> StepChanged;

        [Inject]
        public void Construct(IAgentHandoffService handoffService, IGameSceneLoader sceneLoader)
        {
            _handoffService = handoffService;
            _sceneLoader = sceneLoader;
        }

        public void AttachShellView(IOnboardingShellView shellView)
        {
            _shellView = shellView;

            // ShellView가 부착된 직후 한 번만 시작 흐름 진행.
            if (!_hasStarted)
            {
                _hasStarted = true;
                StartFlow();
            }
            else
            {
                // 이미 시작했지만 ShellView가 (씬 재로드 등으로) 새로 부착됐다면 현재 스텝을 다시 보여준다.
                _shellView?.ShowStep(CurrentStep);
            }
        }

        private void StartFlow()
        {
            // AgentCreationScene → OnboardingScene 재진입 시 핸드오프된 이름이 있으면 §6 로딩 직행.
            var pendingName = _handoffService?.Consume();
            if (!string.IsNullOrEmpty(pendingName))
            {
                BeginLoadingAsync(pendingName, this.GetCancellationTokenOnDestroy()).Forget();
                return;
            }

            // 일반 진입 — Welcome부터.
            GoTo(OnboardingFlowStep.Welcome);
        }

        public void Advance()
        {
            switch (CurrentStep)
            {
                case OnboardingFlowStep.Welcome: GoTo(OnboardingFlowStep.Plan); break;
                case OnboardingFlowStep.Plan:    GoTo(OnboardingFlowStep.Auth); break;
                case OnboardingFlowStep.Auth:    GoTo(OnboardingFlowStep.License); break;
                case OnboardingFlowStep.License: GoTo(OnboardingFlowStep.User); break;
                case OnboardingFlowStep.User:
                    BeginAgentCreationAsync(this.GetCancellationTokenOnDestroy()).Forget();
                    break;
                default:
                    Debug.LogWarning($"[OnboardingFlowController] Advance에서 처리되지 않는 스텝: {CurrentStep}");
                    break;
            }
        }

        public void GoBack()
        {
            switch (CurrentStep)
            {
                case OnboardingFlowStep.Plan:    GoTo(OnboardingFlowStep.Welcome); break;
                case OnboardingFlowStep.Auth:    GoTo(OnboardingFlowStep.Plan); break;
                case OnboardingFlowStep.License: GoTo(OnboardingFlowStep.Auth); break;
                case OnboardingFlowStep.User:    GoTo(OnboardingFlowStep.License); break;
                default:
                    Debug.LogWarning($"[OnboardingFlowController] GoBack에서 처리되지 않는 스텝: {CurrentStep}");
                    break;
            }
        }

        public void GoTo(OnboardingFlowStep step)
        {
            CurrentStep = step;
            _shellView?.ShowStep(step);
            StepChanged?.Invoke(step);
        }

        public async UniTask BeginAgentCreationAsync(CancellationToken ct = default)
        {
            CurrentStep = OnboardingFlowStep.AgentCreation;
            StepChanged?.Invoke(CurrentStep);

            // 씬 깜빡임은 LoadingManager 가 페이드 오버레이로 가린다 — 별도 인터스티셜 메시지 불필요.
            if (_sceneLoader == null)
            {
                Debug.LogError("[OnboardingFlowController] IGameSceneLoader 미주입.");
                return;
            }
            await _sceneLoader.ChangeSceneAsync(AgentCreationSceneName, ct);
        }

        public async UniTask BeginLoadingAsync(string agentName, CancellationToken ct = default)
        {
            if (_shellView == null)
            {
                Debug.LogError("[OnboardingFlowController] ShellView가 주입되지 않았습니다.");
                return;
            }

            GoTo(OnboardingFlowStep.Loading);
            await _shellView.RunLoadingAsync(agentName ?? string.Empty, ct);
            await EnterOfficeAsync(ct);
        }

        public async UniTask EnterOfficeAsync(CancellationToken ct = default)
        {
            CurrentStep = OnboardingFlowStep.Office;
            StepChanged?.Invoke(CurrentStep);

            if (_sceneLoader == null)
            {
                Debug.LogError("[OnboardingFlowController] IGameSceneLoader 미주입.");
                return;
            }
            await _sceneLoader.ChangeSceneAsync(AgentOfficeSceneName, ct);
        }
    }
}
