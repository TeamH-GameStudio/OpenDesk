using OpenDesk.Core.Services;
using OpenDesk.Onboarding.Implementations;
using OpenDesk.Onboarding.Services;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Onboarding.Installers
{
    /// <summary>
    /// 온보딩 씬 전용 LifetimeScope
    /// Core 서비스는 부모 Scope(CoreInstaller)에서 주입받음
    /// </summary>
    public class OnboardingInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ── M1: 환경 서비스 (Node.js / 관리자 권한 / WSL2) ──────────

            builder.Register<NodeEnvironmentService>(Lifetime.Singleton)
                   .As<INodeEnvironmentService>();

            builder.Register<AdminPrivilegeService>(Lifetime.Singleton)
                   .As<IAdminPrivilegeService>();

            #if UNITY_STANDALONE_WIN
            builder.Register<Wsl2Service>(Lifetime.Singleton)
                   .As<IWsl2Service>();
            #endif

            // ── 온보딩 전용 서비스 ────────────────────────────────────────

            builder.Register<OpenClawDetector>(Lifetime.Singleton)
                   .As<IOpenClawDetector>();

            builder.Register<OpenClawInstaller>(Lifetime.Singleton)
                   .As<IOpenClawInstaller>();

            builder.Register<AgentConfigParser>(Lifetime.Transient)
                   .As<IAgentConfigParser>();

            builder.Register<OnboardingSettings>(Lifetime.Singleton)
                   .As<IOnboardingSettings>();

            // ── 오케스트레이터 ────────────────────────────────────────────
            // IOpenClawBridgeService, IWorkspaceService는
            // 부모 CoreInstaller에서 자동으로 주입됨
            builder.Register<OnboardingService>(Lifetime.Singleton)
                   .As<IOnboardingService>();

            // ── 온보딩 진입점 ─────────────────────────────────────────────
            builder.RegisterEntryPoint<OnboardingBootstrapper>();
        }
    }
}
