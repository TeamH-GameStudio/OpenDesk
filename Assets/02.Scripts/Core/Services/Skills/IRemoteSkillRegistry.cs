using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 원격 catalog.json + 스킬 zip 패키지를 HTTP 로 가져오는 레지스트리 어댑터.
    /// 캐시 정책은 SkillCatalogService 가 담당하고 본 인터페이스는 fetch 만 책임진다.
    /// </summary>
    public interface IRemoteSkillRegistry
    {
        /// <summary>
        /// catalog.json fetch. ETag/Last-Modified 검사 후 변경 없으면 null 반환 (304 의미).
        /// forceRefresh=true 면 캐시 무시하고 강제 GET.
        /// </summary>
        UniTask<RemoteCatalogFetchResult> FetchCatalogAsync(
            string previousEtag,
            bool forceRefresh,
            CancellationToken ct);

        /// <summary>
        /// 스킬 zip 다운로드. progress 는 0..1, 실패 시 throw.
        /// 결과는 로컬 임시 파일 경로 (호출자가 검증 후 이동/정리).
        /// </summary>
        UniTask<string> DownloadSkillPackageAsync(
            SkillCatalogEntry entry,
            string destinationTempPath,
            IProgress<float> progress,
            CancellationToken ct);
    }

    public readonly struct RemoteCatalogFetchResult
    {
        public readonly SkillCatalog Catalog;
        public readonly string ETag;
        public readonly bool NotModified;

        public RemoteCatalogFetchResult(SkillCatalog catalog, string etag, bool notModified)
        {
            Catalog = catalog;
            ETag = etag;
            NotModified = notModified;
        }

        public static RemoteCatalogFetchResult Updated(SkillCatalog catalog, string etag)
            => new(catalog, etag, notModified: false);

        public static RemoteCatalogFetchResult Cached(string etag)
            => new(null, etag, notModified: true);
    }
}
