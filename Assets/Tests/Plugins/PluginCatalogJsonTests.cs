using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Tests.Plugins
{
    public class PluginCatalogJsonTests
    {
        [Test]
        public void Catalog_RoundTrip_PreservesEntries()
        {
            var catalog = new PluginCatalog
            {
                schemaVersion = "1.0",
                updatedAt = 1714000000,
                plugins = new List<PluginCatalogEntry>
                {
                    new()
                    {
                        id = "notion",
                        displayName = "Notion",
                        vendor = "notion",
                        transport = "stdio",
                        version = "1.0.0",
                        downloadUrl = "https://example.com/notion.zip",
                        checksum = "sha256:abc",
                        publishedAt = 1713000000,
                    },
                    new()
                    {
                        id = "figma",
                        displayName = "Figma",
                        vendor = "figma",
                        transport = "sse",
                        version = "0.9.0",
                    },
                },
            };

            var json = JsonUtility.ToJson(catalog);
            var revived = JsonUtility.FromJson<PluginCatalog>(json);

            Assert.AreEqual("1.0", revived.schemaVersion);
            Assert.AreEqual(1714000000, revived.updatedAt);
            Assert.AreEqual(2, revived.plugins.Count);
            Assert.AreEqual("notion", revived.plugins[0].id);
            Assert.AreEqual("sha256:abc", revived.plugins[0].checksum);
            Assert.AreEqual("figma", revived.plugins[1].id);
            Assert.AreEqual("sse", revived.plugins[1].transport);
        }

        [Test]
        public void CatalogEntry_ToDescriptor_FillsFromManifestWhenPresent()
        {
            var entry = new PluginCatalogEntry
            {
                id = "notion",
                vendor = "notion",
                transport = "stdio",
                version = "1.0.0",
            };
            var manifest = new PluginManifest
            {
                id = "notion",
                vendor = "notion",
                transport = "stdio",
                serverSpec = new McpServerSpecData
                {
                    command = "npx",
                    args = new List<string> { "-y", "@notion/mcp-server" },
                    env = new List<McpEnvEntry>
                    {
                        new() { key = "NOTION_API_KEY", value = "{{NOTION_API_KEY}}" },
                    },
                },
                requiredCredentials = new List<CredentialRequirementData>
                {
                    new() { key = "NOTION_API_KEY", kind = "api-key" },
                },
            };

            var descriptor = entry.ToDescriptor(manifest, isInstalled: true, installPath: "/p/notion");

            Assert.IsTrue(descriptor.IsInstalled);
            Assert.AreEqual("npx", descriptor.ServerSpec.Command);
            Assert.AreEqual(2, descriptor.ServerSpec.Args.Count);
            Assert.AreEqual(1, descriptor.RequiredCredentials.Count);
            Assert.AreEqual(CredentialKind.ApiKey, descriptor.RequiredCredentials[0].Kind);
        }

        [Test]
        public void CatalogEntry_ToDescriptor_FallsBackToCatalogCredentialsWhenNoManifest()
        {
            var entry = new PluginCatalogEntry
            {
                id = "github",
                vendor = "github",
                transport = "stdio",
                requiredCredentials = new List<CredentialRequirementData>
                {
                    new() { key = "GITHUB_TOKEN", kind = "bearer" },
                },
            };

            var descriptor = entry.ToDescriptor(manifestOrNull: null, isInstalled: false, installPath: null);

            Assert.IsFalse(descriptor.IsInstalled);
            Assert.AreEqual(McpServerSpec.Empty, descriptor.ServerSpec);
            Assert.AreEqual(1, descriptor.RequiredCredentials.Count);
            Assert.AreEqual(CredentialKind.Bearer, descriptor.RequiredCredentials[0].Kind);
        }
    }
}
