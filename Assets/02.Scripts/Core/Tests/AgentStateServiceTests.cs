using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Tests
{
    public class AgentStateServiceTests
    {
        private AgentStateService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new AgentStateService();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void GetState_초기값_Idle()
        {
            Assert.AreEqual(AgentActionType.Idle, _service.GetState("main"));
            Assert.AreEqual(AgentActionType.Idle, _service.GetState("dev"));
        }

        [Test]
        public void ApplyEvent_TaskStarted_상태변경()
        {
            var e = new AgentEvent(AgentActionType.TaskStarted, sessionId: "main");
            _service.ApplyEvent(e);

            Assert.AreEqual(AgentActionType.TaskStarted, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_세션별_독립상태()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskStarted, sessionId: "main"));
            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskCompleted, sessionId: "dev"));

            Assert.AreEqual(AgentActionType.TaskStarted,  _service.GetState("main"));
            Assert.AreEqual(AgentActionType.TaskCompleted, _service.GetState("dev"));
        }

        [Test]
        public void ApplyEvent_같은상태반복_이벤트미발행()
        {
            var emittedCount = 0;
            _service.OnStateChanged.Subscribe(_ => emittedCount++);

            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskStarted, sessionId: "main"));
            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskStarted, sessionId: "main")); // 동일

            Assert.AreEqual(1, emittedCount); // 최초 1번만 발행
        }

        [Test]
        public void ForceState_즉시반영()
        {
            _service.ForceState("main", AgentActionType.Thinking);
            Assert.AreEqual(AgentActionType.Thinking, _service.GetState("main"));
        }

        [Test]
        public void OnStateChanged_구독_콜백호출()
        {
            var received = new List<(string, AgentActionType)>();
            _service.OnStateChanged.Subscribe(x => received.Add(x));

            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskStarted,   sessionId: "main"));
            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskCompleted,  sessionId: "main"));

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(AgentActionType.TaskStarted,  received[0].Item2);
            Assert.AreEqual(AgentActionType.TaskCompleted, received[1].Item2);
        }
    }
}
