using System.Collections.Generic;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 실시간 콘솔 로그 수집/표시
    /// 터미널 없이 인앱에서 에이전트 작업 내역 확인
    /// </summary>
    public interface IConsoleLogService
    {
        /// <summary>새 로그 수신 스트림</summary>
        Observable<ConsoleLogEntry> OnLogReceived { get; }

        /// <summary>최근 로그 N개 조회</summary>
        IReadOnlyList<ConsoleLogEntry> GetRecentLogs(int count = 50);

        /// <summary>로그 레벨 필터 설정</summary>
        void SetFilter(LogLevel minLevel);

        /// <summary>로그 전체 삭제</summary>
        void Clear();

        /// <summary>외부에서 로그 추가 (Bridge 이벤트 → 로그 변환)</summary>
        void AddLog(ConsoleLogEntry entry);

        /// <summary>AgentEvent를 로그로 변환하여 추가</summary>
        void AddFromAgentEvent(Core.Models.AgentEvent e);
    }
}
