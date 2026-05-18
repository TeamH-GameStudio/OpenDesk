using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Services.Plugins;
using R3;

namespace OpenDesk.Core.Tests.Plugins
{
    public class McpConfigComposerTests
    {
        private string _credDir;
        private StubLoadoutService _loadout;
        private InMemoryPluginCatalogService _catalog;
        private PluginCredentialService _credentials;
        private McpConfigComposer _composer;

        [SetUp]
        public void SetUp()
        {
            _credDir = Path.Combine(Path.GetTempPath(), "OpenDeskMcpComposerTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_credDir);

            _loadout = new StubLoadoutService();
            _catalog = new InMemoryPluginCatalogService();
            _credentials = new PluginCredentialService(() => _credDir);
            _composer = new McpConfigComposer(_loadout, _catalog, _credentials);
        }

        [TearDown]
        public void TearDown()
        {
            _credentials.Dispose();
            _catalog.Dispose();
            try { if (Directory.Exists(_credDir)) Directory.Delete(_credDir, recursive: true); }
            catch { /* ignore */ }
        }

        [Test]
        public void Compose_NoLoadout_ReturnsEmptyPayload()
        {
            var payload = _composer.ComposeAsync("agent-1").AsTask().Result;
            Assert.AreEqual("agent-1", payload.agentId);
            Assert.IsTrue(payload.IsEmpty);
        }

        [Test]
        public void Compose_SubstitutesEnvTokensFromCredentials()
        {
            _catalog.RegisterLocal(MakeNotionDescriptor());
            _loadout.SetEquipped("a", new[] { "notion" });
            _credentials.SetAsync("notion", "NOTION_API_KEY", "secret-xyz").AsTask().Wait();

            var payload = _composer.ComposeAsync("a").AsTask().Result;

            Assert.AreEqual(1, payload.servers.Count);
            var server = payload.servers[0];
            Assert.AreEqual("notion", server.name);
            Assert.AreEqual("stdio", server.transport);
            Assert.AreEqual("npx", server.command);
            Assert.AreEqual(2, server.args.Count);
            Assert.AreEqual(1, server.env.Count);
            Assert.AreEqual("NOTION_API_KEY", server.env[0].key);
            Assert.AreEqual("secret-xyz", server.env[0].value);
        }

        [Test]
        public void Compose_SkipsServerWhenRequiredCredentialMissing()
        {
            _catalog.RegisterLocal(MakeNotionDescriptor());
            _loadout.SetEquipped("a", new[] { "notion" });

            var payload = _composer.ComposeAsync("a").AsTask().Result;

            Assert.IsTrue(payload.IsEmpty);
        }

        [Test]
        public void Compose_IgnoresUnknownPluginId()
        {
            _loadout.SetEquipped("a", new[] { "ghost" });
            var payload = _composer.ComposeAsync("a").AsTask().Result;
            Assert.IsTrue(payload.IsEmpty);
        }

        [Test]
        public void Compose_IgnoresPluginWithEmptyServerSpec()
        {
            _catalog.RegisterLocal(PluginDescriptor.Empty("broken"));
            _loadout.SetEquipped("a", new[] { "broken" });
            var payload = _composer.ComposeAsync("a").AsTask().Result;
            Assert.IsTrue(payload.IsEmpty);
        }

        [Test]
        public void Compose_HandlesMultipleEquippedPlugins()
        {
            _catalog.RegisterLocal(MakeNotionDescriptor());
            _catalog.RegisterLocal(MakeGithubDescriptor());
            _loadout.SetEquipped("a", new[] { "notion", "github" });
            _credentials.SetAsync("notion", "NOTION_API_KEY", "n-key").AsTask().Wait();
            _credentials.SetAsync("github", "GITHUB_TOKEN", "ghp_xxx").AsTask().Wait();

            var payload = _composer.ComposeAsync("a").AsTask().Result;

            Assert.AreEqual(2, payload.servers.Count);
        }

        // ── helpers ──

        private static PluginDescriptor MakeNotionDescriptor() => new(
            Id: "notion",
            DisplayName: "Notion",
            Description: string.Empty,
            Vendor: PluginVendor.Notion,
            Transport: PluginTransport.Stdio,
            Version: "1.0.0",
            IconUrl: string.Empty,
            ServerSpec: new McpServerSpec(
                "npx",
                new[] { "-y", "@notion/mcp-server" },
                new Dictionary<string, string> { ["NOTION_API_KEY"] = "{{NOTION_API_KEY}}" }
            ),
            RequiredCredentials: new[]
            {
                new CredentialRequirement("NOTION_API_KEY", "API Key", CredentialKind.ApiKey, Optional: false),
            },
            DownloadUrl: string.Empty,
            Checksum: string.Empty,
            PublishedAt: System.DateTime.MinValue,
            IsInstalled: true,
            InstallPath: "/p/notion"
        );

        private static PluginDescriptor MakeGithubDescriptor() => new(
            Id: "github",
            DisplayName: "GitHub",
            Description: string.Empty,
            Vendor: PluginVendor.GitHub,
            Transport: PluginTransport.Stdio,
            Version: "1.0.0",
            IconUrl: string.Empty,
            ServerSpec: new McpServerSpec(
                "github-mcp",
                System.Array.Empty<string>(),
                new Dictionary<string, string> { ["GITHUB_TOKEN"] = "{{GITHUB_TOKEN}}" }
            ),
            RequiredCredentials: new[]
            {
                new CredentialRequirement("GITHUB_TOKEN", "Personal Access Token", CredentialKind.Bearer, Optional: false),
            },
            DownloadUrl: string.Empty,
            Checksum: string.Empty,
            PublishedAt: System.DateTime.MinValue,
            IsInstalled: true,
            InstallPath: "/p/github"
        );

        private sealed class StubLoadoutService : IAgentPluginLoadoutService
        {
            private readonly Dictionary<string, AgentPluginLoadout> _by = new();
            private readonly Subject<AgentPluginLoadout> _changed = new();

            public Observable<AgentPluginLoadout> OnLoadoutChanged => _changed;

            public AgentPluginLoadout GetLoadout(string agentId)
                => _by.TryGetValue(agentId, out var l) ? l : AgentPluginLoadout.Empty(agentId);

            public UniTask<bool> EquipAsync(string agentId, string pluginId) => UniTask.FromResult(false);
            public UniTask<bool> UnequipAsync(string agentId, string pluginId) => UniTask.FromResult(false);

            public void SetEquipped(string agentId, IEnumerable<string> ids)
            {
                _by[agentId] = new AgentPluginLoadout(agentId, new List<string>(ids));
            }
        }
    }
}
