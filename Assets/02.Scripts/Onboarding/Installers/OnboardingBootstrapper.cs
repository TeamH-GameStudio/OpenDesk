using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace OpenDesk.Onboarding.Installers
{
    /// <summary>
    /// 온보딩 씬 시작 시 자동 실행
    /// 완료되면 오피스 씬으로 전환
    /// </summary>
    public class OnboardingBootstrapper : IStartable
    {
        private readonly IOnboardingService _onboarding;

        private const string OfficSceneName = "Office";

        public OnboardingBootstrapper(IOnboardingService onboarding)
        {
            _onboarding = onboarding;
        }

        public void Start()
        {
            // 상태 변화 구독 — ReadyToEnter 되면 씬 전환
            _onboarding.StateChanged.Subscribe(state => OnStateChanged(state));

            // 온보딩 시작
            _onboarding.StartAsync().Forget();
        }

        private void OnStateChanged(OnboardingState state)
        {
            if (state == OnboardingState.ReadyToEnter)
            {
                Debug.Log("[Onboarding] 완료 → 오피스 씬으로 전환");
                SceneManager.LoadScene(OfficSceneName);
            }

            if (state == OnboardingState.FatalError)
            {
                Debug.LogError("[Onboarding] 치명적 오류 — 재시작 필요");
            }
        }
    }
}
