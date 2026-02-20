using OpenDesk.Core.Implementations;
using OpenDesk.Core.Services;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Core.Installers
{
    /// <summary>
    /// VContainer DI 등록
    /// 씬 LifetimeScope에 붙여 사용
    /// </summary>
    public class CoreInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ── 코어 서비스 ───────────────────────────────

            // EventParser: Stateless → Transient
            builder.Register<EventParserService>(Lifetime.Transient)
                   .As<IEventParserService>();

            // Bridge: WebSocket 연결 유지 → Singleton
            builder.Register<OpenClawBridgeService>(Lifetime.Singleton)
                   .As<IOpenClawBridgeService>();

            // AgentState: 에이전트별 상태 → Singleton
            builder.Register<AgentStateService>(Lifetime.Singleton)
                   .As<IAgentStateService>();

            // SubAgent: 서브에이전트 목록 → Singleton
            builder.Register<SubAgentService>(Lifetime.Singleton)
                   .As<ISubAgentService>();

#if GOOGLE_DRIVE_ENABLED
            // GoogleDrive: OAuth 유지 → Singleton
            builder.Register<GoogleDriveService>(Lifetime.Singleton)
                   .As<IGoogleDriveService>();
#endif

            // Workspace: 파일 관리 → Singleton
            builder.Register<WorkspaceService>(Lifetime.Singleton)
                   .As<IWorkspaceService>();

            // ── 앱 부트스트래퍼 (앱 시작 시 자동 실행) ───
            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
