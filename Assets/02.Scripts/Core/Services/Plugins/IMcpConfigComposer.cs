using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 에이전트에 장착된 플러그인 + 자격증명을 합쳐 미들웨어 MCP 페이로드를 생성.
    /// OfficePipelineManager 가 채팅 전에 호출해 IAiChatService.SendMcpConfigAsync 로 전달한다.
    /// </summary>
    public interface IMcpConfigComposer
    {
        UniTask<McpConfigPayload> ComposeAsync(string agentId, CancellationToken ct = default);
    }
}
