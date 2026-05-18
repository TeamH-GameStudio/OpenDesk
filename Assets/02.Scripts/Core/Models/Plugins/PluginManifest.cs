using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// 설치된 플러그인 디렉토리(~/.opendesk/plugins/{id}/) 의 manifest.json 매핑.
    /// JsonUtility 호환 [Serializable] class.
    /// </summary>
    [Serializable]
    public class PluginManifest
    {
        public string id;
        public string displayName;
        public string description;
        public string vendor;            // PluginVendorExtensions.ParseVendor 로 변환
        public string transport;         // PluginVendorExtensions.ParseTransport
        public string version;
        public string iconUrl;
        public McpServerSpecData serverSpec = new();
        public List<CredentialRequirementData> requiredCredentials = new();
        // route_capability 라우팅용 capability 목록. e.g. ["calendar.create_event", "calendar.read"].
        public List<string> capabilities = new();

        public PluginDescriptor ToDescriptor(string installPath)
        {
            var vendorEnum = PluginVendorExtensions.ParseVendor(vendor);
            var transportEnum = PluginVendorExtensions.ParseTransport(transport);
            var spec = serverSpec != null ? serverSpec.ToSpec() : McpServerSpec.Empty;
            var creds = ParseCredentials(requiredCredentials);
            var caps = capabilities != null
                ? capabilities.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray()
                : Array.Empty<string>();

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
                DownloadUrl: string.Empty,
                Checksum: string.Empty,
                PublishedAt: DateTime.MinValue,
                IsInstalled: !string.IsNullOrEmpty(installPath),
                InstallPath: installPath ?? string.Empty,
                Capabilities: caps
            );
        }

        internal static IReadOnlyList<CredentialRequirement> ParseCredentials(
            List<CredentialRequirementData> raw)
        {
            if (raw == null || raw.Count == 0)
                return Array.Empty<CredentialRequirement>();

            var result = new List<CredentialRequirement>(raw.Count);
            foreach (var entry in raw)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                result.Add(new CredentialRequirement(
                    Key: entry.key,
                    DisplayName: string.IsNullOrEmpty(entry.displayName) ? entry.key : entry.displayName,
                    Kind: PluginVendorExtensions.ParseCredentialKind(entry.kind),
                    Optional: entry.optional
                ));
            }
            return result;
        }

        internal static List<CredentialRequirementData> SerializeCredentials(
            IReadOnlyList<CredentialRequirement> creds)
        {
            var list = new List<CredentialRequirementData>();
            if (creds == null) return list;
            foreach (var c in creds)
            {
                list.Add(new CredentialRequirementData
                {
                    key = c.Key ?? string.Empty,
                    displayName = c.DisplayName ?? string.Empty,
                    kind = c.Kind.ToSerializedKey(),
                    optional = c.Optional,
                });
            }
            return list;
        }
    }

    [Serializable]
    public class CredentialRequirementData
    {
        public string key;
        public string displayName;
        public string kind;       // PluginVendorExtensions.ParseCredentialKind
        public bool optional;
    }
}
