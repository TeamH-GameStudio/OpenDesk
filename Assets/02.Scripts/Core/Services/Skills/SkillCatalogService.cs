using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 카탈로그 캐시 + 새로고침. 캐시는 OpenDeskPaths.CatalogCache 의 JSON 파일.
    /// 24h TTL 또는 forceRefresh 시 원격 fetch. 네트워크 실패 시 캐시/빌트인 폴백.
    /// </summary>
    public class SkillCatalogService : ISkillCatalogService, IDisposable
    {
        private static readonly TimeSpan CatalogTtl = TimeSpan.FromHours(24);

        private readonly IRemoteSkillRegistry _registry;
        private readonly Subject<Unit> _onChanged = new();
        private readonly object _gate = new();

        private SkillCatalog _catalog;
        private string _etag = string.Empty;
        private DateTime _lastFetchedAt = DateTime.MinValue;
        private readonly Dictionary<string, SkillDescriptor> _byId = new();

        public Observable<Unit> OnCatalogChanged => _onChanged;
        public bool IsLoaded => _byId.Count > 0;

        public SkillCatalogService(IRemoteSkillRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            TryLoadDiskCache();
        }

        public async UniTask<bool> RefreshAsync(bool forceRefresh, CancellationToken ct)
        {
            if (!forceRefresh && IsFresh()) return false;

            try
            {
                var result = await _registry.FetchCatalogAsync(_etag, forceRefresh, ct);
                if (result.NotModified)
                {
                    _lastFetchedAt = DateTime.UtcNow;
                    PersistMetadataOnly();
                    return false;
                }

                ApplyCatalog(result.Catalog, result.ETag);
                PersistDiskCache();
                _onChanged.OnNext(Unit.Default);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillCatalogService] 원격 fetch 실패: {ex.Message}. 캐시 사용.");

                if (!IsLoaded)
                {
                    ApplyCatalog(BuiltInFallback.Build(), etag: string.Empty);
                    _onChanged.OnNext(Unit.Default);
                    return true;
                }
                return false;
            }
        }

        public IReadOnlyList<SkillDescriptor> GetAll()
        {
            lock (_gate)
            {
                return _byId.Values.ToList();
            }
        }

        public IReadOnlyList<SkillDescriptor> GetByCategory(SkillCategory category)
        {
            lock (_gate)
            {
                return _byId.Values.Where(d => d.Category == category).ToList();
            }
        }

        public IReadOnlyList<SkillCategory> GetRecommendedCategoriesFor(AgentRole role)
        {
            lock (_gate)
            {
                if (_catalog == null) return Array.Empty<SkillCategory>();
                var raw = _catalog.GetRecommendedCategoriesFor(role);
                var result = new List<SkillCategory>(raw.Count);
                foreach (var key in raw)
                    result.Add(SkillCategoryExtensions.ParseCategory(key));
                return result;
            }
        }

        public SkillDescriptor GetById(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            lock (_gate)
            {
                return _byId.TryGetValue(skillId, out var d) ? d : null;
            }
        }

        public void NotifyInstallStateChanged(string skillId, bool isInstalled, string installPath)
        {
            if (string.IsNullOrEmpty(skillId)) return;
            lock (_gate)
            {
                if (!_byId.TryGetValue(skillId, out var existing)) return;
                _byId[skillId] = existing.WithInstallState(isInstalled, installPath);
            }
            _onChanged.OnNext(Unit.Default);
        }

        // ── 내부 ──────────────────────────────────────────────────

        private bool IsFresh()
        {
            return IsLoaded && DateTime.UtcNow - _lastFetchedAt < CatalogTtl;
        }

        private void ApplyCatalog(SkillCatalog catalog, string etag)
        {
            if (catalog == null) return;

            lock (_gate)
            {
                _catalog = catalog;
                _etag = etag ?? string.Empty;
                _lastFetchedAt = DateTime.UtcNow;
                _byId.Clear();

                foreach (var entry in catalog.skills)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                    var descriptor = entry.ToDescriptor(manifestOrNull: null, isInstalled: false, installPath: string.Empty);
                    _byId[entry.id] = descriptor;
                }
            }

            Debug.Log($"[SkillCatalogService] 카탈로그 적용: {_byId.Count}개 스킬");
        }

        // v4: 스킬/플러그인 분리 + 의존성 필드 도입으로 스키마가 바뀜.
        // 이 버전과 일치하지 않는 옛 캐시는 자동 무시 → BuiltInFallback 으로 재시드.
        private const string CurrentSchemaVersion = "2.0-v4";

        private void TryLoadDiskCache()
        {
            try
            {
                var path = OpenDeskPaths.CatalogCache;
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return;

                var envelope = JsonUtility.FromJson<CacheEnvelope>(json);
                if (envelope == null || envelope.catalog == null) return;

                // 스키마 버전 mismatch — plugin-like 도구가 SkillDescriptor 로 들어 있던 옛 캐시를 거부.
                if (!string.Equals(envelope.catalog.schemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
                {
                    Debug.Log($"[SkillCatalogService] 캐시 스키마 mismatch (cache='{envelope.catalog.schemaVersion}' vs current='{CurrentSchemaVersion}') — 무시하고 fallback 사용");
                    return;
                }

                _etag = envelope.etag ?? string.Empty;
                _lastFetchedAt = envelope.fetchedAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(envelope.fetchedAt).UtcDateTime
                    : DateTime.MinValue;
                ApplyCatalog(envelope.catalog, _etag);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillCatalogService] 디스크 캐시 로드 실패: {ex.Message}");
            }
        }

        private void PersistDiskCache()
        {
            try
            {
                if (_catalog == null) return;
                EnsureDirectory(OpenDeskPaths.Skills);

                var envelope = new CacheEnvelope
                {
                    etag = _etag,
                    fetchedAt = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                    catalog = _catalog,
                };
                var json = JsonUtility.ToJson(envelope, prettyPrint: false);
                File.WriteAllText(OpenDeskPaths.CatalogCache, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillCatalogService] 캐시 저장 실패: {ex.Message}");
            }
        }

        private void PersistMetadataOnly()
        {
            // ETag/fetchedAt 만 갱신 (304 응답 시 호출)
            PersistDiskCache();
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public void Dispose() => _onChanged.Dispose();

        [Serializable]
        private class CacheEnvelope
        {
            public string etag;
            public long fetchedAt;
            public SkillCatalog catalog;
        }
    }
}
