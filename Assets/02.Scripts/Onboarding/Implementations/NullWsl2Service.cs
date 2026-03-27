using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Services;
using R3;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// Windows 이외 플랫폼(Mac/Linux)에서 등록되는 Null Object
    /// UNITY_STANDALONE_WIN 심볼이 없을 때 OnboardingInstaller가 이 구현을 주입
    /// </summary>
    public class NullWsl2Service : IWsl2Service
    {
        public ReadOnlyReactiveProperty<float>  Progress   { get; } =
            new ReactiveProperty<float>(1f).ToReadOnlyReactiveProperty();
        public ReadOnlyReactiveProperty<string> StatusText { get; } =
            new ReactiveProperty<string>("WSL2 불필요 (Windows 전용)").ToReadOnlyReactiveProperty();

        public UniTask<bool> IsEnabledAsync(CancellationToken ct = default) =>
            UniTask.FromResult(true);

        public UniTask<IReadOnlyList<string>> GetDistributionsAsync(CancellationToken ct = default) =>
            UniTask.FromResult<IReadOnlyList<string>>(System.Array.Empty<string>());

        public UniTask<Wsl2InstallResult> EnableAsync(CancellationToken ct = default) =>
            UniTask.FromResult(new Wsl2InstallResult { Success = true, NeedsReboot = false, Message = "N/A" });
    }
}
