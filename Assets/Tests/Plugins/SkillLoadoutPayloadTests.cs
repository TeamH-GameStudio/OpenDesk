using NUnit.Framework;
using OpenDesk.Core.Models.Skills;
using UnityEngine;

namespace OpenDesk.Core.Tests.Plugins
{
    public class SkillLoadoutPayloadTests
    {
        [Test]
        public void RoundTrip_PreservesAllFields()
        {
            var payload = new SkillLoadoutPayload { agentId = "agent-1" };
            payload.skills.Add(new SkillLoadoutEntry
            {
                id = "code-reviewer",
                name = "Code Reviewer",
                description = "Reviews code for security/perf",
                body = "# 상세 행동 지침\n...",
            });
            payload.skills.Add(new SkillLoadoutEntry
            {
                id = "doc-writer",
                name = "Doc Writer",
                description = "Writes docs",
                body = "",
            });

            var json = JsonUtility.ToJson(payload);
            var revived = JsonUtility.FromJson<SkillLoadoutPayload>(json);

            Assert.AreEqual("agent-1", revived.agentId);
            Assert.AreEqual(2, revived.skills.Count);
            Assert.AreEqual("code-reviewer", revived.skills[0].id);
            Assert.AreEqual("Reviews code for security/perf", revived.skills[0].description);
            Assert.AreEqual("# 상세 행동 지침\n...", revived.skills[0].body);
            Assert.AreEqual(string.Empty, revived.skills[1].body);
        }

        [Test]
        public void EmptyPayload_HasNoSkills()
        {
            var payload = new SkillLoadoutPayload();
            Assert.IsNotNull(payload.skills);
            Assert.AreEqual(0, payload.skills.Count);
        }
    }
}
