using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;

namespace OpenDesk.AgentCreation.Soul
{
    /// <summary>
    /// 위저드 입력(이름·역할·말투)을 기반으로 에이전트별 Soul markdown을 생성한다.
    /// 구현체는 외부 LLM 호출 또는 로컬 템플릿 합성을 사용할 수 있다.
    /// 실패 시 예외를 던지며, 호출자가 폴백 처리하도록 한다.
    /// </summary>
    public interface ISoulGenerationService
    {
        /// <summary>
        /// Soul 본문(markdown) 생성. 결과는 5개 섹션을 포함한 검증된 텍스트여야 한다.
        /// 호출자가 SoulRepository에 저장하는 책임을 가진다.
        /// </summary>
        UniTask<string> GenerateAsync(AgentCreationData data, CancellationToken ct);
    }
}
