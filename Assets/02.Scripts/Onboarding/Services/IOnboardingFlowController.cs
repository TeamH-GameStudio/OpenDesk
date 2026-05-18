using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 온보딩 플로우 상태 머신. View들은 이 컨트롤러로부터 StepChanged를 받아 화면을 토글한다.
    /// </summary>
    public interface IOnboardingFlowController
    {
        OnboardingFlowStep CurrentStep { get; }

        event Action<OnboardingFlowStep> StepChanged;

        /// <summary>
        /// ShellView가 자신을 등록한다 (단방향 — 순환 의존 회피).
        /// ShellView.OnEnable에서 호출.
        /// </summary>
        void AttachShellView(IOnboardingShellView shellView);

        /// <summary>현재 스텝 다음으로 진행 (Welcome→Plan→Auth→User).</summary>
        void Advance();

        /// <summary>이전 스텝으로 복귀.</summary>
        void GoBack();

        /// <summary>임의 스텝으로 직접 점프 (외부 진입점용).</summary>
        void GoTo(OnboardingFlowStep step);

        /// <summary>§5로 이동 — AgentCreationScene 비동기 로드.</summary>
        UniTask BeginAgentCreationAsync(CancellationToken ct = default);

        /// <summary>§6 로딩 단계 진입 + 메시지 시퀀스 실행 + Office 진입.</summary>
        UniTask BeginLoadingAsync(string agentName, CancellationToken ct = default);

        /// <summary>AgentOfficeScene 비동기 로드.</summary>
        UniTask EnterOfficeAsync(CancellationToken ct = default);
    }
}
