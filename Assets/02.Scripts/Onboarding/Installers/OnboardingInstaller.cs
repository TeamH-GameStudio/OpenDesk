using OpenDesk.Core.Implementations.Credits;
using OpenDesk.Core.Implementations.Licensing;
using OpenDesk.Core.Services.Credits;
using OpenDesk.Core.Services.Licensing;
using OpenDesk.Onboarding.Implementations;
using OpenDesk.Onboarding.Services;
using OpenDesk.Onboarding.UI.Components;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Onboarding.Installers
{
    /// <summary>
    /// 온보딩 씬 전용 LifetimeScope.
    /// Core 서비스(IGameDataService, IAgentHandoffService 등)는 부모 Scope(CoreInstaller)에서 주입받는다.
    /// </summary>
    public class OnboardingInstaller : LifetimeScope
    {
        [SerializeField] private OnboardingShellView _shellView;
        [SerializeField] private OnboardingFlowController _flowController;

        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[VContainer] OnboardingInstaller.Configure() 시작");

            // ── UI 셸 (UIDocument 보유 MonoBehaviour) ─────────────
            if (_shellView == null)
            {
                Debug.LogError("[OnboardingInstaller] _shellView 미할당.");
            }
            else
            {
                builder.RegisterComponent(_shellView)
                       .AsSelf()
                       .As<IOnboardingShellView>();
            }

            // ── Flow Controller (씬 라우팅 + 상태 머신) ─────────
            if (_flowController == null)
            {
                Debug.LogError("[OnboardingInstaller] _flowController 미할당.");
            }
            else
            {
                builder.RegisterComponent(_flowController)
                       .AsSelf()
                       .As<IOnboardingFlowController>();
            }

            // ── 영속 래퍼 서비스 ─────────────────────────────────
            builder.Register<UserProfileService>(Lifetime.Singleton)
                   .As<IUserProfileService>();

            builder.Register<PlanService>(Lifetime.Singleton)
                   .As<IPlanService>();

            // ── 인증 (UI 스텁) ───────────────────────────────────
            builder.Register<FakeGoogleAuthService>(Lifetime.Singleton)
                   .As<IGoogleAuthService>();

            // ── 라이선스 활성화 (Hybrid routing — Phase 1: mock 미들웨어) ──
            // ClaudeWebSocketClient 가 Onboarding 씬에 없으면 LicenseService 는 활성화 불가하나,
            // 'skip (BYOK)' 버튼으로 우회 가능. PlayerPrefs JWT 가 source of truth.
            builder.Register<LicenseService>(Lifetime.Scoped)
                   .As<ILicenseService>();

            // ── 레거시 IOnboardingService 호환 등록 ────────────────
            // 신규 흐름은 IOnboardingFlowController를 사용하지만, OnboardingScene이 아직 레거시
            // OnboardingUIController GO를 가지고 있을 가능성에 대비해 IOnboardingService를 계속 등록한다.
            // EntryPoint(OnboardingBootstrapper)는 등록하지 않으므로 자동 ReadyToEnter 트리거는 발생하지 않는다.
            builder.Register<OnboardingSettings>(Lifetime.Singleton)
                   .As<IOnboardingSettings>();

#pragma warning disable CS0618
            builder.Register<OnboardingService>(Lifetime.Singleton)
                   .As<IOnboardingService>();
#pragma warning restore CS0618

            // ViewModels는 ShellView가 직접 new로 생성한다 (서비스만 inject 받아 VM 생성자로 전달).

            Debug.Log("[VContainer] OnboardingInstaller.Configure() 완료");
        }
    }
}
