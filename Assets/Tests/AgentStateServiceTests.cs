using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Tests
{
    /// <summary>
    /// AgentStateService 테스트 — 기존 + 에이전틱 루프 + 글로벌 상태
    /// </summary>
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

        // ── 기존 테스트 (유지) ──────────────────────────────────────────

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
            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskStarted, sessionId: "main"));

            Assert.AreEqual(1, emittedCount);
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

        // ── 에이전틱 루프 상태 테스트 (M0 추가) ─────────────────────────

        [Test]
        public void ApplyEvent_Planning_상태변경()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Planning, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Planning, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_Executing_상태변경()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Executing, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Executing, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_Reviewing_상태변경()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Reviewing, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Reviewing, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_ToolUsing_Executing매핑()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.ToolUsing, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Executing, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_ToolResult_Reviewing매핑()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.ToolResult, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Reviewing, _service.GetState("main"));
        }

        [Test]
        public void ApplyEvent_Connected_Idle매핑()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Connected, sessionId: "main"));
            Assert.AreEqual(AgentActionType.Idle, _service.GetState("main"));
        }

        // ── 글로벌 상태 조회 테스트 (M0 추가) ───────────────────────────

        [Test]
        public void IsAnyAgentBusy_초기_False()
        {
            Assert.IsFalse(_service.IsAnyAgentBusy);
        }

        [Test]
        public void IsAnyAgentBusy_작업중_True()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Thinking, sessionId: "main"));
            Assert.IsTrue(_service.IsAnyAgentBusy);
        }

        [Test]
        public void BusyAgentCount_여러에이전트()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Thinking, sessionId: "main"));
            _service.ApplyEvent(new AgentEvent(AgentActionType.Planning, sessionId: "dev"));
            _service.ApplyEvent(new AgentEvent(AgentActionType.Idle, sessionId: "planner"));

            Assert.AreEqual(2, _service.BusyAgentCount);
        }

        [Test]
        public void IsAnyAgentBusy_완료후_False()
        {
            _service.ApplyEvent(new AgentEvent(AgentActionType.Thinking, sessionId: "main"));
            Assert.IsTrue(_service.IsAnyAgentBusy);

            _service.ApplyEvent(new AgentEvent(AgentActionType.TaskCompleted, sessionId: "main"));
            Assert.IsFalse(_service.IsAnyAgentBusy);
        }
    }
}
