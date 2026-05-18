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
    /// [Deprecated] 구 OfficeScene 전용 LifetimeScope. 메인 씬은 AgentOfficeScene_Moon + AgentOfficeInstaller 로 대체됨.
    /// OfficeScene 자체가 레거시이므로 본 Installer 도 미사용. 가역 보존을 위해 [Obsolete] 만 부착.
    /// </summary>
    [System.Obsolete("구 OfficeScene 전용. 메인 오피스는 AgentOfficeInstaller (AgentOfficeScene_Moon) 를 사용합니다.")]
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
