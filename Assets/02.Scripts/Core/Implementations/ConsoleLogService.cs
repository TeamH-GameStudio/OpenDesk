using System;
using System.Collections.Generic;
using System.Linq;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 실시간 콘솔 로그 수집
    /// - AgentEvent → 한글 로그 자동 변환
    /// - 최대 500개 보관 (오래된 것부터 삭제)
    /// - 레벨 필터링 지원
    /// </summary>
    public class ConsoleLogService : IConsoleLogService, IDisposable
    {
        private readonly List<ConsoleLogEntry>    _logs      = new();
        private readonly Subject<ConsoleLogEntry> _logStream = new();
        private LogLevel _minLevel = LogLevel.Info;
        private const int MaxLogs  = 500;

        public Observable<ConsoleLogEntry> OnLogReceived => _logStream;

        public IReadOnlyList<ConsoleLogEntry> GetRecentLogs(int count = 50)
        {
            lock (_logs)
            {
                return _logs
                    .Where(l => l.Level >= _minLevel)
                    .TakeLast(count)
                    .ToList();
            }
        }

        public void SetFilter(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public void Clear()
        {
            lock (_logs) { _logs.Clear(); }
        }

        public void AddLog(ConsoleLogEntry entry)
        {
            lock (_logs)
            {
                _logs.Add(entry);
                if (_logs.Count > MaxLogs)
                    _logs.RemoveRange(0, _logs.Count - MaxLogs);
            }

            if (entry.Level >= _minLevel)
                _logStream.OnNext(entry);
        }

        public void AddFromAgentEvent(AgentEvent e)
        {
            var (level, category, message) = TranslateEvent(e);

            AddLog(new ConsoleLogEntry
            {
                Timestamp  = e.Timestamp,
                Level      = level,
                SessionId  = e.SessionId,
                RawMessage = e.ToString(),
                Translated = message,
                Category   = category,
            });
        }

        // ── 이벤트 → 한글 로그 변환 ────────────────────────────────────

        private static (LogLevel level, string category, string message) TranslateEvent(AgentEvent e)
        {
            var session = string.IsNullOrEmpty(e.SessionId) ? "메인" : e.SessionId;
            var task    = string.IsNullOrEmpty(e.TaskName) ? "" : $" [{e.TaskName}]";

            return e.ActionType switch
            {
                AgentActionType.Connected       => (LogLevel.Info,        "연결",   $"[OK] {session} 에이전트 연결됨"),
                AgentActionType.Disconnected    => (LogLevel.Warning,     "연결",   $"[X] {session} 에이전트 연결 끊김"),
                AgentActionType.TaskStarted     => (LogLevel.AgentAction, "작업",   $"▶ {session} 작업 시작{task}"),
                AgentActionType.TaskCompleted   => (LogLevel.Info,        "작업",   $"[OK] {session} 작업 완료{task}"),
                AgentActionType.TaskFailed      => (LogLevel.Error,       "작업",   $"[X] {session} 작업 실패{task}"),
                AgentActionType.Thinking        => (LogLevel.AgentAction, "사고",   $"💭 {session} 사고 중..."),
                AgentActionType.Planning        => (LogLevel.AgentAction, "계획",   $" {session} 계획 수립 중{task}"),
                AgentActionType.Executing       => (LogLevel.AgentAction, "실행",   $"⚙ {session} 실행 중{task}"),
                AgentActionType.Reviewing       => (LogLevel.AgentAction, "검토",   $"🔍 {session} 결과 검토 중"),
                AgentActionType.ToolUsing       => (LogLevel.AgentAction, "도구",   $"🔧 {session} 도구 호출{task}"),
                AgentActionType.ToolResult      => (LogLevel.Info,        "도구",   $"📎 {session} 도구 결과 수신"),
                AgentActionType.SubAgentSpawned => (LogLevel.Info,        "위임",   $"👥 {session} → 서브에이전트 생성 ({e.SubAgentId})"),
                AgentActionType.SubAgentCompleted => (LogLevel.Info,      "위임",   $"[OK] 서브에이전트 {e.SubAgentId} 완료"),
                AgentActionType.SubAgentFailed  => (LogLevel.Error,       "위임",   $"[X] 서브에이전트 {e.SubAgentId} 실패"),
                _                               => (LogLevel.Info,        "기타",   $"{session}: {e.ActionType}"),
            };
        }

        public void Dispose()
        {
            _logStream.Dispose();
        }
    }
}
