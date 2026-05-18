using System;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Models.Skills;

namespace OpenDesk.Claude.Models
{
    // ── Unity → 서버 요청 ─────────────────────────────────────

    [Serializable]
    public class ChatRequest
    {
        public string type = "chat";
        public string message;
    }

    [Serializable]
    public class ClearRequest
    {
        public string type = "clear";
    }

    [Serializable]
    public class ConfigRequest
    {
        public string type = "config";
        public string systemPrompt;
        public string model;
    }

    [Serializable]
    public class PingRequest
    {
        public string type = "ping";
    }

    [Serializable]
    public class ResumeRequest
    {
        public string type = "resume";
        public string conversation; // ConversationFile JSON
    }

    [Serializable]
    public class CancelRequest
    {
        public string type = "cancel";
    }

    [Serializable]
    public class SetProviderRequest
    {
        public string type = "set_provider";
        public string provider;     // "anthropic_cli" / "anthropic_api" / 향후 "openai" / "gemini"
    }

    [Serializable]
    public class SetMcpConfigRequest
    {
        public string type = "set_mcp_config";
        public McpConfigPayload payload;
    }

    [Serializable]
    public class SetSkillLoadoutRequest
    {
        public string type = "set_skill_loadout";
        public SkillLoadoutPayload payload;
    }

    [Serializable]
    public class AuthStartRequest
    {
        public string type = "auth_start";
    }

    // ── 라이선스 / 크레딧 (hybrid routing) ─────────────────────

    [Serializable]
    public class SetAuthRequest
    {
        public string type = "set_auth";
        public string jwt;
    }

    [Serializable]
    public class SetComplexityHintRequest
    {
        public string type = "config";
        public string complexityHint;
    }

    [Serializable]
    public class LicenseActivateRequest
    {
        public string type = "license.activate";
        public string licenseKey;
        public string fingerprint;
        public string deviceName;
    }

    [Serializable]
    public class LicenseActivatedMessage
    {
        public string type;
        public string jwt;
        public string refreshToken;
        public string userId;
        public string planTier;
        public long balance;
    }

    [Serializable]
    public class LicenseErrorMessage
    {
        public string type;
        public string code;
        public string message;
    }

    [Serializable]
    public class AuthStatusMessage
    {
        public string type;
        public bool authenticated;
    }

    [Serializable]
    public class CreditRoutingMessage
    {
        public string type;
        public string taskId;
        public string model;
        public string tier;
        public long estimatedCredits;
        public string reasoning;
        public bool escalationAllowed;
        public int expectedToolCalls;
    }

    [Serializable]
    public class CreditBalanceMessage
    {
        public string type;
        public long balance;
        public long held;
    }

    [Serializable]
    public class CreditSettledMessage
    {
        public string type;
        public string taskId;
        public string model;
        public long actualCredits;
        public int inputTokens;
        public int outputTokens;
        public long balance;
    }

    [Serializable]
    public class CreditInsufficientMessage
    {
        public string type;
        public string code;
        public long required;
        public long balance;
    }

    [Serializable]
    public class AuthCancelRequest
    {
        public string type = "auth_cancel";
    }

    // ── 서버 → Unity 응답 (수신 파싱용) ────────────────────────

    [Serializable]
    public class ServerMessage
    {
        public string type;
    }

    [Serializable]
    public class ConnectedMessage
    {
        public string type;
        public string model;
        public string provider;
    }

    [Serializable]
    public class ProviderChangedMessage
    {
        public string type;
        public string provider;
        public bool available;
        public string info;
    }

    [Serializable]
    public class DeltaMessage
    {
        public string type;
        public string text;
    }

    [Serializable]
    public class FinalMessage
    {
        public string type;
        public string text;
        public float cost;
    }

    [Serializable]
    public class ErrorMessage
    {
        public string type;
        public string message;
        public string code;
    }

    [Serializable]
    public class StatusMessage
    {
        public string type;
        public string text;
    }

