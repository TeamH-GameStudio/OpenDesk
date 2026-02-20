using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Presentation.SceneLoading;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// Loading 씬 경유 비동기 씬 전환
    ///
    /// 흐름:
    ///   현재 씬 → Loading 씬 (즉시) → 목적지 씬 비동기 로드 → 전환
    ///
    /// 특징:
    ///   - Loading 씬은 static 변수로 목적지를 수신 (씬 간 DI 없이 간단하게)
    ///   - Progress를 ReactiveProperty로 스트리밍 → UI 자동 업데이트
    /// </summary>
    public class SceneLoader : ISceneLoader, IDisposable
    {
        // 로딩 씬 이름 (Build Settings에 등록 필요)
        private const string LoadingSceneName = "Loading";

        // Loading 씬이 읽어갈 다음 목적지
        public static string PendingScene { get; private set; } = "";

        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;

        public async UniTask LoadSceneAsync(string sceneName, CancellationToken ct = default)
        {
            PendingScene        = sceneName;
            _progress.Value     = 0f;
            _statusText.Value   = "로딩 중...";

            // 1. Loading 씬으로 즉시 전환 (동기)
            SceneManager.LoadScene(LoadingSceneName);

            // 2. Loading 씬이 자리잡을 때까지 대기
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            // 3. 목적지 씬 비동기 로드 (allowSceneActivation = false)
            var op = SceneManager.LoadSceneAsync(sceneName);
            op.allowSceneActivation = false;

            // 4. 90%까지 진행률 업데이트
            while (op.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                _progress.Value   = op.progress;
                _statusText.Value = $"로딩 중... {op.progress * 100f:F0}%";
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 5. 로드 완료 직전 — Loading 씬에서 연출 마무리 대기 (0.5초)
            _progress.Value   = 0.95f;
            _statusText.Value = "준비 완료...";
            await UniTask.Delay(500, cancellationToken: ct);

            // 6. 씬 전환 허용
            _progress.Value         = 1f;
            op.allowSceneActivation = true;

            // 7. 전환 완료까지 대기
            while (!op.isDone)
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

            Debug.Log($"[SceneLoader] {sceneName} 로드 완료");
        }

        public void Dispose()
        {
            _progress.Dispose();
            _statusText.Dispose();
        }
    }
}
