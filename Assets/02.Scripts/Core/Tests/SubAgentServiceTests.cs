using NUnit.Framework;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Models;

namespace OpenDesk.Core.Tests
{
    public class SubAgentServiceTests
    {
        private SubAgentService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new SubAgentService();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void OnSubAgentSpawned_목록에추가()
        {
            var e = new AgentEvent(
                AgentActionType.SubAgentSpawned,
                sessionId:  "main",
                subAgentId: "sub_001",
                taskName:   "데이터 분석"
            );

            _service.OnSubAgentSpawned(e);

            Assert.AreEqual(1, _service.ActiveAgents.CurrentValue.Count);
            Assert.AreEqual("sub_001", _service.ActiveAgents.CurrentValue[0].Id);
        }

        [Test]
        public void OnSubAgentCompleted_목록에서제거()
        {
            var spawn = new AgentEvent(AgentActionType.SubAgentSpawned,
                sessionId: "main", subAgentId: "sub_001", taskName: "분석");
            var done = new AgentEvent(AgentActionType.SubAgentCompleted,
                subAgentId: "sub_001");

            _service.OnSubAgentSpawned(spawn);
            _service.OnSubAgentCompleted(done);

            Assert.AreEqual(0, _service.ActiveAgents.CurrentValue.Count);
        }

        [Test]
        public void OnSubAgentSpawned_중복ID_무시()
        {
            var e = new AgentEvent(AgentActionType.SubAgentSpawned,
                subAgentId: "sub_001", taskName: "분석");

            _service.OnSubAgentSpawned(e);
            _service.OnSubAgentSpawned(e); // 동일 ID 중복

            Assert.AreEqual(1, _service.ActiveAgents.CurrentValue.Count);
        }

        [Test]
        public void OnSubAgentFailed_목록에서제거()
        {
            var spawn = new AgentEvent(AgentActionType.SubAgentSpawned,
                subAgentId: "sub_001", taskName: "분석");
            var fail = new AgentEvent(AgentActionType.SubAgentFailed,
                subAgentId: "sub_001");

            _service.OnSubAgentSpawned(spawn);
            _service.OnSubAgentFailed(fail);

            Assert.AreEqual(0, _service.ActiveAgents.CurrentValue.Count);
        }

        [Test]
        public void ActiveAgents_여러서브에이전트_전부표시()
        {
            _service.OnSubAgentSpawned(new AgentEvent(
                AgentActionType.SubAgentSpawned, subAgentId: "sub_001", taskName: "크롤링"));
            _service.OnSubAgentSpawned(new AgentEvent(
                AgentActionType.SubAgentSpawned, subAgentId: "sub_002", taskName: "분석"));

            Assert.AreEqual(2, _service.ActiveAgents.CurrentValue.Count);
        }
    }
}
