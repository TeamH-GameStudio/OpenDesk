using System;

namespace OpenDesk.Core.Models
{
    /// <summary>
    /// AI 제공업체 정보
    /// OpenClaw는 자체 AI 없음 — 사용자가 외부 모델을 연결해야 함
    /// </summary>
    public class ApiProvider
    {
        public string   Id          { get; set; } = "";   // "anthropic", "openai", "ollama" 등
        public string   DisplayName { get; set; } = "";   // "Anthropic (Claude)"
        public string   IconName    { get; set; } = "";   // UI 아이콘 참조명
        public bool     IsLocal     { get; set; }         // Ollama 등 로컬 모델 (무료)
        public bool     RequiresKey { get; set; } = true; // API 키 필요 여부
        public string   KeyHint     { get; set; } = "";   // "sk-ant-..." 같은 키 형식 힌트
        public string   SignupUrl   { get; set; } = "";   // API 키 발급 페이지 URL
    }

    /// <summary>저장된 API 키 상태</summary>
    public enum ApiKeyStatus
    {
        NotSet,         // 미입력
        Validating,     // 검증 중
        Valid,          // 유효
        Invalid,        // 유효하지 않음 (만료/잘못된 키)
        Error,          // 검증 중 오류
    }

    /// <summary>저장된 API 키 엔트리</summary>
    public class ApiKeyEntry
    {
        public string       ProviderId   { get; set; } = "";
        public ApiKeyStatus Status       { get; set; } = ApiKeyStatus.NotSet;
        public DateTime     LastVerified { get; set; }
        public string       ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// 라우팅 모드 — 비용 최적화 수준
    /// ClawRouter가 요청 난이도에 따라 모델을 자동 분배
    /// </summary>
    public enum RoutingMode
    {
        /// <summary>
        /// 무료/최저가 — Ollama 로컬 모델 또는 DeepSeek V3.2
        /// API 키 없이도 Ollama 설정 시 사용 가능
        /// $0 (로컬) ~ $1.00/1M토큰
        /// </summary>
        Free,

        /// <summary>
        /// 에코 모드 — 단순 작업은 저가 모델, 복잡한 작업만 중급 모델
        /// DeepSeek V3.2, Gemini Flash-Lite, Claude Haiku
        /// $0.40 ~ $1.00/1M토큰
        /// </summary>
        Eco,

        /// <summary>
        /// 자동 모드 — 난이도에 따라 저가~고가 모델 자동 분배
        /// Claude Sonnet, GPT-4o-mini 등
        /// $0.60 ~ $15.00/1M토큰
        /// </summary>
        Auto,

        /// <summary>
        /// 프리미엄 모드 — 모든 요청에 최고 성능 모델 사용
        /// Claude Opus, GPT-5.2
        /// $10.00 ~ $75.00/1M토큰
        /// </summary>
        Premium,
    }

    /// <summary>라우팅 설정 + 비용 추정</summary>
    public class RoutingConfig
    {
        public RoutingMode Mode               { get; set; } = RoutingMode.Free;
        public string      PrimaryModel       { get; set; } = "";    // 기본 모델 ID
        public bool        UseLocalFallback   { get; set; } = true;  // API 실패 시 로컬 모델 사용
        public decimal     EstimatedMonthlyCost { get; set; }        // 예상 월 비용 (USD)
    }
}
