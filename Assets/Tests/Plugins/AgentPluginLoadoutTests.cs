using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Tests.Plugins
{
    public class AgentPluginLoadoutTests
    {
        [Test]
        public void Empty_HasNoEquippedPlugins()
        {
            var loadout = AgentPluginLoadout.Empty("agent-1");
            Assert.IsTrue(loadout.IsEmpty);
            Assert.AreEqual("agent-1", loadout.AgentId);
            Assert.AreEqual(0, loadout.EquippedPluginIds.Count);
        }

        [Test]
        public void WithEquip_AddsPluginId()
        {
            var loadout = AgentPluginLoadout.Empty("agent-1").WithEquip("notion");
            Assert.IsFalse(loadout.IsEmpty);
            Assert.AreEqual(1, loadout.EquippedPluginIds.Count);
            Assert.AreEqual("notion", loadout.EquippedPluginIds[0]);
        }

        [Test]
        public void WithEquip_IsIdempotent_ForSameId()
        {
            var loadout = AgentPluginLoadout.Empty("a")
                .WithEquip("notion")
                .WithEquip("notion");
            Assert.AreEqual(1, loadout.EquippedPluginIds.Count);
        }

        [Test]
        public void WithUnequip_RemovesPluginId()
        {
            var loadout = AgentPluginLoadout.Empty("a")
                .WithEquip("notion")
                .WithEquip("github")
                .WithUnequip("notion");
            Assert.AreEqual(1, loadout.EquippedPluginIds.Count);
            Assert.AreEqual("github", loadout.EquippedPluginIds[0]);
        }

        [Test]
        public void Record_IsImmutable_OriginalUnchangedAfterWith()
        {
            var original = AgentPluginLoadout.Empty("a");
            var equipped = original.WithEquip("notion");
            Assert.IsTrue(original.IsEmpty);
            Assert.IsFalse(equipped.IsEmpty);
        }

        [Test]
        public void PersistedData_RoundTrip_PreservesEntries()
        {
            var data = new AgentPluginLoadoutPersistedData
            {
                entries = new List<PersistedPluginLoadoutEntry>
                {
                    new() { agentId = "a1", equippedPluginIds = new List<string> { "notion", "github" } },
                    new() { agentId = "a2", equippedPluginIds = new List<string> { "figma" } },
                },
            };

            var json = JsonUtility.ToJson(data);
            var revived = JsonUtility.FromJson<AgentPluginLoadoutPersistedData>(json);

            Assert.AreEqual(2, revived.entries.Count);
            Assert.AreEqual("a1", revived.entries[0].agentId);
            Assert.AreEqual(2, revived.entries[0].equippedPluginIds.Count);
            Assert.AreEqual("figma", revived.entries[1].equippedPluginIds[0]);
        }
    }
}
