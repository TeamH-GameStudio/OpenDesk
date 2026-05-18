using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using R3;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 플러그인 카탈로그 in-memory 캐시 + 설치 상태 머지.
    /// UI/마이그레이션이 의존하는 단일 진입점. ISkillCatalogService 와 동일 패턴.
    /// </summary>
    public interface IPluginCatalogService
    {
        Observable<Unit> OnCatalogChanged { get; }

        bool IsLoaded { get; }

        UniTask<bool> RefreshAsync(bool forceRefresh, CancellationToken ct);

        IReadOnlyList<PluginDescriptor> GetAll();

        IReadOnlyList<PluginDescriptor> GetByVendor(PluginVendor vendor);

        PluginDescriptor GetById(string pluginId);

        void NotifyInstallStateChanged(string pluginId, bool isInstalled, string installPath);

        /// <summary>로컬에서 PluginDescriptor 를 등록 (Custom 플러그인, 마이그레이션 결과 등).</summary>
        void RegisterLocal(PluginDescriptor descriptor);
    }
}
