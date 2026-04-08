using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.SkillDiskette.Models;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// Claude 통신 Facade.
    /// ClaudeWebSocketClient를 래핑하여 디스켓/파이프라인 기능 통합.
    /// 새 프로토콜에서는 미들웨어가 system prompt/세션을 관리하므로
    /// SetSystemPrompt/ResumeSession은 no-op.
    /// </summary>
    public interface IClaudeService
    {
        /// <summary>시스템 프롬프트 설정 — 새 프로토콜에서는 미들웨어가 관리 (no-op)</summary>
        [Obsolete("새 프로토콜에서는 미들웨어가 system prompt를 관리합니다")]
        void SetSystemPrompt(string systemPrompt);

        /// <summary>메시지 전송 (기본 에이전트에게)</summary>
        void SendMessage(string message);

        /// <summary>세션 재개 — 새 프로토콜에서는 session_switch 사용 (no-op)</summary>
        [Obsolete("새 프로토콜에서는 session_switch를 사용합니다")]
        void ResumeSession(string conversationJson);

        /// <summary>대화 히스토리 초기화</summary>
        void ClearHistory();

        /// <summary>MCP 설정 전달 — 새 프로토콜에서는 미들웨어가 관리 (no-op)</summary>
        [Obsolete("새 프로토콜에서는 미들웨어가 도구를 관리합니다")]
        void SendMcpConfig(string mcpConfigJson);

        /// <summary>자연어 프롬프트로 스킬 디스켓 크래프팅</summary>
        UniTask<CraftResult> CraftDisketteAsync(string naturalLanguagePrompt, CancellationToken ct);

        /// <summary>연결 상태</summary>
        bool IsConnected { get; }

        // ── 이벤트 (ClaudeWebSocketClient 중계) ──

        event Action<string> OnDelta;
        event Action<string, float> OnFinal;
        event Action<string> OnError;
        event Action<string> OnStatus;
        event Action<bool, string> OnConnectionChanged;
        event Action OnCleared;
    }
}
