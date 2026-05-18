using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using R3;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 메모리 캐시 기반 카탈로그 서비스. 원격/디스크 캐시는 추후 RemotePluginRegistry 가 보강한다.
    /// 현재는 로컬 RegisterLocal + 마이그레이션 결과만 보관한다.
    /// </summary>
    public class InMemoryPluginCatalogService : IPluginCatalogService, IDisposable
    {
        private readonly Subject<Unit> _onChanged = new();
        private readonly object _gate = new();
        private readonly Dictionary<string, PluginDescriptor> _byId = new();

        public Observable<Unit> OnCatalogChanged => _onChanged;
        public bool IsLoaded => _byId.Count > 0;

        public UniTask<bool> RefreshAsync(bool forceRefresh, CancellationToken ct)
        {
            // InMemory 구현은 원격 fetch 가 없다. 이미 로드되어 있으면 false (변경 없음) 반환.
            return UniTask.FromResult(false);
        }

        public IReadOnlyList<PluginDescriptor> GetAll()
        {
            lock (_gate)
            {
                return _byId.Values.ToList();
            }
        }

        public IReadOnlyList<PluginDescriptor> GetByVendor(PluginVendor vendor)
        {
            lock (_gate)
            {
                return _byId.Values.Where(d => d.Vendor == vendor).ToList();
            }
        }

        public PluginDescriptor GetById(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return null;
            lock (_gate)
            {
                return _byId.TryGetValue(pluginId, out var d) ? d : null;
            }
        }

        public void NotifyInstallStateChanged(string pluginId, bool isInstalled, string installPath)
        {
            if (string.IsNullOrEmpty(pluginId)) return;
            lock (_gate)
            {
                if (!_byId.TryGetValue(pluginId, out var existing)) return;
                _byId[pluginId] = existing.WithInstallState(isInstalled, installPath);
            }
            _onChanged.OnNext(Unit.Default);
        }

        public void RegisterLocal(PluginDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id)) return;
            lock (_gate)
            {
                _byId[descriptor.Id] = descriptor;
            }
            _onChanged.OnNext(Unit.Default);
        }

        public void Dispose() => _onChanged.Dispose();
    }
}
