using OpenDesk.Claude;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Services;
using OpenDesk.Pipeline;
using OpenDesk.Presentation.Character;
using OpenDesk.Presentation.UI.Session;
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
            builder.RegisterComponentInHierarchy<AgentOfficeBootstrapper>();
            builder.RegisterComponentInHierarchy<SessionListController>();
            builder.RegisterComponentInHierarchy<ChatPanelController>();
            builder.RegisterComponentInHierarchy<AgentClickHandler>();
            builder.RegisterComponentInHierarchy<ClaudeWebSocketClient>();
            builder.Register<ClaudeService>(Lifetime.Scoped).As<IClaudeService>();

            // 씬에 있을 때만 등록 (Pipeline 패치 전에는 없을 수 있음)
            var printer = FindObjectOfType<DiskettePrinterController>();
            if (printer != null)
                builder.RegisterComponent(printer);

            Debug.Log("[VContainer] AgentOfficeInstaller.Configure() 완료");
        }
    }
}
