using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Implementations.Credits;
using OpenDesk.Core.Implementations.Licensing;
using OpenDesk.Core.Persistence;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Auth;
using OpenDesk.Core.Services.Credits;
using OpenDesk.Core.Services.Licensing;
using OpenDesk.Core.Services.Plugins;
using OpenDesk.Core.Services.Skills;
using OpenDesk.Presentation.SceneLoading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Core.Installers
{
    /// <summary>
    /// VContainer DI 등록 — 전체 서비스.
    ///
    /// VContainerSettings.RootLifetimeScope 에 프리팹으로 등록되어
    /// 어느 씬에서 Play 를 시작해도 단일 인스턴스가 root scope 로 자동 생성되고
    /// DontDestroyOnLoad 도 VContainer 가 직접 처리한다.
    /// 자식 LifetimeScope(AgentCreationInstaller, AgentOfficeInstaller, OnboardingInstaller)
    /// 는 parentReference 가 비어 있어도 이 root 를 자동 부모로 잡는다.
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

            // 씬 간 핸드오프 채널 — AgentCreationScene → OnboardingScene §6 로딩에 에이전트 이름 전달.
            builder.Register<AgentHandoffService>(Lifetime.Singleton)
                   .As<IAgentHandoffService>();

            // 에이전트 생성 ↔ 오피스 사이 양방향 핸드셰이크 (record 발행 + setup 완료 신호).
            builder.Register<AgentCreationBridgeService>(Lifetime.Singleton)
                   .As<IAgentCreationBridge>();

            // 드래프트 JSON 영속화 — Application.persistentDataPath/agents/*.json.
            // SaveTrigger / RosterBootstrapper 가 동일 인스턴스 공유.
            // 생성자에 string rootDirectory = null 옵셔널 파라미터가 있어 VContainer 가 resolve 실패하므로
            // RegisterInstance 로 명시적 인스턴스를 등록한다.
            //
            // AgentDraftJsonStore 는 IAgentRepository 구현체 — 동일 인스턴스를
            // 인터페이스와 구체 타입 양쪽으로 노출해 기존 SaveTrigger/RosterBootstrapper
            // 도 깨지지 않고, 신규 구독자(ChatPanelController 등)는 IAgentRepository
            // 로만 의존하도록 한다.
            var agentStore = new AgentDraftJsonStore();
            builder.RegisterInstance(agentStore)
                   .As<IAgentRepository>()
                   .AsSelf();

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

            // ── Telemetry — 미들웨어 hook chain 의 latency/cache/retry 측정값 수신 ───
            // ClaudeWebSocketClient.OnTelemetry → AgentTelemetryService.Ingest 까지의
            // wiring 은 AgentOfficeBootstrapper 또는 CostHudController 에서 구독.
            builder.Register<AgentTelemetryService>(Lifetime.Singleton)
                   .As<IAgentTelemetryService>();

            // ── M4: 채널 + 스킬 마켓 ─────────────────────

            builder.Register<ChannelService>(Lifetime.Singleton)
                   .As<IChannelService>();

            builder.Register<SkillMarketService>(Lifetime.Singleton)
                   .As<ISkillMarketService>();

            // ── 신규 Skill 시스템 (정적 JSON 레지스트리 + 캐시 + 인스톨러 + Loadout + 추천) ──
            builder.Register<RemoteSkillRegistry>(Lifetime.Singleton)
                   .As<IRemoteSkillRegistry>();

            builder.Register<SkillCatalogService>(Lifetime.Singleton)
                   .As<ISkillCatalogService>();

            builder.Register<SkillInstallerService>(Lifetime.Singleton)
                   .As<ISkillInstallerService>();

            builder.Register<AgentSkillLoadoutService>(Lifetime.Singleton)
                   .As<IAgentSkillLoadoutService>();

            builder.Register<SkillRecommendationService>(Lifetime.Singleton)
                   .As<ISkillRecommendationService>();

            // ── Plugin 시스템 (Skill 과 분리된 외부 앱 연동) ──────
            builder.Register<InMemoryPluginCatalogService>(Lifetime.Singleton)
                   .As<IPluginCatalogService>();

            builder.Register<AgentPluginLoadoutService>(Lifetime.Singleton)
                   .As<IAgentPluginLoadoutService>();

            builder.Register<PluginCredentialService>(Lifetime.Singleton)
                   .As<IPluginCredentialService>();

            // Anthropic 인증 자격증명 (API Key + OAuth 토큰 존재 감지)
            // 글로벌 ~/.claude/ 와 분리된 격리 디렉토리(OpenDeskPaths.ClaudeConfigDir) 기반.
            builder.Register<AnthropicCredentialService>(Lifetime.Singleton)
                   .As<IAnthropicCredentialService>()
                   .AsSelf();

            // ── 라이선스 / 크레딧 (hybrid routing) ────────────────
            // 디바이스 지문은 OS 호출만 — WS 의존성 없어 root scope.
            builder.Register<DeviceFingerprintService>(Lifetime.Singleton)
                   .As<IDeviceFingerprintService>();

            // LicenseService / CreditBalanceService / RoutingHintService 는
            // ClaudeWebSocketClient (씬별 MonoBehaviour) 에 의존하므로 씬별 Installer
            // (OnboardingInstaller / AgentOfficeInstaller) 에서 Scoped 로 등록한다.
            // PlayerPrefs 캐시(JWT, balance) 가 source of truth 라 인스턴스가 분리돼도 동작 일치.

            builder.Register<McpConfigComposer>(Lifetime.Singleton)
                   .As<IMcpConfigComposer>();

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

            // ── 씬 전환 + 로딩 오버레이 ─────────────────
            // GameSceneLoader 는 SceneManager.LoadSceneAsync + 진행률/페이드 통합 진입점.
            // LoadingManagerBootstrap 가 BuildContainer 직후 LoadingManager GameObject 를
            // 코드로 만들어 DontDestroyOnLoad + DI 주입 → 호출처는 prefab/씬 작업 없이 바로 사용.
            builder.Register<GameSceneLoader>(Lifetime.Singleton)
                   .As<IGameSceneLoader>();
            builder.RegisterEntryPoint<LoadingManagerBootstrap>();

            // ── 앱 부트스트래퍼 ──────────────────────────
            builder.RegisterEntryPoint<AppBootstrapper>();
        }
    }
}
