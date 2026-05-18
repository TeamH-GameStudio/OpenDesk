using NUnit.Framework;
using OpenDesk.Core.Persistence;

namespace OpenDesk.Core.Tests.Plugins
{
    public class AgentPluginLoadoutDataTests
    {
        [Test]
        public void Equip_AddsPluginAndMarksDirty()
        {
            var data = new AgentPluginLoadoutData();
            data.InitializeDefault();

            var added = data.Equip("agent-1", "notion");

            Assert.IsTrue(added);
            Assert.IsTrue(data.IsDirty);
            Assert.AreEqual(1, data.GetLoadout("agent-1").EquippedPluginIds.Count);
        }

        [Test]
        public void Equip_SamePluginTwice_IsNoOp()
        {
            var data = new AgentPluginLoadoutData();
            data.InitializeDefault();

            data.Equip("a", "notion");
            data.ResetDirty();
            var addedTwice = data.Equip("a", "notion");

            Assert.IsFalse(addedTwice);
            Assert.IsFalse(data.IsDirty);
        }

        [Test]
        public void Unequip_RemovesPluginAndMarksDirty()
        {
            var data = new AgentPluginLoadoutData();
            data.InitializeDefault();
            data.Equip("a", "notion");
            data.Equip("a", "github");
            data.ResetDirty();

            var removed = data.Unequip("a", "notion");

            Assert.IsTrue(removed);
            Assert.IsTrue(data.IsDirty);
            var loadout = data.GetLoadout("a");
            Assert.AreEqual(1, loadout.EquippedPluginIds.Count);
            Assert.AreEqual("github", loadout.EquippedPluginIds[0]);
        }

        [Test]
        public void Unequip_MissingAgent_ReturnsFalse()
        {
            var data = new AgentPluginLoadoutData();
            data.InitializeDefault();
            Assert.IsFalse(data.Unequip("ghost", "x"));
            Assert.IsFalse(data.IsDirty);
        }

        [Test]
        public void ToJson_FromJson_RoundTripPreservesEntries()
        {
            var data = new AgentPluginLoadoutData();
            data.InitializeDefault();
            data.Equip("a1", "notion");
            data.Equip("a1", "figma");
            data.Equip("a2", "github");

            var json = data.ToJson();

            var loaded = new AgentPluginLoadoutData();
            loaded.FromJson(json);

            var l1 = loaded.GetLoadout("a1");
            var l2 = loaded.GetLoadout("a2");
            Assert.AreEqual(2, l1.EquippedPluginIds.Count);
            Assert.AreEqual(1, l2.EquippedPluginIds.Count);
            Assert.AreEqual("github", l2.EquippedPluginIds[0]);
        }

        [Test]
        public void FromJson_NullOrEmpty_ResultsInEmptyLoadouts()
        {
            var data = new AgentPluginLoadoutData();
            data.FromJson(null);
            Assert.IsTrue(data.GetLoadout("any").IsEmpty);
            data.FromJson(string.Empty);
            Assert.IsTrue(data.GetLoadout("any").IsEmpty);
        }
    }
}
