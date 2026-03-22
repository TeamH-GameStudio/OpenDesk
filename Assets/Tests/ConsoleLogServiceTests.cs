using System.Collections.Generic;
using NUnit.Framework;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Tests
{
    public class ConsoleLogServiceTests
    {
        private ConsoleLogService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new ConsoleLogService();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        [Test]
        public void AddLog_로그추가_조회가능()
        {
            _service.AddLog(new ConsoleLogEntry
            {
                Level      = LogLevel.Info,
                RawMessage = "테스트 메시지",
            });

            var logs = _service.GetRecentLogs(10);
            Assert.AreEqual(1, logs.Count);
            Assert.AreEqual("테스트 메시지", logs[0].RawMessage);
        }

        [Test]
        public void AddLog_스트림발행()
        {
            var received = new List<ConsoleLogEntry>();
            _service.OnLogReceived.Subscribe(e => received.Add(e));

            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Info, RawMessage = "msg" });

            Assert.AreEqual(1, received.Count);
        }

        [Test]
        public void SetFilter_Warning이상_Info제외()
        {
            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Info, RawMessage = "info" });
            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Warning, RawMessage = "warn" });
            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Error, RawMessage = "error" });

            _service.SetFilter(LogLevel.Warning);
            var logs = _service.GetRecentLogs(10);

            Assert.AreEqual(2, logs.Count);
            Assert.AreEqual("warn", logs[0].RawMessage);
            Assert.AreEqual("error", logs[1].RawMessage);
        }

        [Test]
        public void Clear_전체삭제()
        {
            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Info, RawMessage = "1" });
            _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Info, RawMessage = "2" });

            _service.Clear();
            var logs = _service.GetRecentLogs(10);

            Assert.AreEqual(0, logs.Count);
        }

        [Test]
        public void AddFromAgentEvent_한글변환()
        {
            var e = new AgentEvent(AgentActionType.TaskStarted, sessionId: "main", taskName: "데이터 수집");
            _service.AddFromAgentEvent(e);

            var logs = _service.GetRecentLogs(10);
            Assert.AreEqual(1, logs.Count);
            Assert.IsTrue(logs[0].Translated.Contains("작업 시작"));
            Assert.AreEqual("작업", logs[0].Category);
        }

        [Test]
        public void AddFromAgentEvent_Planning_계획카테고리()
        {
            var e = new AgentEvent(AgentActionType.Planning, sessionId: "dev", taskName: "분석");
            _service.AddFromAgentEvent(e);

            var logs = _service.GetRecentLogs(10);
            Assert.AreEqual("계획", logs[0].Category);
        }

        [Test]
        public void GetRecentLogs_최대개수제한()
        {
            for (int i = 0; i < 100; i++)
                _service.AddLog(new ConsoleLogEntry { Level = LogLevel.Info, RawMessage = $"msg_{i}" });

            var logs = _service.GetRecentLogs(5);
            Assert.AreEqual(5, logs.Count);
            Assert.AreEqual("msg_95", logs[0].RawMessage); // 마지막 5개
        }

        [Test]
        public void DisplayMessage_번역있으면_번역사용()
        {
            var entry = new ConsoleLogEntry
            {
                RawMessage = "Task started",
                Translated = "작업 시작됨",
            };

            Assert.AreEqual("작업 시작됨", entry.DisplayMessage);
        }

        [Test]
        public void DisplayMessage_번역없으면_원문사용()
        {
            var entry = new ConsoleLogEntry
            {
                RawMessage = "Task started",
                Translated = "",
            };

            Assert.AreEqual("Task started", entry.DisplayMessage);
        }
    }
}
