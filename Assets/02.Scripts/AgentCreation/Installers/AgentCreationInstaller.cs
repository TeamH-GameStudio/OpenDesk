using OpenDesk.AgentCreation.Bootstrap;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.AgentCreation.Soul;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.AgentCreation.Installers
{
    /// <summary>
    /// AgentCreation 씬 전용 LifetimeScope.
    /// Core 서비스는 부모 Scope(CoreInstaller)에서 주입받음.
    /// </summary>
    public class AgentCreationInstaller : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            Debug.Log("[VContainer] AgentCreationInstaller.Configure() 시작");

            // Haiku 기반 Soul 생성 서비스 — 위저드 Confirm 단계에서 1회 호출.
            // IApiKeyVaultService는 부모 CoreInstaller에서 등록됨.
            builder.Register<HaikuSoulGenerationService>(Lifetime.Scoped)
                   .As<ISoulGenerationService>();

            // 새 위저드 씬용 컴포넌트 — JSON 저장 → Bridge 발행 + Single 모드면 오피스 씬 전환,
            // 풀스크린 로딩 오버레이, Additive 시 카메라/리스너 비활성.
            // RegisterComponentInHierarchy 는 씬에 컴포넌트가 없으면 throw 한다 —
            // 누락 시 전체 Build() 가 실패해 모든 의존성 주입이 끊긴다.
            // 누락 가능성이 있는 컴포넌트는 옵션 패턴 또는 부착 후 추가하는 식으로 다뤄야 한다.
            builder.RegisterComponentInHierarchy<AgentDraftSaveTrigger>();
            builder.RegisterComponentInHierarchy<AgentCreationCompletionRelay>();
            builder.RegisterComponentInHierarchy<AgentCreationSceneBootstrap>();
            // AgentCreationOverlayView 는 인스펙터에 아직 부착되지 않았을 수 있으므로 등록 제외 —
            // 부착 후에 본 라인 다시 활성화하거나, 옵션 패턴(RegisterComponentInHierarchyOrNull)이
            // 도입되면 그때 전환.

            Debug.Log("[VContainer] AgentCreationInstaller.Configure() 완료");
        }
    }
}
