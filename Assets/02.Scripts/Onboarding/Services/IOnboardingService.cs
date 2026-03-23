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

        // Node.js 버전 충돌 시 사용자 선택
        /// <summary>nvm으로 기존 버전과 공존하도록 안전 설치</summary>
        UniTask HandleNodeUpgrade_SafeInstall(CancellationToken ct = default);
        /// <summary>기존 버전을 새 버전으로 덮어쓰기</summary>
        UniTask HandleNodeUpgrade_Overwrite(CancellationToken ct = default);
        /// <summary>Node.js 업그레이드 건너뛰기 (일부 기능 제한)</summary>
        UniTask HandleNodeUpgrade_Skip(CancellationToken ct = default);

        // WSL2 설치 후 재부팅 요청
        void RequestReboot();
    }
}
