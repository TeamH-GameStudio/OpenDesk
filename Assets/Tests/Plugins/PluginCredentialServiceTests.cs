using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Services.Plugins;

namespace OpenDesk.Core.Tests.Plugins
{
    public class PluginCredentialServiceTests
    {
        private string _tmpDir;

        [SetUp]
        public void SetUp()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), "OpenDeskPluginCredTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true); }
            catch { /* ignore */ }
        }

        private PluginCredentialService MakeService() => new(() => _tmpDir);

        [Test]
        public void SetThenGet_RoundTripsValue()
        {
            using var svc = MakeService();
            svc.SetAsync("notion", "NOTION_API_KEY", "secret-xyz").AsTask().Wait();
            var value = svc.GetAsync("notion", "NOTION_API_KEY").AsTask().Result;
            Assert.AreEqual("secret-xyz", value);
        }

        [Test]
        public void Get_UnknownKey_ReturnsNull()
        {
            using var svc = MakeService();
            var value = svc.GetAsync("notion", "MISSING").AsTask().Result;
            Assert.IsNull(value);
        }

        [Test]
        public void DeleteAsync_RemovesSingleKey()
        {
            using var svc = MakeService();
            svc.SetAsync("notion", "A", "1").AsTask().Wait();
            svc.SetAsync("notion", "B", "2").AsTask().Wait();

            svc.DeleteAsync("notion", "A").AsTask().Wait();

            Assert.IsNull(svc.GetAsync("notion", "A").AsTask().Result);
            Assert.AreEqual("2", svc.GetAsync("notion", "B").AsTask().Result);
        }

        [Test]
        public void DeleteAllAsync_RemovesAllKeysForPlugin()
        {
            using var svc = MakeService();
            svc.SetAsync("notion", "A", "1").AsTask().Wait();
            svc.SetAsync("notion", "B", "2").AsTask().Wait();

            svc.DeleteAllAsync("notion").AsTask().Wait();

            Assert.IsNull(svc.GetAsync("notion", "A").AsTask().Result);
            Assert.IsNull(svc.GetAsync("notion", "B").AsTask().Result);
        }

        [Test]
        public void HasAllRequiredAsync_FalseWhenAnyMandatoryMissing()
        {
            using var svc = MakeService();
            var descriptor = PluginDescriptor.Empty("notion") with
            {
                RequiredCredentials = new[]
                {
                    new CredentialRequirement("NOTION_API_KEY", "API Key", CredentialKind.ApiKey, Optional: false),
                    new CredentialRequirement("OPT", "Optional", CredentialKind.Custom, Optional: true),
                },
            };

            Assert.IsFalse(svc.HasAllRequiredAsync(descriptor).AsTask().Result);

            svc.SetAsync("notion", "NOTION_API_KEY", "x").AsTask().Wait();
            Assert.IsTrue(svc.HasAllRequiredAsync(descriptor).AsTask().Result);
        }

        [Test]
        public void HasAllRequiredAsync_TrueWhenOnlyOptionalsAreMissing()
        {
            using var svc = MakeService();
            var descriptor = PluginDescriptor.Empty("notion") with
            {
                RequiredCredentials = new[]
                {
                    new CredentialRequirement("OPT", "Optional", CredentialKind.Custom, Optional: true),
                },
            };
            Assert.IsTrue(svc.HasAllRequiredAsync(descriptor).AsTask().Result);
        }

        [Test]
        public void SetAsync_NullOrEmptyId_IsNoOp()
        {
            using var svc = MakeService();
            svc.SetAsync(null, "K", "v").AsTask().Wait();
            svc.SetAsync(string.Empty, "K", "v").AsTask().Wait();
            svc.SetAsync("notion", null, "v").AsTask().Wait();
            Assert.IsEmpty(Directory.GetFiles(_tmpDir));
        }
    }
}
