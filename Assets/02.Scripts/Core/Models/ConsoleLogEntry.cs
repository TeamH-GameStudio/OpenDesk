using System;

namespace OpenDesk.Core.Models
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        AgentAction,    // 에이전트 행동 로그 (도구 호출 등)
    }

    /// <summary>실시간 콘솔 로그 항목</summary>
    public class ConsoleLogEntry
    {
        public DateTime Timestamp   { get; set; } = DateTime.UtcNow;
        public LogLevel Level       { get; set; } = LogLevel.Info;
        public string   SessionId   { get; set; } = "";
        public string   RawMessage  { get; set; } = "";   // 원문
        public string   Translated  { get; set; } = "";   // 한글 번역 (있으면)
        public string   Category    { get; set; } = "";   // "tool", "thinking", "plan" 등

        public string DisplayMessage => string.IsNullOrEmpty(Translated) ? RawMessage : Translated;
    }
}
