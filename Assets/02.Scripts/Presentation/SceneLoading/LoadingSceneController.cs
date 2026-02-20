using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// Loading 씬에 있는 컨트롤러
    /// SceneLoader.PendingScene을 읽어 목적지 씬을 비동기 로드
    ///
    /// 씬 구성:
    ///   - Canvas > ProgressBar (Slider)
    ///   - Canvas > StatusText (TextMeshPro)
    ///   - Canvas > LogoImage (Image) — 선택
    /// </summary>
    public class LoadingSceneController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Slider    _progressBar;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("연출")]
        [SerializeField] private float _fadeInDuration  = 0.3f;
        [SerializeField] private float _fadeOutDuration = 0.3f;

        private CancellationTokenSource _cts;

        private void Start()
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy()
            );
            RunLoadingAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid RunLoadingAsync(CancellationToken ct)
        {
            // 1. 페이드 인
            await FadeCanvasAsync(0f, 1f, _fadeInDuration, ct);

            var destination = SceneLoader.PendingScene;
            if (string.IsNullOrEmpty(destination))
            {
                Debug.LogError("[Loading] PendingScene이 비어있습니다!");
                return;
            }

            // 2. 비동기 로드
            var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(destination);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                UpdateUI(op.progress, $"로딩 중... {op.progress * 100f:F0}%");
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            UpdateUI(0.99f, "준비 완료...");
            await UniTask.Delay(400, cancellationToken: ct);

            // 3. 페이드 아웃
            await FadeCanvasAsync(1f, 0f, _fadeOutDuration, ct);

            // 4. 전환
            op.allowSceneActivation = true;
        }

        // ── UI 업데이트 ──────────────────────────────────────────────────────

        private void UpdateUI(float progress, string text)
        {
            if (_progressBar != null) _progressBar.value = progress;
            if (_statusText  != null) _statusText.text   = text;
        }

        // ── 페이드 ───────────────────────────────────────────────────────────

        private async UniTask FadeCanvasAsync(
            float from, float to, float duration, CancellationToken ct)
        {
            if (_canvasGroup == null) return;

            var elapsed = 0f;
            _canvasGroup.alpha = from;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed            += Time.deltaTime;
                _canvasGroup.alpha  = Mathf.Lerp(from, to, elapsed / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _canvasGroup.alpha = to;
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
