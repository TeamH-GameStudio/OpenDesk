using System;
using OpenDesk.Core.Models;

namespace OpenDesk.Core.Services
{
    [Obsolete("OpenClaw legacy. Replaced by Claude CLI/Anthropic API path (IClaudeService). Will be removed once Anthropic-direct migration is fully verified.", error: false)]
    public interface IEventParserService
    {
        // Raw JSON → AgentEvent 변환
        // 파싱 실패 시 null 반환
        AgentEvent? Parse(string rawJson);

        // 특정 JSON 키 → ActionType 매핑 규칙 등록
        void RegisterRule(string eventTypeKey, AgentActionType actionType);
    }
}
