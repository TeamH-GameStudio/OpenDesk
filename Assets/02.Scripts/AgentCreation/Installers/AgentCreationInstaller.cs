using OpenDesk.Presentation.UI.AgentCreation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.AgentCreation.Installers
{
    /// <summary>
    /// AgentCreation 씬 전용 LifetimeScope.
    /// Core 서비스는 부모 Scope(CoreInstaller)에서 주입받음.
    /// </summary>
    public class AgentCreationInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[VContainer] AgentCreationInstaller.Configure() 시작");

            builder.RegisterComponentInHierarchy<AgentCreationWizardController>();

            Debug.Log("[VContainer] AgentCreationInstaller.Configure() 완료");
        }
    }
}
