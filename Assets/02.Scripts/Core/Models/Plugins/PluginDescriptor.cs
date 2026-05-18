using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// 플러그인의 런타임 표현. 카탈로그 엔트리 + 매니페스트 + 설치 상태를 통합한 immutable 모델.
    /// JsonUtility 직렬화 대상이 아님 (PluginManifest / PluginCatalogEntry 가 직렬화 책임).
    /// AgentPluginLoadoutService / IMcpConfigComposer 가 장착/조합 단위로 사용.
    /// </summary>
    public sealed record PluginDescriptor(
        string Id,
        string DisplayName,
        string Description,
        PluginVendor Vendor,
        PluginTransport Transport,
        string Version,
        string IconUrl,
        McpServerSpec ServerSpec,
        IReadOnlyList<CredentialRequirement> RequiredCredentials,
        string DownloadUrl,
        string Checksum,
        DateTime PublishedAt,
        bool IsInstalled,
        string InstallPath,
        // route_capability 가 조회하는 capability 목록 (예: "calendar.create_event").
        // manifest.json 의 "capabilities" 배열에서 파싱 — 빈 배열이면 route_capability 매칭에서 제외.
        IReadOnlyList<string> Capabilities = null
    )
    {
        public static PluginDescriptor Empty(string id) => new(
            Id: id ?? string.Empty,
            DisplayName: id ?? string.Empty,
            Description: string.Empty,
            Vendor: PluginVendor.Custom,
            Transport: PluginTransport.Stdio,
            Version: "0.0.0",
            IconUrl: string.Empty,
            ServerSpec: McpServerSpec.Empty,
            RequiredCredentials: Array.Empty<CredentialRequirement>(),
            DownloadUrl: string.Empty,
            Checksum: string.Empty,
            PublishedAt: DateTime.MinValue,
            IsInstalled: false,
            InstallPath: string.Empty,
            Capabilities: Array.Empty<string>()
        );

        public PluginDescriptor WithInstallState(bool isInstalled, string installPath) =>
            this with { IsInstalled = isInstalled, InstallPath = installPath ?? string.Empty };

        public PluginDescriptor WithServerSpec(McpServerSpec spec) =>
            this with { ServerSpec = spec ?? McpServerSpec.Empty };
    }
}
