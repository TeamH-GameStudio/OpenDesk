using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 원클릭 보안 감사 + 자가 복구
    /// - openclaw security audit --deep 명령 래핑
    /// - 4개 도메인 점검: Gateway, Filesystem, Execution, Skills
    /// - --fix 옵션으로 자동 수정
    /// </summary>
    public interface ISecurityAuditService
    {
        /// <summary>보안 감사 실행 (deep=true: WebSocket 프로브 + 스킬 정적 분석)</summary>
        UniTask<AuditReport> RunAuditAsync(bool deep = false, CancellationToken ct = default);

        /// <summary>자동 수정 실행 (openclaw security audit --fix)</summary>
        UniTask<AuditReport> RunAutoFixAsync(CancellationToken ct = default);

        /// <summary>마지막 감사 리포트</summary>
        AuditReport LastReport { get; }

        /// <summary>감사 진행률 (0.0 ~ 1.0)</summary>
        ReadOnlyReactiveProperty<float> Progress { get; }

        /// <summary>감사 상태 텍스트</summary>
        ReadOnlyReactiveProperty<string> StatusText { get; }
    }
}
