using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.SkillDiskette.Models;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// Claude 통신 Facade.
    /// 기존 ClaudeWebSocketClient를 래핑하여 디스켓/파이프라인 기능 통합.
    /// </summary>
    public interface IClaudeService
    {
        /// <summary>시스템 프롬프트 설정 (장착 디스켓 기반 합성 결과)</summary>
        void SetSystemPrompt(string systemPrompt);

        /// <summary>메시지 전송</summary>
        void SendMessage(string message);

        /// <summary>세션 재개 (대화 히스토리 복원)</summary>
        void ResumeSession(string conversationJson);

        /// <summary>대화 히스토리 초기화</summary>
        void ClearHistory();

        /// <summary>MCP 설정 전달 (외부 도구 디스켓용)</summary>
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
