using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Tests.Plugins
{
    public class PluginManifestJsonTests
    {
        [Test]
        public void Manifest_RoundTrip_PreservesCoreFields()
        {
            var original = new PluginManifest
            {
                id = "notion",
                displayName = "Notion",
                description = "Notion workspace integration",
                vendor = "notion",
                transport = "stdio",
                version = "1.2.3",
                iconUrl = "https://example.com/notion.png",
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
                    new() { key = "NOTION_API_KEY", displayName = "Notion API Key", kind = "api-key", optional = false },
                },
            };

            var json = JsonUtility.ToJson(original);
            var revived = JsonUtility.FromJson<PluginManifest>(json);

            Assert.AreEqual("notion", revived.id);
            Assert.AreEqual("Notion", revived.displayName);
            Assert.AreEqual("stdio", revived.transport);
            Assert.AreEqual("1.2.3", revived.version);
            Assert.AreEqual("npx", revived.serverSpec.command);
            Assert.AreEqual(2, revived.serverSpec.args.Count);
            Assert.AreEqual("-y", revived.serverSpec.args[0]);
            Assert.AreEqual(1, revived.serverSpec.env.Count);
            Assert.AreEqual("NOTION_API_KEY", revived.serverSpec.env[0].key);
            Assert.AreEqual("{{NOTION_API_KEY}}", revived.serverSpec.env[0].value);
            Assert.AreEqual(1, revived.requiredCredentials.Count);
            Assert.AreEqual("NOTION_API_KEY", revived.requiredCredentials[0].key);
            Assert.IsFalse(revived.requiredCredentials[0].optional);
        }

        [Test]
        public void Manifest_ToDescriptor_ResolvesEnumsAndPaths()
        {
            var manifest = new PluginManifest
            {
                id = "github",
                displayName = "GitHub",
                vendor = "github",
                transport = "stdio",
                version = "0.5.0",
                serverSpec = new McpServerSpecData
                {
                    command = "github-mcp",
                    args = new List<string>(),
                    env = new List<McpEnvEntry>(),
                },
                requiredCredentials = new List<CredentialRequirementData>
                {
                    new() { key = "GITHUB_TOKEN", displayName = "Personal Access Token", kind = "bearer", optional = false },
                },
            };

            var descriptor = manifest.ToDescriptor("/home/user/.opendesk/plugins/github");

            Assert.AreEqual("github", descriptor.Id);
            Assert.AreEqual(PluginVendor.GitHub, descriptor.Vendor);
            Assert.AreEqual(PluginTransport.Stdio, descriptor.Transport);
            Assert.IsTrue(descriptor.IsInstalled);
            Assert.AreEqual("/home/user/.opendesk/plugins/github", descriptor.InstallPath);
            Assert.AreEqual("github-mcp", descriptor.ServerSpec.Command);
            Assert.AreEqual(1, descriptor.RequiredCredentials.Count);
            Assert.AreEqual(CredentialKind.Bearer, descriptor.RequiredCredentials[0].Kind);
        }

        [Test]
        public void Manifest_ToDescriptor_HandlesNullsAndUnknownVendor()
        {
            var manifest = new PluginManifest
            {
                id = "weird-thing",
                displayName = null,
                vendor = "no-such-vendor",
                transport = null,
                serverSpec = null,
                requiredCredentials = null,
            };

            var descriptor = manifest.ToDescriptor(installPath: null);

            Assert.AreEqual("weird-thing", descriptor.Id);
            Assert.AreEqual("weird-thing", descriptor.DisplayName);
            Assert.AreEqual(PluginVendor.Custom, descriptor.Vendor);
            Assert.AreEqual(PluginTransport.Stdio, descriptor.Transport);
            Assert.IsFalse(descriptor.IsInstalled);
            Assert.AreEqual(string.Empty, descriptor.InstallPath);
            Assert.AreEqual(McpServerSpec.Empty, descriptor.ServerSpec);
            Assert.AreEqual(0, descriptor.RequiredCredentials.Count);
        }
    }
}
