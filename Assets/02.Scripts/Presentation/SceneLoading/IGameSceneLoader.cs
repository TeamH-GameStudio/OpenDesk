using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// 씬 전환 + 로딩 오버레이 통합 진입점.
    /// 호출처는 <see cref="UnityEngine.SceneManagement.SceneManager"/> 를 직접 부르지 말고
    /// 이 인터페이스로 전환한다. 페이드 / 진행률 / 최소 표시 시간이 일관되게 보장된다.
    /// </summary>
    public interface IGameSceneLoader
    {
        /// <summary>0..1 진행률. UI 옵저버(LoadingManager)가 슬라이더 갱신용으로 구독.</summary>
        event Action<float> Progress;

        /// <summary>씬 로드 시작 직전 — 오버레이 페이드 인 트리거.</summary>
        event Action LoadStarted;

        /// <summary>씬 활성화 + 마무리 딜레이 후 — 오버레이 페이드 아웃 트리거.</summary>
        event Action LoadCompleted;

        /// <summary>지정 씬을 Single 모드로 비동기 전환. 동시 호출 시 첫 호출만 동작.</summary>
        UniTask ChangeSceneAsync(string sceneName, CancellationToken ct = default);
    }
}
