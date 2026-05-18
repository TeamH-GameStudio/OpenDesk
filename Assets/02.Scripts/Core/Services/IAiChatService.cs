using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Claude.Models;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Models.Skills;
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

        /// <summary>모델 ID 동적 전환. 드롭다운 변경 시 호출. 빈 문자열이면 미들웨어 기본값 사용.</summary>
        void SetModel(string model);

        /// <summary>메시지 전송</summary>
        void SendMessage(string message);

        /// <summary>세션 재개 (대화 히스토리 복원)</summary>
        void ResumeSession(string conversationJson);

        /// <summary>대화 히스토리 초기화</summary>
        void ClearHistory();

        /// <summary>MCP 설정 전달 (외부 도구 디스켓용 — CLI 백엔드만 지원, API 백엔드는 NoOp)</summary>
        void SendMcpConfig(string mcpConfigJson);

        /// <summary>
        /// 장착 스킬 인덱스(이름+설명) 전달. 본문은 LLM 이 미들웨어 내장 도구
        /// read_skill_body 로 지연 로드한다. Claude Code 의 SKILL.md 패턴과 동일.
        /// </summary>
        void SendSkillLoadout(SkillLoadoutPayload payload);

        /// <summary>현재 진행 중인 응답을 중단. CLI: 미들웨어가 subprocess kill. API: 활성 CTS 취소.</summary>
        void Abort();

        /// <summary>ask_user 도구에 대한 사용자 응답 회신. selected 는 null 가능.</summary>
        void SendToolUserResponse(string toolUseId, string response, string[] selected);

        /// <summary>route_capability 의 capability_pick 카드 — "다음부터 자동" 선호 저장 여부 포함.</summary>
        void SendToolUserResponse(string toolUseId, string response, string[] selected, bool remember);

        /// <summary>설치된 플러그인 목록 push — 미들웨어의 route_capability 가 조회.</summary>
        void SendPluginRegistry(string agentId, PluginRegistryEntry[] entries);

        /// <summary>백그라운드 작업 제어. action: "stop" | "update".</summary>
        void SendTaskControl(string action, string taskId);

        /// <summary>자연어 프롬프트로 스킬 디스켓 크래프팅</summary>
        UniTask<CraftResult> CraftDisketteAsync(string naturalLanguagePrompt, CancellationToken ct);

        /// <summary>연결 상태 (API 백엔드는 키 유효성으로 판정)</summary>
        bool IsConnected { get; }

        /// <summary>API Key 또는 OAuth 토큰이 있어 인증 가능한 상태인지. UI 가드용.</summary>
        bool IsAuthenticated { get; }

        // ── 이벤트 (스트리밍 응답 중계) ──

        event Action<string> OnDelta;
        event Action<string, float> OnFinal;
        event Action<string> OnError;
        event Action<string> OnStatus;
        event Action<bool, string> OnConnectionChanged;
        event Action OnCleared;

        /// <summary>인터랙티브 ask_user 요청 — ChatPanelView 가 인라인 카드 렌더.</summary>
        event Action<ToolUserAskMessage> OnToolUserAsk;

        /// <summary>서브에이전트 라이프사이클 이벤트.</summary>
        event Action<SubAgentSpawnedMessage> OnSubAgentSpawned;
        event Action<SubAgentCompletedMessage> OnSubAgentCompleted;
        event Action<SubAgentFailedMessage> OnSubAgentFailed;

        /// <summary>백그라운드 작업 / Cron 상태 변화.</summary>
        event Action<TaskStateMessage> OnTaskState;
        event Action<CronStateMessage> OnCronState;
    }
}
