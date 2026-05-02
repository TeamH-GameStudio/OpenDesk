using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.SkillDiskette.Models;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// AI 채팅 백엔드 추상화. 모델/제공자 비종속.
    ///
    /// 구현체:
    ///   - AnthropicCliChatService: Python 미들웨어 + Claude CLI subprocess (MCP 지원, 개발 편의)
    ///   - AnthropicApiChatService: Anthropic Messages API HTTP 직접 호출 (경량, 빠름)
    ///   - (예정) OpenAiChatService, GoogleGeminiChatService 등
    ///
    /// 백엔드 선택은 AgentOfficeInstaller에서 PlayerPrefs `OpenDesk_ChatBackend` 키로 토글.
    /// </summary>
    public interface IAiChatService
    {
        /// <summary>시스템 프롬프트 설정 (장착 디스켓 기반 합성 결과)</summary>
        void SetSystemPrompt(string systemPrompt);

        /// <summary>메시지 전송</summary>
        void SendMessage(string message);

        /// <summary>세션 재개 (대화 히스토리 복원)</summary>
        void ResumeSession(string conversationJson);

        /// <summary>대화 히스토리 초기화</summary>
        void ClearHistory();

        /// <summary>MCP 설정 전달 (외부 도구 디스켓용 — CLI 백엔드만 지원, API 백엔드는 NoOp)</summary>
        void SendMcpConfig(string mcpConfigJson);

        /// <summary>자연어 프롬프트로 스킬 디스켓 크래프팅</summary>
        UniTask<CraftResult> CraftDisketteAsync(string naturalLanguagePrompt, CancellationToken ct);

        /// <summary>연결 상태 (API 백엔드는 키 유효성으로 판정)</summary>
        bool IsConnected { get; }

        // ── 이벤트 (스트리밍 응답 중계) ──

        event Action<string> OnDelta;
        event Action<string, float> OnFinal;
        event Action<string> OnError;
        event Action<string> OnStatus;
        event Action<bool, string> OnConnectionChanged;
        event Action OnCleared;
    }
}
