using NUnit.Framework;
using OpenDesk.Core.Implementations;
using OpenDesk.Core.Models;

namespace OpenDesk.Core.Tests
{
    /// <summary>
    /// Unity Test Runner (EditMode)에서 실행
    /// </summary>
    public class EventParserServiceTests
    {
        private EventParserService _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new EventParserService();
        }

        [Test]
        public void Parse_TaskStarted_반환()
        {
            var json = "{\"type\":\"task_started\",\"session_id\":\"main\",\"task_name\":\"웹 크롤링\"}";
            var result = _parser.Parse(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(AgentActionType.TaskStarted, result.Value.ActionType);
            Assert.AreEqual("main", result.Value.SessionId);
            Assert.AreEqual("웹 크롤링", result.Value.TaskName);
        }

        [Test]
        public void Parse_TaskCompleted_반환()
        {
            var json = "{\"type\":\"task_completed\",\"session_id\":\"dev\"}";
            var result = _parser.Parse(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(AgentActionType.TaskCompleted, result.Value.ActionType);
        }

        [Test]
        public void Parse_SubAgentSpawned_Id포함()
        {
            var json = "{\"type\":\"subagent_spawned\",\"session_id\":\"main\",\"subagent_id\":\"sub_001\",\"task_name\":\"데이터 분석\"}";
            var result = _parser.Parse(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(AgentActionType.SubAgentSpawned, result.Value.ActionType);
            Assert.AreEqual("sub_001", result.Value.SubAgentId);
        }

        [Test]
        public void Parse_빈문자열_Null반환()
        {
            Assert.IsNull(_parser.Parse(""));
            Assert.IsNull(_parser.Parse("   "));
            Assert.IsNull(_parser.Parse(null));
        }

        [Test]
        public void Parse_잘못된JSON_Null반환()
        {
            Assert.IsNull(_parser.Parse("not_json"));
            Assert.IsNull(_parser.Parse("{invalid}"));
        }

        [Test]
        public void Parse_알수없는타입_Idle반환()
        {
            var json = "{\"type\":\"unknown_event\",\"session_id\":\"main\"}";
            var result = _parser.Parse(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(AgentActionType.Idle, result.Value.ActionType);
        }

        [Test]
        public void RegisterRule_커스텀타입_등록후파싱()
        {
            _parser.RegisterRule("my_custom_event", AgentActionType.Thinking);
            var json = "{\"type\":\"my_custom_event\",\"session_id\":\"main\"}";
            var result = _parser.Parse(json);

            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(AgentActionType.Thinking, result.Value.ActionType);
        }
    }
}
