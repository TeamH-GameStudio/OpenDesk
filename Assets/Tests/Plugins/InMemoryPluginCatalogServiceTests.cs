using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Services.Plugins;

namespace OpenDesk.Core.Tests.Plugins
{
    public class InMemoryPluginCatalogServiceTests
    {
        [Test]
        public void NewService_HasNothingLoaded()
        {
            using var svc = new InMemoryPluginCatalogService();
            Assert.IsFalse(svc.IsLoaded);
            Assert.AreEqual(0, svc.GetAll().Count);
            Assert.IsNull(svc.GetById("notion"));
        }

        [Test]
        public void RegisterLocal_AddsDescriptorAndIsRetrievable()
        {
            using var svc = new InMemoryPluginCatalogService();
            var descriptor = PluginDescriptor.Empty("notion") with
            {
                DisplayName = "Notion",
                Vendor = PluginVendor.Notion,
            };

            svc.RegisterLocal(descriptor);

            Assert.IsTrue(svc.IsLoaded);
            Assert.AreEqual("Notion", svc.GetById("notion").DisplayName);
            Assert.AreEqual(1, svc.GetByVendor(PluginVendor.Notion).Count);
            Assert.AreEqual(0, svc.GetByVendor(PluginVendor.Figma).Count);
        }

        [Test]
        public void NotifyInstallStateChanged_UpdatesExistingDescriptor()
        {
            using var svc = new InMemoryPluginCatalogService();
            svc.RegisterLocal(PluginDescriptor.Empty("notion"));

            svc.NotifyInstallStateChanged("notion", isInstalled: true, installPath: "/p/notion");

            var d = svc.GetById("notion");
            Assert.IsTrue(d.IsInstalled);
            Assert.AreEqual("/p/notion", d.InstallPath);
        }

        [Test]
        public void RefreshAsync_OnInMemoryImpl_DoesNothing()
        {
            using var svc = new InMemoryPluginCatalogService();
            var changed = svc.RefreshAsync(forceRefresh: true, CancellationToken.None).AsTask().Result;
            Assert.IsFalse(changed);
        }

        [Test]
        public void RegisterLocal_NullOrEmptyId_IsIgnored()
        {
            using var svc = new InMemoryPluginCatalogService();
            svc.RegisterLocal(null);
            svc.RegisterLocal(PluginDescriptor.Empty(string.Empty));
            Assert.IsFalse(svc.IsLoaded);
        }
    }
}
