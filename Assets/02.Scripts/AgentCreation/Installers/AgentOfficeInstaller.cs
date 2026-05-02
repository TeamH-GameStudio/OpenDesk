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

            // ── AI 채팅 백엔드 선택 ──────────────────────────────────────
            // PlayerPrefs `OpenDesk_ChatBackend` 키로 토글:
            //   "cli" (기본): Anthropic Claude CLI subprocess + Python 미들웨어 (MCP 지원)
            //   "api"       : Anthropic Messages API HTTP 직접 호출 (경량, 빠름)
            var backend = PlayerPrefs.GetString("OpenDesk_ChatBackend", "cli");
            if (backend == "api")
            {
                builder.Register<AnthropicApiChatService>(Lifetime.Scoped).As<IAiChatService>();
                Debug.Log("[VContainer] AI 백엔드: AnthropicApiChatService (HTTP 직접)");
            }
            else
            {
                builder.Register<AnthropicCliChatService>(Lifetime.Scoped).As<IAiChatService>();
                Debug.Log("[VContainer] AI 백엔드: AnthropicCliChatService (Python 미들웨어)");
            }

            builder.RegisterComponentInHierarchy<DiskettePrinterController>();

            Debug.Log("[VContainer] AgentOfficeInstaller.Configure() 완료");
        }
    }
}
