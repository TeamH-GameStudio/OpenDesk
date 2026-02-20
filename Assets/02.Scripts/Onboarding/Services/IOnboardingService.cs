using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using R3;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// 온보딩 전체 흐름 조율 — 상태 머신 오케스트레이터
    /// Presentation은 여기만 바라본다
    /// </summary>
    public interface IOnboardingService
    {
        OnboardingState          CurrentState { get; }
        ReadOnlyReactiveProperty<OnboardingState> StateChanged { get; }

        OnboardingContext Context { get; }

        // 온보딩 시작
        UniTask StartAsync(CancellationToken ct = default);

        // UI에서 트리거하는 사용자 액션들
        UniTask RetryCurrentStepAsync(CancellationToken ct = default);
        UniTask SubmitGatewayUrlAsync(string url, CancellationToken ct = default);
        UniTask SkipWorkspaceSetupAsync();
        UniTask ConfirmWorkspacePathAsync(string path, CancellationToken ct = default);
        UniTask EnterOfflineMode();
    }
}
