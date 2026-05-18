using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using OpenDesk.Presentation.SceneLoading;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace OpenDesk.Onboarding.Installers
{
    /// <summary>
    /// [Deprecated] 구 온보딩 자동 부팅 EntryPoint. OnboardingInstaller 가 의도적으로 등록하지 않으며,
    /// 신규 흐름은 OnboardingFlowController 가 상태 머신과 씬 전환을 모두 담당한다.
    /// 가역 보존을 위해 [Obsolete] 만 부착.
    /// </summary>
    [System.Obsolete("OnboardingFlowController 로 대체됨. OnboardingInstaller 가 이 EntryPoint 를 등록하지 않습니다.")]
    public class OnboardingBootstrapper : IStartable
    {
        private readonly IOnboardingService _onboarding;
        private readonly IGameSceneLoader _sceneLoader;

        private const string OfficeSceneName = "AgentOfficeScene_Moon";

        public OnboardingBootstrapper(IOnboardingService onboarding, IGameSceneLoader sceneLoader)
        {
            _onboarding = onboarding;
            _sceneLoader = sceneLoader;
        }

        public void Start()
        {
            Debug.Log("[Bootstrapper] OnboardingBootstrapper.Start() 진입");

            // 상태 변화 구독 — ReadyToEnter 되면 씬 전환
            _onboarding.StateChanged.Subscribe(state => OnStateChanged(state));

            // 온보딩 시작
            Debug.Log("[Bootstrapper] OnboardingService.StartAsync() 호출");
            _onboarding.StartAsync().Forget();
        }

        private void OnStateChanged(OnboardingState state)
        {
            if (state == OnboardingState.ReadyToEnter)
            {
                Debug.Log("[Onboarding] 완료 → 오피스 씬으로 전환");
                _sceneLoader.ChangeSceneAsync(OfficeSceneName).Forget();
            }

            if (state == OnboardingState.FatalError)
            {
                Debug.LogError("[Onboarding] 치명적 오류 — 재시작 필요");
            }
        }
    }
}
