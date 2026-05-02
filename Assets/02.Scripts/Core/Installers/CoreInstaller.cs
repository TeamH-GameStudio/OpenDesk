using OpenDesk.Core.Implementations;
using OpenDesk.Core.Persistence;
using OpenDesk.Core.Services;
using UnityEngine;
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

            // DEPRECATED: OpenClaw legacy. EventParser/Bridge unregistered 2026-04-27.
            // Anthropic API path uses IClaudeService (registered in AgentOfficeInstaller).
#pragma warning disable CS0618
            // builder.Register<EventParserService>(Lifetime.Transient)
            //        .As<IEventParserService>();
            //
            // builder.Register<OpenClawBridgeService>(Lifetime.Singleton)
            //        .As<IOpenClawBridgeService>();
#pragma warning restore CS0618

            builder.Register<AgentStateService>(Lifetime.Singleton)
                   .As<IAgentStateService>();

            builder.Register<SubAgentService>(Lifetime.Singleton)
                   .As<ISubAgentService>();

#if GOOGLE_DRIVE_ENABLED
            builder.Register<GoogleDriveService>(Lifetime.Singleton)
                   .As<IGoogleDriveService>();
#else
            // Google Drive 비활성 — 더미 등록으로 의존성 해소
            builder.Register<NullGoogleDriveService>(Lifetime.Singleton)
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

            // ── 영속 데이터 저장 (테이블별 Local/Server 라우팅) ─────
            // Local 저장소 — 구체 타입으로만 등록 (라우터가 직접 의존).
            builder.Register<LocalGameDataRepository>(Lifetime.Singleton)
                   .AsSelf();

            // Server 저장소 — 미구현 placeholder. 실제 서버 연동이 추가되면 교체.
            // Server 모드 테이블이 호출되면 즉시 throw하여 조용한 데이터 유실을 방지한다.
            builder.Register<NullServerGameDataRepository>(Lifetime.Singleton)
                   .As<IServerGameDataRepository>();

            // 라우팅 정책 — DefaultMode + 테이블별 override. 런타임 변경 가능.
            builder.Register<StorageRoutingPolicy>(Lifetime.Singleton)
                   .As<IStorageRoutingPolicy>()
                   .AsSelf();

            // 라우터 — IGameDataRepository로 노출되는 유일한 진입점.
            builder.Register<RoutingGameDataRepository>(Lifetime.Singleton)
                   .As<IGameDataRepository>();

            builder.Register<GameDataService>(Lifetime.Singleton)
                   .As<IGameDataService>();

            // AutoSaveService: ITickable로 매 프레임 호출, IDisposable로 정리.
            builder.RegisterEntryPoint<AutoSaveService>().AsSelf();

            // ── 앱 부트스트래퍼 ──────────────────────────
            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
