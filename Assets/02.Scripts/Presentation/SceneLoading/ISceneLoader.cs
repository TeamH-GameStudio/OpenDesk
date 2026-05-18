using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Presentation.SceneLoading
{
    [Obsolete("Use IGameSceneLoader instead. ISceneLoader 는 Loading 씬 경유 패턴이지만 OpenDesk 는 persistent overlay 로 전환됨 (LoadingManager).")]
    public interface ISceneLoader
    {
        // 로딩 진행률 0.0 ~ 1.0
        ReadOnlyReactiveProperty<float>  Progress   { get; }
        ReadOnlyReactiveProperty<string> StatusText { get; }

        // 로딩 씬을 거쳐 목적지 씬으로 이동
        UniTask LoadSceneAsync(string sceneName, CancellationToken ct = default);
    }
}
