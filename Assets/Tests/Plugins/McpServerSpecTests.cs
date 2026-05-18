using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Tests.Plugins
{
    public class McpServerSpecTests
    {
        [Test]
        public void Empty_HasNoCommandAndIsInvalid()
        {
            var spec = McpServerSpec.Empty;
            Assert.AreEqual(string.Empty, spec.Command);
            Assert.AreEqual(0, spec.Args.Count);
            Assert.AreEqual(0, spec.EnvTemplate.Count);
            Assert.IsFalse(spec.IsValid);
        }

        [Test]
        public void IsValid_True_WhenCommandIsNonEmpty()
        {
            var spec = new McpServerSpec("npx", new[] { "-y" }, new Dictionary<string, string>());
            Assert.IsTrue(spec.IsValid);
        }

        [Test]
        public void Data_RoundTrip_PreservesAllFields()
        {
            var spec = new McpServerSpec(
                "npx",
                new[] { "-y", "@notion/mcp-server" },
                new Dictionary<string, string> { ["NOTION_API_KEY"] = "{{NOTION_API_KEY}}" }
            );
            var data = McpServerSpecData.FromSpec(spec);
            var json = JsonUtility.ToJson(data);
            var revivedData = JsonUtility.FromJson<McpServerSpecData>(json);
            var revivedSpec = revivedData.ToSpec();

            Assert.AreEqual("npx", revivedSpec.Command);
            Assert.AreEqual(2, revivedSpec.Args.Count);
            Assert.AreEqual("-y", revivedSpec.Args[0]);
            Assert.AreEqual(1, revivedSpec.EnvTemplate.Count);
            Assert.AreEqual("{{NOTION_API_KEY}}", revivedSpec.EnvTemplate["NOTION_API_KEY"]);
        }

        [Test]
        public void Data_ToSpec_HandlesNullCollections()
        {
            var data = new McpServerSpecData { command = "x", args = null, env = null };
            var spec = data.ToSpec();
            Assert.AreEqual("x", spec.Command);
            Assert.AreEqual(0, spec.Args.Count);
            Assert.AreEqual(0, spec.EnvTemplate.Count);
        }

        [Test]
        public void Data_ToSpec_SkipsEntriesWithEmptyKeys()
        {
            var data = new McpServerSpecData
            {
                command = "x",
                args = new List<string>(),
                env = new List<McpEnvEntry>
                {
                    new() { key = "GOOD", value = "v" },
                    new() { key = "", value = "x" },
                    new() { key = null, value = "y" },
                },
            };
            var spec = data.ToSpec();
            Assert.AreEqual(1, spec.EnvTemplate.Count);
            Assert.AreEqual("v", spec.EnvTemplate["GOOD"]);
        }
    }
}