    /// <summary>
    /// 미들웨어가 OAuth 진행 상태를 푸시하는 이벤트.
    /// state: "url" | "code" | "status" | "success" | "failed"
    /// </summary>
    [Serializable]
    public class AuthEventMessage
    {
        public string type;
        public string state;
        public string message;
        public string url;
        public string code;
    }

    // ── 발화/입모양 동기화용 (multi-agent runner / PROTOCOL.md) ─────────

    /// <summary>
    /// 캐릭터 입모양 + 타이핑 효과용 lightweight 토큰 청크.
    /// 채팅 UI 누적용 <see cref="DeltaMessage"/> 와 별개 채널.
    /// </summary>
    [Serializable]
    public class TextDeltaMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public float  timestamp;
        public string text;
    }

    /// <summary>발화 시작 신호 — 첫 text_delta 직전에 1회.</summary>
    [Serializable]
    public class TalkingStartMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public float  timestamp;
    }

    /// <summary>
    /// 발화 종료 신호. reason: "complete" | "error" | "interrupted"
    /// talking_start 가 emit 된 경우에만 짝으로 emit.
    /// </summary>
    [Serializable]
    public class TalkingStopMessage
    {
        public string type;
        public string agent_id;
        public string role;
        public string session_id;
        public float  timestamp;
        public string reason;
    }

    // ── 인터랙티브 도구 (ask_user) ──────────────────────────────

    /// <summary>AskUserQuestion 도구의 옵션 한 줄.</summary>
    [Serializable]
    public class AskOption
    {
        public string label;
        public string description;
    }

    /// <summary>route_capability 가 옵션과 함께 실어 보내는 호환 플러그인 메타데이터.</summary>
    [Serializable]
    public class CompatiblePluginInfo
    {
        public string id;
        public string display_name;
        public string vendor;
        public string author;
    }

    /// <summary>미들웨어 → Unity: ask_user / route_capability 도구가 사용자 응답을 요청.
    ///
    /// payload_kind:
    ///   - "ask_user"        : 기본 인터랙티브 질문 (라디오/체크/자유입력)
    ///   - "capability_pick" : route_capability 의 인라인 도구 선택 카드.
    ///                         compatible_plugins + capability 가 채워져 있다.
    /// 응답으로 ToolUserResponseRequest.remember 를 같이 보내면 선호로 저장된다.
    /// </summary>
    [Serializable]
    public class ToolUserAskMessage
    {
        public string type;
        public string agent_id;
        public string tool_use_id;
        public string question;
        public string header;
        public bool   multi_select;
        public AskOption[] options;

        // ── route_capability 확장 필드 (구버전 미들웨어 호환 위해 모두 기본값 안전) ──
        public string payload_kind;                 // "ask_user" | "capability_pick"
        public string capability;                   // e.g. "calendar.create_event"
        public CompatiblePluginInfo[] compatible_plugins;
    }

    /// <summary>Unity → 미들웨어: 사용자가 ChatPanel 카드로 응답한 결과.</summary>
    [Serializable]
    public class ToolUserResponseRequest
    {
        public string type = "tool_user_response";
        public string tool_use_id;
        public string response;     // 자유 입력 (옵션이 0개거나 자유 입력만 했을 때)
        public string[] selected;   // 선택된 옵션 라벨 배열
        public bool remember;       // capability_pick 카드에서 "다음부터 자동" 선택 시 true
    }

    // ── 플러그인 레지스트리 — route_capability 가 조회 ──────────────

    [Serializable]
    public class PluginRegistryEntry
    {
        public string id;
        public string display_name;
        public string vendor;
        public string author;
        public string[] capabilities;
    }

    /// <summary>Unity → 미들웨어: 설치된 플러그인 카탈로그를 push.
    /// 미들웨어는 ``set_plugin_registry`` op 로 받아 route_capability 가 조회한다.</summary>
    [Serializable]
    public class SetPluginRegistryRequest
    {
        public string type = "set_plugin_registry";
        public string agent_id;
        public PluginRegistryEntry[] payload;
    }

    // ── 서브에이전트 라이프사이클 ────────────────────────────────

    [Serializable]
    public class SubAgentSpawnedMessage
    {
        public string type;
        public string agent_id;       // 부모
        public string sub_agent_id;
        public string task_name;
        public string subagent_type;
        public float  timestamp;
    }

    [Serializable]
    public class SubAgentCompletedMessage
    {
        public string type;
        public string agent_id;
        public string sub_agent_id;
        public string task_name;
        public float  timestamp;
    }

    [Serializable]
    public class SubAgentFailedMessage
    {
        public string type;
        public string agent_id;
        public string sub_agent_id;
        public string error;
        public float  timestamp;
    }

    // ── 백그라운드 작업 ──────────────────────────────────────────

    /// <summary>미들웨어 → Unity: 작업 상태 변화. status: pending | running | completed | failed | stopped.</summary>
    [Serializable]
    public class TaskStateMessage
    {
        public string type;
        public string agent_id;
        public string task_id;
        public string status;
        public string description;
        public int    exit_code;       // 미완료면 0 (JSON null → JsonUtility 기본값)
        public float  timestamp;
    }

    /// <summary>Unity → 미들웨어: 작업 제어. action: stop | update.</summary>
    [Serializable]
    public class TaskControlRequest
    {
        public string type = "task_control";
        public string action;
        public string task_id;
    }

    // ── Cron ─────────────────────────────────────────────────────

    [Serializable]
    public class CronStateMessage
    {
        public string type;
        public string cron_id;
        public string name;
        public string schedule;
        public bool   enabled;
        public float  last_run;
        public float  next_run;
    }

    // ── Telemetry (Middleware/PROTOCOL.md v1 — 2026-05-17 freeze) ────────
    //
    // 미들웨어의 hook chain (LatencyHook / CacheStatHook / TelemetryEmitterHook 등) 이
    // 발행하는 추가 이벤트. 기존 chat protocol 변경 없음 — 클라이언트는 unknown type 으로
    // 무시해도 안전. event: "first_token" | "request_complete" | "error" | "retry".
    //
    // JsonUtility 의 nested null fragility 회피 — 모든 nested 객체는 항상 emit
    // (서버가 빈 {} 로 채워보냄). null 체크 불필요.

    [Serializable]
    public class TelemetryLatency
    {
        public int   ttft_ms;
        public int   total_ms;
        public int[] tool_rounds_ms;
    }

    [Serializable]
    public class TelemetryTokens
    {
        public int input;
        public int output;
        public int cache_creation_input;
        public int cache_read_input;
    }

    [Serializable]
    public class TelemetryCache
    {
        public bool  available;
        public float hit_ratio;
        public int   creation_tokens;
        public int   read_tokens;
    }

    [Serializable]
    public class TelemetryReliability
    {
        public int    retry_count;
        public int    rate_limit_hits;
        public int    max_tool_rounds;
        public int    tool_rounds_used;
        public string stop_reason;
    }

    [Serializable]
    public class TelemetryError
    {
        public string message;
        public string code;
        public bool   recoverable;
    }

    [Serializable]
    public class TelemetryEvent
    {
        public string type;                    // 항상 "telemetry"
        public string @event;                  // first_token / request_complete / error / retry
        public string request_id;
        public string agent_id;
        public string session_id;
        public string provider;                // anthropic_api / anthropic_cli / ...
        public string model;
        public float  timestamp;

        public TelemetryLatency     latency;
        public TelemetryTokens      tokens;
        public TelemetryCache       cache;
        public TelemetryReliability reliability;

        public float  cost_estimate_usd;
        public string telemetry_completeness;  // full | partial
        public bool   has_error;
        public TelemetryError error;
    }
}
