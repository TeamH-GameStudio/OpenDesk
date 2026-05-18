using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// <see cref="IGameSceneLoader"/> 기본 구현 — ProjectH SceneLoader 패턴 포팅.
    /// Singleton 으로 등록 (CoreInstaller).
    ///
    /// 흐름:
    ///   1. LoadStarted 발행 → 오버레이가 페이드 인
    ///   2. SceneManager.LoadSceneAsync(allowSceneActivation=false) 와 최소 표시 시간을 병렬
    ///   3. op.progress 0..0.9 를 displayed 0..0.95 로 매핑해 부드럽게 보고
    ///   4. 둘 다 끝나면 allowSceneActivation = true → 0.95..1.0 진행 + 활성화
    ///   5. PostActivation 250ms 안정 대기 후 LoadCompleted 발행 → 페이드 아웃
    /// </summary>
    public sealed class GameSceneLoader : IGameSceneLoader
    {
        // 빠른 SSD 에서도 로딩 화면이 한 번 깜빡이고 사라지지 않도록 강제 표시 시간.
        private const int MinDisplayMs = 1500;

        // 활성화 직후 새 씬 첫 프레임을 안정적으로 그리기 위한 짧은 대기.
        private const int PostActivationMs = 250;

        public event Action<float> Progress;
        public event Action LoadStarted;
        public event Action LoadCompleted;

        private bool _inFlight;

        public async UniTask ChangeSceneAsync(string sceneName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[GameSceneLoader] sceneName 이 비어있음 — 무시.");
                return;
            }
            if (_inFlight)
            {
                Debug.LogWarning($"[GameSceneLoader] 이미 다른 씬 전환이 진행 중. '{sceneName}' 무시.");
                return;
            }

            _inFlight = true;
            try
            {
                Debug.Log($"[GameSceneLoader] Begin '{sceneName}'");
                LoadStarted?.Invoke();
                NotifyProgress(0f);

                var minDelayTask = UniTask.Delay(MinDisplayMs, cancellationToken: ct);
                var loadTask = LoadInternalAsync(sceneName, ct);
                await UniTask.WhenAll(minDelayTask, loadTask);

                NotifyProgress(1f);
                await UniTask.Delay(PostActivationMs, cancellationToken: ct);
                Debug.Log($"[GameSceneLoader] Done '{sceneName}'");
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[GameSceneLoader] '{sceneName}' 전환 취소됨.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSceneLoader] '{sceneName}' 전환 실패: {e}");
            }
            finally
            {
                LoadCompleted?.Invoke();
                _inFlight = false;
            }
        }

        private async UniTask LoadInternalAsync(string sceneName, CancellationToken ct)
        {
            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (op == null)
            {
                Debug.LogError($"[GameSceneLoader] '{sceneName}' LoadSceneAsync == null. Build Settings 확인.");
                return;
            }
            op.allowSceneActivation = false;

            // Unity 의 op.progress 는 0..0.9 까지만 차오르고 0.9 부터는 활성화 대기 단계.
            // 사용자에게는 그 매핑을 0..0.95 로 보여주고, 0.95..1.0 은 활성화 후 채운다.
            float displayed = 0f;
            while (op.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                float target = Mathf.Min(op.progress / 0.9f * 0.95f, 0.95f);
                while (displayed < target)
                {
                    displayed = Mathf.MoveTowards(displayed, target, 0.04f);
                    NotifyProgress(displayed);
                    await UniTask.Delay(16, cancellationToken: ct);
                }
                await UniTask.Yield();
            }

            NotifyProgress(0.95f);
            op.allowSceneActivation = true;

            while (!op.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield();
            }

            // Single 모드라 자동 적용되지만 명시적으로 active scene 지정.
            var loaded = SceneManager.GetSceneByName(sceneName);
            if (loaded.IsValid()) SceneManager.SetActiveScene(loaded);
        }

        private void NotifyProgress(float value)
        {
            Progress?.Invoke(Mathf.Clamp01(value));
        }
    }
}
