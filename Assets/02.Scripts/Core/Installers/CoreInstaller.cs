using OpenDesk.Core.Implementations;
using OpenDesk.Core.Services;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Core.Installers
{
    /// <summary>
    /// VContainer DI 등록 — 전체 서비스
    /// 씬 LifetimeScope에 붙여 사용
    /// </summary>
    public class CoreInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ── 코어 서비스 (M0) ─────────────────────────

            builder.Register<EventParserService>(Lifetime.Transient)
                   .As<IEventParserService>();

            builder.Register<OpenClawBridgeService>(Lifetime.Singleton)
                   .As<IOpenClawBridgeService>();

            builder.Register<AgentStateService>(Lifetime.Singleton)
                   .As<IAgentStateService>();

            builder.Register<SubAgentService>(Lifetime.Singleton)
                   .As<ISubAgentService>();

#if GOOGLE_DRIVE_ENABLED
            builder.Register<GoogleDriveService>(Lifetime.Singleton)
                   .As<IGoogleDriveService>();
#endif

            builder.Register<WorkspaceService>(Lifetime.Singleton)
                   .As<IWorkspaceService>();

            // ── M2: API 키 볼트 + 라우터 ─────────────────

            builder.Register<ApiKeyVaultService>(Lifetime.Singleton)
                   .As<IApiKeyVaultService>();

            builder.Register<ClawRouterService>(Lifetime.Singleton)
                   .As<IClawRouterService>();

            // ── M3: 대시보드 (비용 모니터 + 콘솔 로그) ───

            builder.Register<CostMonitorService>(Lifetime.Singleton)
                   .As<ICostMonitorService>();

            builder.Register<ConsoleLogService>(Lifetime.Singleton)
                   .As<IConsoleLogService>();

            // ── M4: 채널 + 스킬 마켓 ─────────────────────

            builder.Register<ChannelService>(Lifetime.Singleton)
                   .As<IChannelService>();

            builder.Register<SkillMarketService>(Lifetime.Singleton)
                   .As<ISkillMarketService>();

            // ── M5: 보안 감사 ────────────────────────────

            builder.Register<SecurityAuditService>(Lifetime.Singleton)
                   .As<ISecurityAuditService>();

            // ── 앱 부트스트래퍼 ──────────────────────────
            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
