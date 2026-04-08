using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude.Models;
using OpenDesk.SkillDiskette.Models;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// Claude 미들웨어 통신 Facade.
    /// 멀티 에이전트 프로토콜 -- agent_id 기반 라우팅.
    /// </summary>
    public interface IClaudeService
    {
        /// <summary>채팅 메시지 전송</summary>
        void SendMessage(string agentId, string message);

        /// <summary>대화 초기화 (새 세션)</summary>
        void ClearHistory(string agentId);

        /// <summary>전체 에이전트 상태 조회</summary>
        void RequestStatus();

        /// <summary>세션 목록 조회</summary>
        void RequestSessionList(string agentId);

        /// <summary>새 세션 생성</summary>
        void CreateSession(string agentId);

        /// <summary>세션 전환</summary>
        void SwitchSession(string agentId, string sessionId);

        /// <summary>세션 삭제</summary>
        void DeleteSession(string agentId, string sessionId);

        /// <summary>자연어 프롬프트로 스킬 디스켓 크래프팅</summary>
        UniTask<CraftResult> CraftDisketteAsync(string agentId, string naturalLanguagePrompt, CancellationToken ct);

        /// <summary>연결 상태</summary>
        bool IsConnected { get; }

        // ── 이벤트 (멀티 에이전트) ──

        /// <summary>에이전트 상태 변화 (agentId, state, tool)</summary>
        event Action<string, string, string> OnAgentState;

        /// <summary>AI 사고 과정 (agentId, thinking)</summary>
        event Action<string, string> OnAgentThinking;

        /// <summary>응답 텍스트 delta (agentId, text)</summary>
        event Action<string, string> OnAgentDelta;

        /// <summary>최종 응답 (agentId, message)</summary>
        event Action<string, string> OnAgentMessage;

        /// <summary>캐릭터 액션 (agentId, action)</summary>
        event Action<string, string> OnAgentAction;

        /// <summary>에러 (agentId, error, message)</summary>
        event Action<string, string, string> OnAgentError;

        /// <summary>세션 목록 (agentId, currentSessionId, sessions)</summary>
        event Action<string, string, SessionInfo[]> OnSessionList;

        /// <summary>세션 전환 완료 (agentId, sessionId, chatHistory)</summary>
        event Action<string, string, ChatHistoryEntry[]> OnSessionSwitched;

        /// <summary>WebSocket 연결 변경 (connected)</summary>
        event Action<bool> OnConnectionChanged;
    }
}
