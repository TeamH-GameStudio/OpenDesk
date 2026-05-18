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
        private bool _seeded;

        public Observable<Unit> OnCatalogChanged => _onChanged;
        public bool IsLoaded => _byId.Count > 0;

        public InMemoryPluginCatalogService()
        {
            // v4: BuiltInPluginFallback 으로 ctor 즉시 seed — SkillMarketView 가 통합 마켓에서
            // "플러그인" 섹션을 비어 있지 않게 보여주려면 catalog 가 미리 채워져 있어야 한다.
            SeedFromFallback();
        }

        public UniTask<bool> RefreshAsync(bool forceRefresh, CancellationToken ct)
        {
            // forceRefresh 시 fallback 으로 다시 채워 넣음 (원격 fetch 가 도입되기 전 임시 동작).
            if (forceRefresh || !_seeded)
            {
                SeedFromFallback();
                _onChanged.OnNext(Unit.Default);
                return UniTask.FromResult(true);
            }
            return UniTask.FromResult(false);
        }

        private void SeedFromFallback()
        {
            var entries = BuiltInPluginFallback.Build();
            lock (_gate)
            {
                foreach (var d in entries)
                {
                    if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                    // 이미 등록된 plugin (예: RegisterLocal 결과) 은 덮어쓰지 않음 — install state 보존.
                    if (!_byId.ContainsKey(d.Id))
                        _byId[d.Id] = d;
                }
                _seeded = true;
            }
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
