using System;
using OpenDesk.AgentCreation.Persistence;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// AgentCreationScene 과 AgentOfficeScene 사이의 양방향 핸드오프 채널.
    /// CoreInstaller 싱글톤으로 등록되어 두 씬에서 동일 인스턴스를 resolve.
    ///
    /// 흐름:
    ///   - 위저드 완료 → RaiseAgentSaved(record, path) → Office 가 스폰
    ///   - 스폰 + 와드로브 적용 + 최소 3초 보장 후 → RaiseOfficeSetupCompleted → 위저드 씬 unload
    ///
    /// 주의: 핸들러는 반드시 try/finally 또는 명시적 detach 로 정리해야 한다.
    /// 싱글톤이라 씬 unload 만으로는 자동 청소되지 않는다.
    /// </summary>
    public interface IAgentCreationBridge
    {
        /// <summary>위저드 → 오피스: 새 드래프트 저장 완료.</summary>
        event Action<AgentDraftRecord, string> AgentSaved;

        /// <summary>오피스 → 위저드: 스폰/세팅 완료, 이제 unload 해도 안전.</summary>
        event Action OfficeSetupCompleted;

        void RaiseAgentSaved(AgentDraftRecord record, string savedPath);
        void RaiseOfficeSetupCompleted();
    }
}
