using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using R3;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 플러그인별 자격증명(Notion API key, GitHub PAT 등) 저장소.
    /// IApiKeyVaultService 와 분리 — AI 모델 키와 외부 서비스 토큰을 섞지 않는다.
    /// </summary>
    public interface IPluginCredentialService
    {
        Observable<string> OnCredentialChanged { get; }  // pluginId

        UniTask SetAsync(string pluginId, string key, string value, CancellationToken ct = default);

        UniTask<string> GetAsync(string pluginId, string key, CancellationToken ct = default);

        UniTask DeleteAsync(string pluginId, string key, CancellationToken ct = default);

        UniTask DeleteAllAsync(string pluginId, CancellationToken ct = default);

        UniTask<bool> HasAllRequiredAsync(PluginDescriptor descriptor, CancellationToken ct = default);
    }
}
