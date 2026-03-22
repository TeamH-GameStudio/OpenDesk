using System.Collections.Generic;

namespace OpenDesk.Core.Models
{
    public enum AuditSeverity
    {
        Pass,       // 통과
        Info,       // 참고
        Warn,       // 경고
        Critical,   // 치명적
    }

    public enum AuditDomain
    {
        GatewayExposure,      // 게이트웨이 인증/포트 노출
        FilesystemSecrets,    // 파일 권한/토큰 평문 노출
        ExecutionShell,       // 셸 명령 정책/승인 우회
        SkillsSupplyChain,   // 스킬 코드 안전성
    }

    /// <summary>보안 감사 개별 항목</summary>
    public class AuditItem
    {
        public AuditDomain   Domain      { get; set; }
        public AuditSeverity Severity    { get; set; } = AuditSeverity.Pass;
        public string        Title       { get; set; } = "";
        public string        Description { get; set; } = "";
        public bool          CanAutoFix  { get; set; }
        public bool          IsFixed     { get; set; }
    }

    /// <summary>보안 감사 전체 리포트</summary>
    public class AuditReport
    {
        public System.DateTime          Timestamp { get; set; } = System.DateTime.UtcNow;
        public bool                     IsDeepScan { get; set; }
        public List<AuditItem>          Items     { get; set; } = new();
        public int CriticalCount => Items.FindAll(i => i.Severity == AuditSeverity.Critical).Count;
        public int WarnCount     => Items.FindAll(i => i.Severity == AuditSeverity.Warn).Count;
        public int PassCount     => Items.FindAll(i => i.Severity == AuditSeverity.Pass).Count;
        public bool IsClean      => CriticalCount == 0 && WarnCount == 0;
    }
}
