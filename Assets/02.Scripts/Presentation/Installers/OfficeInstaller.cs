using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.Dashboard;
using OpenDesk.Presentation.UI.Modals;
using OpenDesk.Presentation.UI.OfficeWizard;
using OpenDesk.Presentation.UI.Panels;
using OpenDesk.Presentation.UI.TopBar;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Presentation.Installers
{
    /// <summary>
    /// Office 씬 전용 LifetimeScope
    /// Core 서비스는 부모 Scope(CoreInstaller)에서 주입받음
    /// 씬 내 MonoBehaviour에 [Inject] 주입
    /// </summary>
    public class OfficeInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[VContainer] OfficeInstaller.Configure() 시작");

            // ── TopBar ──────────────────────────────────────────
            builder.RegisterComponentInHierarchy<TopBarController>();

            // ── 터미널 채팅 ─────────────────────────────────────
            builder.RegisterComponentInHierarchy<TerminalChatController>();

            // ── 탭 네비게이션 ───────────────────────────────────
            builder.RegisterComponentInHierarchy<TabController>();

            // ── 설정 패널 (6개) ─────────────────────────────────
            builder.RegisterComponentInHierarchy<ChannelsPanelController>();
            builder.RegisterComponentInHierarchy<ApiKeysPanelController>();
            builder.RegisterComponentInHierarchy<RoutingPanelController>();
            builder.RegisterComponentInHierarchy<SkillsPanelController>();
            builder.RegisterComponentInHierarchy<SecurityPanelController>();
            builder.RegisterComponentInHierarchy<SettingsPanelController>();

            // ── 모달 다이얼로그 ─────────────────────────────────
            builder.RegisterComponentInHierarchy<ModalDialogController>();

            // ── 대시보드 HUD (BottomHUD) ────────────────────────
            builder.RegisterComponentInHierarchy<AgenticLoopVisualizer>();
            builder.RegisterComponentInHierarchy<ConsoleLogController>();
            builder.RegisterComponentInHierarchy<CostHudController>();

            // ── 3D 캐릭터 ───────────────────────────────────────
            builder.RegisterComponentInHierarchy<AgentCharacterController>();

            // ── Office 환영 마법사 ──────────────────────────────
            builder.RegisterComponentInHierarchy<OfficeWizardController>();

            Debug.Log("[VContainer] OfficeInstaller.Configure() 완료 — 15개 컨트롤러 등록");
        }
    }
}
