using OpenDesk.Characters.Wardrobe;
using OpenDesk.Claude;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Implementations.Credits;
using OpenDesk.Core.Implementations.Licensing;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Auth;
using OpenDesk.Core.Services.Credits;
using OpenDesk.Core.Services.Licensing;
using OpenDesk.Pipeline;
using OpenDesk.Presentation.UI.Credits;
using OpenDesk.Presentation.Cameras;
using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.UI.Auth;
using OpenDesk.Presentation.UI.Chat;
using OpenDesk.Presentation.UI.Hud;
using OpenDesk.Presentation.UI.Office;
using OpenDesk.Presentation.UI.Session;
using OpenDesk.Presentation.UI.Plugins;
using OpenDesk.Presentation.UI.SkillLoadout;
using OpenDesk.Presentation.UI.SkillMarket;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.AgentCreation.Installers
{
    /// <summary>
    /// AgentOffice 씬 전용 LifetimeScope.
    /// Core 서비스는 부모 Scope(CoreInstaller)에서 주입받음.
    /// </summary>
    public class AgentOfficeInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[VContainer] AgentOfficeInstaller.Configure() 시작");

            builder.RegisterComponentInHierarchy<AgentSpawner>();
            // AgentOfficeBootstrapper(레거시 PlayerPrefs 기반)는 AgentRosterBootstrapper 로 교체됨.
            builder.RegisterComponentInHierarchy<AgentRosterBootstrapper>();
            builder.RegisterComponentInHierarchy<AgentCreationOpener>();
            builder.RegisterComponentInHierarchy<OfficeRosterEmptyView>();

            // uGUI 레거시 컴포넌트 — UI Toolkit 마이그레이션 중. 씬에 없을 수 있으므로 조건부 등록.
#pragma warning disable CS0618 // [Obsolete] SessionListController / ChatPanelController — 마이그레이션 기간 동안 등록 보존
            TryRegisterFromHierarchy<SessionListController>(builder);
            TryRegisterFromHierarchy<ChatPanelController>(builder);
#pragma warning restore CS0618
            TryRegisterFromHierarchy<AgentClickHandler>(builder);
            TryRegisterFromHierarchy<AgentPointerService>(builder);
            // AgentHoverHandler 는 AgentHudView 가 HoverChanged 를 직접 구독해 폐기 — 등록 제거.
            TryRegisterFromHierarchy<ClaudeWebSocketClient>(builder);

            // Cinemachine 카메라 포커스 — 씬에 CameraRig + CinemachineCameraFocusService 가 배치된 경우만 등록.
            // 인터페이스 ICameraFocusService 로 노출해 AgentClickHandler 가 의존.
            var cameraFocus = FindFirstObjectByType<CinemachineCameraFocusService>(FindObjectsInactive.Include);
            if (cameraFocus != null)
            {
                builder.RegisterComponent(cameraFocus).As<ICameraFocusService>();
                Debug.Log("[VContainer] CinemachineCameraFocusService (ICameraFocusService) 등록됨");
            }
            else
            {
                Debug.Log("[VContainer] CinemachineCameraFocusService 미존재 — 카메라 포커스 비활성");
            }

            // ── AI 채팅 게이트웨이 ──────────────────────────────────────
            // 모든 provider(anthropic_cli, anthropic_api, openai, gemini...) 는 Python 미들웨어가 라우팅한다.
            // Unity 측은 단일 MiddlewareChatService 만 IAiChatService 로 등록.
            // provider 선택은 PlayerPrefs `OpenDesk_ChatBackend` ("anthropic_cli" 기본, 레거시 "cli"/"api" 호환).
            builder.Register<MiddlewareChatService>(Lifetime.Scoped).As<IAiChatService>();
            Debug.Log("[VContainer] AI 게이트웨이: MiddlewareChatService (provider 라우팅은 미들웨어)");

            // ── 라이선스 / 크레딧 (hybrid routing — opendesk_routed provider) ──
            builder.Register<LicenseService>(Lifetime.Scoped)
                   .As<ILicenseService>();
            builder.Register<CreditBalanceService>(Lifetime.Scoped)
                   .As<ICreditBalanceService>();
            builder.Register<RoutingHintService>(Lifetime.Scoped)
                   .As<IRoutingHintService>();

            // UI 컴포넌트 — 씬 hierarchy 에 UIDocument 가 배치된 경우만 등록.
            TryRegisterAnyComponent<CreditBalanceBadge>(builder);
            TryRegisterAnyComponent<InsufficientCreditsModal>(builder);

            // 미들웨어 sub_agent_* WS 이벤트 → ISubAgentService 어댑터 (IAiChatService + ISubAgentService 둘 다 보임).
            builder.RegisterEntryPoint<SubAgentEventBridge>();

            // ── Anthropic OAuth 로그인 오케스트레이션 ──────────────────
            // ClaudeWebSocketClient 의 OnAuthEvent 를 R3 Observable 로 노출.
            builder.Register<AuthLoginService>(Lifetime.Scoped)
                   .As<IAuthLoginService>()
                   .AsSelf();

            // 인증 모달 (UI Toolkit) — UIDocument + AnthropicAuthModal 가 씬에 배치된 경우만 컴포넌트 등록.
            // VContainer 는 C# 디폴트 파라미터(`modal = null`)를 존중하지 않고 항상 resolve 를 시도하므로,
            // 씬에 모달이 없을 때도 AuthEntryGuard 가 깨지지 않도록 null 팩토리를 등록한다.
            // AuthEntryGuard.Initialize 가 이미 `_modal == null` 분기를 가지고 있다.
            var authModal = FindFirstObjectByType<AnthropicAuthModal>(FindObjectsInactive.Include);
            if (authModal != null)
            {
                builder.RegisterComponent(authModal);
                Debug.Log("[VContainer] AnthropicAuthModal 등록됨");
            }
            else
            {
                builder.Register<AnthropicAuthModal>(_ => null, Lifetime.Scoped);
                Debug.LogWarning("[VContainer] AnthropicAuthModal 미배치 — null stub 등록 (AuthEntryGuard 가드 비활성)");
            }

            // 씬 진입 시 자동 인증 가드 — 미인증이면 모달 표시. 모달 없으면 경고만.
            builder.RegisterEntryPoint<AuthEntryGuard>();

            // 디스켓 프린터 (레거시) — 디스켓 메타포 제거에 따라 DI 등록 비활성. 가역 보존.
#pragma warning disable CS0618
            // TryRegisterFromHierarchy<DiskettePrinterController>(builder);
#pragma warning restore CS0618

            // 오피스 HUD (UI Toolkit) — 씬에 UIDocument + OfficeHudView 가 배치된 경우만 등록.
            var officeHud = FindFirstObjectByType<OfficeHudView>(FindObjectsInactive.Include);
            if (officeHud != null)
            {
                builder.RegisterComponent(officeHud);
                Debug.Log("[VContainer] OfficeHudView 등록됨");
            }

            // 스킬 마켓 패널 (UI Toolkit) — 씬에 UIDocument + SkillMarketView 가 배치된 경우만 등록.
            // 없으면 SkillsPanelController 가 FindFirstObjectByType 으로 늦은 시점에 폴백 탐색한다.
            var skillMarketView = FindFirstObjectByType<SkillMarketView>(FindObjectsInactive.Include);
            if (skillMarketView != null)
            {
                builder.RegisterComponent(skillMarketView);
                Debug.Log("[VContainer] SkillMarketView (UI Toolkit) 등록됨");
            }
            else
            {
                Debug.Log("[VContainer] SkillMarketView 미존재 — UIDocument 미배치 상태");
            }

            // UI Toolkit 신규 패널들 — 씬에 UIDocument + View 컴포넌트가 배치된 경우만 등록.
            // SessionListView 는 아직 미작성 (다음 단계).
            TryRegisterAnyComponent<ChatPanelView>(builder);
            TryRegisterAnyComponent<AgentHudView>(builder);

            // v4: PluginsMarketView 는 SkillMarketView 로 통합 (스킬+플러그인 단일 뷰).
            // 자격증명 모달은 SkillMarketView 가 inject 받아 그대로 사용 → 등록 유지.
            // PluginsMarketView 는 [Obsolete] 보존; DI 등록만 비활성화.
            // TryRegisterAnyComponent<PluginsMarketView>(builder);  // deprecated v4
            TryRegisterAnyComponent<PluginCredentialModal>(builder);

            // 스킬 장착 UI (UI Toolkit) — RPG 인벤토리 모달.
            // UIDocument + SkillLoadoutView 가 씬에 배치된 경우만 등록.
            // 좌측 캐릭터 프리뷰용 AgentLoadoutPreviewBinder 도 함께 (선택 — 별도 preview rig 가 있을 때만).
            TryRegisterAnyComponent<SkillLoadoutView>(builder);
            TryRegisterAnyComponent<AgentLoadoutPreviewBinder>(builder);

            // ChatPanelView.Closed → ICameraFocusService.ReleaseFocus 와이어링.
            // 둘 다 등록되어 있을 때만 활성화. ChatPanelView 가 미등록(테스트 씬 등)이면 카메라는 수동 복귀 필요.
            builder.RegisterBuildCallback(resolver =>
            {
                ChatPanelView chatPanel = null;
                ICameraFocusService focus = null;
                SkillLoadoutView loadoutView = null;
                try { chatPanel = resolver.Resolve<ChatPanelView>(); } catch { /* 미등록 */ }
                try { focus = resolver.Resolve<ICameraFocusService>(); } catch { /* 미등록 */ }
                try { loadoutView = resolver.Resolve<SkillLoadoutView>(); } catch { /* 미등록 */ }

                if (chatPanel != null && focus != null)
                {
                    chatPanel.Closed += focus.ReleaseFocus;
                    Debug.Log("[VContainer] ChatPanelView.Closed → CameraFocusService.ReleaseFocus 와이어링됨");
                }

                // 채팅 패널 좌측 하단 스킬 영역 "+" / "..." 클릭 → 스킬 장착 UI 오픈.
                // SkillLoadoutView 가 씬에 없으면 이벤트는 그냥 무시된다 (이전 동작 호환).
                if (chatPanel != null && loadoutView != null)
                {
                    chatPanel.SkillsRequested += () =>
                        loadoutView.Open(chatPanel.CurrentAgentId, chatPanel.CurrentAgentName, chatPanel.CurrentRole);
                    Debug.Log("[VContainer] ChatPanelView.SkillsRequested → SkillLoadoutView.Open 와이어링됨");
                }
            });

            Debug.Log("[VContainer] AgentOfficeInstaller.Configure() 완료");
        }

        // 씬 전체에서 컴포넌트를 찾되 없으면 조용히 SKIP.
        // RegisterComponentInHierarchy 의 의미를 따라 LifetimeScope GameObject 자식이 아닌
        // "씬 hierarchy 전체"를 검색한다 (FindFirstObjectByType). 비활성 GameObject 도 포함.
        private void TryRegisterFromHierarchy<T>(IContainerBuilder builder) where T : Component
        {
            var found = FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (found != null)
            {
                builder.RegisterComponent(found);
                Debug.Log($"[VContainer] {typeof(T).Name} 등록됨");
            }
            else
            {
                Debug.Log($"[VContainer] {typeof(T).Name} 미존재 — SKIP");
            }
        }

        // 씬 전체에서 컴포넌트를 찾아 등록. UI Toolkit View 처럼 LifetimeScope 자식 밖에 있을 수 있는 경우.
        private void TryRegisterAnyComponent<T>(IContainerBuilder builder) where T : Component
        {
            var found = FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (found != null)
            {
                builder.RegisterComponent(found);
                Debug.Log($"[VContainer] {typeof(T).Name} 등록됨");
            }
            else
            {
                Debug.Log($"[VContainer] {typeof(T).Name} 미존재 — SKIP");
            }
        }
    }
}
