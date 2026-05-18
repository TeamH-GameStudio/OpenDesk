using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// 원격 plugins-catalog.json 의 단일 엔트리. 카탈로그는 메타데이터 + 설치 위치만 들고
    /// 실제 ServerSpec 은 zip 내부 manifest.json 에서 가져온다.
    /// </summary>
    [Serializable]
    public class PluginCatalogEntry
    {
        public string id;
        public string displayName;
        public string description;
        public string vendor;
        public string transport;
        public string version;
        public string iconUrl;
        public string downloadUrl;
        public string checksum;
        public long publishedAt;     // Unix epoch (sec)
        public List<CredentialRequirementData> requiredCredentials = new();

        public PluginDescriptor ToDescriptor(PluginManifest manifestOrNull, bool isInstalled, string installPath)
        {
            var vendorEnum = PluginVendorExtensions.ParseVendor(vendor);
            var transportEnum = PluginVendorExtensions.ParseTransport(transport);
            var spec = manifestOrNull?.serverSpec != null
                ? manifestOrNull.serverSpec.ToSpec()
                : McpServerSpec.Empty;

            // 카탈로그 자체에 자격증명 명세가 있을 수 있고, manifest 가 있다면 manifest 가 우선.
            var rawCreds = manifestOrNull?.requiredCredentials ?? requiredCredentials;
            var creds = PluginManifest.ParseCredentials(rawCreds);

            return new PluginDescriptor(
                Id: id ?? string.Empty,
                DisplayName: string.IsNullOrEmpty(displayName) ? id : displayName,
                Description: description ?? string.Empty,
                Vendor: vendorEnum,
                Transport: transportEnum,
                Version: version ?? "0.0.0",
                IconUrl: iconUrl ?? string.Empty,
                ServerSpec: spec,
                RequiredCredentials: creds,
                DownloadUrl: downloadUrl ?? string.Empty,
                Checksum: checksum ?? string.Empty,
                PublishedAt: FromUnixSeconds(publishedAt),
                IsInstalled: isInstalled,
                InstallPath: installPath ?? string.Empty
            );
        }

        private static DateTime FromUnixSeconds(long sec)
        {
            if (sec <= 0) return DateTime.MinValue;
            try { return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime; }
            catch { return DateTime.MinValue; }
        }
    }

    /// <summary>
    /// 원격 plugins-catalog.json 최상위. RemotePluginRegistry 가 HTTP 로 가져와 캐시한다.
    /// </summary>
    [Serializable]
    public class PluginCatalog
    {
        public string schemaVersion = "1.0";
        public long updatedAt;
        public List<PluginCatalogEntry> plugins = new();
    }
}
