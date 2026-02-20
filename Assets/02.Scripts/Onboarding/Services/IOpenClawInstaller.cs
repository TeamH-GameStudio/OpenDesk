using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// OpenClaw 자동 설치
    /// 진행률 스트림을 통해 UI와 소통
    /// </summary>
    public interface IOpenClawInstaller
    {
        // 설치 진행률 0.0 ~ 1.0
        ReadOnlyReactiveProperty<float>  Progress    { get; }
        ReadOnlyReactiveProperty<string> StatusText  { get; }

        UniTask<bool> InstallAsync(CancellationToken ct = default);
    }
}
