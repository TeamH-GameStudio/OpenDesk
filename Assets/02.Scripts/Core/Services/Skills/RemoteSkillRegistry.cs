using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// HTTP 기반 IRemoteSkillRegistry 기본 구현. UnityWebRequest 사용.
    /// catalogUrl 은 PlayerPrefs `OpenDesk_SkillCatalogUrl` 로 오버라이드 가능 (개발용).
    /// </summary>
    public class RemoteSkillRegistry : IRemoteSkillRegistry
    {
        private const string DefaultCatalogUrl =
            "https://cdn.opendesk.io/skills/catalog.json";
        private const string CatalogUrlPrefKey = "OpenDesk_SkillCatalogUrl";
        private const int TimeoutSeconds = 30;

        private string CatalogUrl =>
            PlayerPrefs.GetString(CatalogUrlPrefKey, DefaultCatalogUrl);

        public async UniTask<RemoteCatalogFetchResult> FetchCatalogAsync(
            string previousEtag,
            bool forceRefresh,
            CancellationToken ct)
        {
            var url = CatalogUrl;
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("[RemoteSkillRegistry] catalog URL 미설정");

            using var req = UnityWebRequest.Get(url);
            req.timeout = TimeoutSeconds;

            if (!forceRefresh && !string.IsNullOrEmpty(previousEtag))
                req.SetRequestHeader("If-None-Match", previousEtag);

            // SendWebRequest 를 폴링으로 대기 — UnityWebRequest 가 304 에서 예외를 던지지 않게 한다.
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(ct);
            }

            if (req.responseCode == 304)
            {
                Debug.Log("[RemoteSkillRegistry] catalog 304 Not Modified");
                return RemoteCatalogFetchResult.Cached(previousEtag);
            }

            if (req.result != UnityWebRequest.Result.Success || req.responseCode >= 400)
                throw new IOException($"catalog.json fetch failed: {req.error} ({req.responseCode})");

            var etag = req.GetResponseHeader("ETag") ?? string.Empty;
            var body = req.downloadHandler?.text;
            if (string.IsNullOrEmpty(body))
                throw new IOException("catalog.json response body empty");

            SkillCatalog catalog;
            try
            {
                catalog = JsonUtility.FromJson<SkillCatalog>(body);
            }
            catch (Exception ex)
            {
                throw new IOException($"catalog.json parse failed: {ex.Message}", ex);
            }

            if (catalog == null)
                throw new IOException("catalog.json deserialized to null");

            catalog.skills ??= new System.Collections.Generic.List<SkillCatalogEntry>();
            catalog.roleRecommendations ??= new System.Collections.Generic.List<RoleRecommendation>();

            Debug.Log($"[RemoteSkillRegistry] catalog fetched ({catalog.skills.Count} skills, etag={etag})");
            return RemoteCatalogFetchResult.Updated(catalog, etag);
        }

        public async UniTask<string> DownloadSkillPackageAsync(
            SkillCatalogEntry entry,
            string destinationTempPath,
            IProgress<float> progress,
            CancellationToken ct)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrEmpty(entry.downloadUrl))
                throw new InvalidOperationException($"downloadUrl missing for skill {entry.id}");
            if (string.IsNullOrEmpty(destinationTempPath))
                throw new ArgumentException("destinationTempPath required", nameof(destinationTempPath));

            // HTTPS 강제 (보안)
            if (!entry.downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"skill download URL must be HTTPS: {entry.downloadUrl}");

            EnsureParentDirectory(destinationTempPath);

            using var req = UnityWebRequest.Get(entry.downloadUrl);
            req.downloadHandler = new DownloadHandlerFile(destinationTempPath) { removeFileOnAbort = true };
            req.timeout = TimeoutSeconds;

            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(op.progress);
                await UniTask.Yield(ct);
            }
            progress?.Report(1f);

            if (req.result != UnityWebRequest.Result.Success)
            {
                SafeDelete(destinationTempPath);
                throw new IOException($"skill download failed: {req.error} ({req.responseCode})");
            }

            Debug.Log($"[RemoteSkillRegistry] downloaded {entry.id} → {destinationTempPath}");
            return destinationTempPath;
        }

        private static void EnsureParentDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }
    }
}
