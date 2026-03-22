using NUnit.Framework;
using OpenDesk.Onboarding.Implementations;

namespace OpenDesk.Onboarding.Tests
{
    public class AgentConfigParserTests
    {
        private AgentConfigParser _parser;

        [SetUp]
        public void SetUp() => _parser = new AgentConfigParser();

        [Test]
        public void Parse_정상YAML_에이전트목록반환()
        {
            var yaml = @"
agents:
  main:
    name: ""팀장""
    model: claude-sonnet-4-6
  dev:
    name: ""개발자""
    model: claude-sonnet-4-6
";
            var result = _parser.ParseFromString(yaml);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("main",  result[0].SessionId);
            Assert.AreEqual("팀장",  result[0].Name);
            Assert.AreEqual("dev",   result[1].SessionId);
            Assert.AreEqual("개발자", result[1].Name);
        }

        [Test]
        public void Parse_빈문자열_빈목록반환()
        {
            var result = _parser.ParseFromString("");
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_Null_빈목록반환()
        {
            var result = _parser.ParseFromString(null);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_AgentsSection없음_빈목록반환()
        {
            var yaml = @"
gateway:
  port: 18789
model: claude-sonnet-4-6
";
            var result = _parser.ParseFromString(yaml);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_역할매핑_올바르게()
        {
            var yaml = @"
agents:
  dev:
    name: ""개발자""
  planning:
    name: ""기획자""
  life:
    name: ""라이프""
";
            var result = _parser.ParseFromString(yaml);

            Assert.AreEqual("dev",     result[0].Role);
            Assert.AreEqual("planner", result[1].Role);
            Assert.AreEqual("life",    result[2].Role);
        }

        [Test]
        public void Parse_ApiKey없음_HasApiKeyFalse()
        {
            var yaml = @"
agents:
  main:
    name: ""팀장""
    model: claude-sonnet-4-6
";
            var result = _parser.ParseFromString(yaml);
            Assert.IsFalse(result[0].HasApiKey);
        }

        [Test]
        public void Parse_ApiKey있음_HasApiKeyTrue()
        {
            var yaml = @"
agents:
  main:
    name: ""팀장""
    api_key: ""sk-ant-abc123""
";
            var result = _parser.ParseFromString(yaml);
            Assert.IsTrue(result[0].HasApiKey);
        }

        [Test]
        public void Parse_ApiKey환경변수형식_HasApiKeyFalse()
        {
            var yaml = @"
agents:
  main:
    name: ""팀장""
    api_key: ${ANTHROPIC_API_KEY}
";
            var result = _parser.ParseFromString(yaml);
            Assert.IsFalse(result[0].HasApiKey);
        }
    }
}
