using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// 로딩 오버레이 매니저. <see cref="LoadingManagerBootstrap"/> 가 코드로 GameObject 를 생성한 뒤
    /// VContainer 가 <see cref="Construct"/> 로 주입한다. Canvas / Slider / TMP 텍스트는 모두
    /// 코드로 빌드 — prefab 의존 0, NotoSansKR 디폴트 폰트만 가정.
    ///
    /// IGameSceneLoader 의 LoadStarted/Progress/LoadCompleted 이벤트를 받아 페이드/슬라이더/펄스를 제어한다.
    /// </summary>
    public sealed class LoadingManager : MonoBehaviour
    {
        private const float FadeDurationSec = 0.3f;
        private const float PulseScale = 1.06f;
        private const float PulseHalfDurationSec = 0.75f;
        private const string TitleText = "OpenDesk";

        // 로딩 화면은 다음 씬과 강한 대비를 만들어야 페이드가 눈에 보인다.
        // OpenDesk 의 다른 화면은 거의 흰 베이지 톤 → 로딩은 brand-signature 풀컬러 배경 + 흰 텍스트.
        private static readonly Color BrandColor = new Color(0.831f, 0.502f, 0.420f);   // #D4806B
        private static readonly Color BackgroundColor = new Color(0.831f, 0.502f, 0.420f); // 풀컬러 배경
        private static readonly Color WhiteText = Color.white;
        private static readonly Color WhiteFill = Color.white;
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.25f);  // 흰 트랙 알파
        private static readonly Color BodyColor = new Color(1f, 1f, 1f, 0.85f);   // 거의 흰 % 텍스트
        private static readonly Color CaptionColor = new Color(1f, 1f, 1f, 0.7f); // 약한 흰 팁 텍스트

        private IGameSceneLoader _sceneLoader;
        private Canvas _canvas;
        private CanvasGroup _group;
        private Slider _progressBar;
        private TMP_Text _percentText;
        private TMP_Text _tipText;
        private RectTransform _logoPulse;

        private bool _pulsing;

        [Inject]
        public void Construct(IGameSceneLoader sceneLoader)
        {
            _sceneLoader = sceneLoader;
            if (_sceneLoader == null) return;
            _sceneLoader.LoadStarted += OnLoadStarted;
            _sceneLoader.LoadCompleted += OnLoadCompleted;
            _sceneLoader.Progress += OnProgress;
        }

        private void Awake()
        {
            BuildUi();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_sceneLoader != null)
            {
                _sceneLoader.LoadStarted -= OnLoadStarted;
                _sceneLoader.LoadCompleted -= OnLoadCompleted;
                _sceneLoader.Progress -= OnProgress;
            }
        }

        private void OnLoadStarted()
        {
            _tipText?.SetText(LoadingTips.GetRandom());
            if (_progressBar != null) _progressBar.value = 0f;
            _percentText?.SetText("0%");
            _canvas.gameObject.SetActive(true);
            FadeAsync(true).Forget();
            PulseLogoAsync().Forget();
        }

        private void OnLoadCompleted()
        {
            FadeAsync(false).Forget();
        }

        private void OnProgress(float value)
        {
            if (_progressBar != null) _progressBar.value = value;
            _percentText?.SetText($"{Mathf.RoundToInt(value * 100f)}%");
        }

        private async UniTaskVoid FadeAsync(bool show)
        {
            float from = _group.alpha;
            float to = show ? 1f : 0f;
            _group.blocksRaycasts = show;
            float elapsed = 0f;
            while (elapsed < FadeDurationSec)
            {
                if (this == null) return;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FadeDurationSec);
                _group.alpha = Mathf.Lerp(from, to, t);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            if (this == null) return;
            _group.alpha = to;
            if (!show) _canvas.gameObject.SetActive(false);
        }

        // 로고가 1.0 ↔ 1.06 사이에서 무한 펄스. _pulsing 플래그로 중복 시작 차단.
        // 캔버스가 비활성화되면 자동 종료.
        private async UniTaskVoid PulseLogoAsync()
        {
            if (_pulsing) return;
            _pulsing = true;
            try
            {
                while (this != null && _canvas != null && _canvas.gameObject.activeSelf)
                {
                    await TweenScaleAsync(1f, PulseScale, PulseHalfDurationSec);
                    await TweenScaleAsync(PulseScale, 1f, PulseHalfDurationSec);
                }
            }
            finally
            {
                _pulsing = false;
                if (_logoPulse != null) _logoPulse.localScale = Vector3.one;
            }
        }

        private async UniTask TweenScaleAsync(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (this == null || _logoPulse == null) return;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                float s = Mathf.Lerp(from, to, t);
                _logoPulse.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        // ─── UI 빌드 (prefab 없이 코드로) ────────────────────────────────
        private void BuildUi()
        {
            // 루트 Canvas + Scaler + Raycaster + CanvasGroup
            var canvasGo = new GameObject(
                "LoadingCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32760; // 사실상 최상위 — 다른 UI 위에 떠야 함

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _group = canvasGo.GetComponent<CanvasGroup>();

            // 배경 (n0 톤)
            CreateStretchedImage(canvasGo.transform, "Background", BackgroundColor);

            // 중앙 로고 텍스트 (흰색, 굵게)
            var titleRt = CreateText(
                canvasGo.transform, "Title", TitleText,
                fontSize: 72, color: WhiteText, bold: true,
                anchoredPos: new Vector2(0f, 60f), size: new Vector2(600f, 100f));
            _logoPulse = titleRt;

            // 진행률 바
            var sliderGo = new GameObject("ProgressBar", typeof(Slider));
            sliderGo.transform.SetParent(canvasGo.transform, false);
            _progressBar = sliderGo.GetComponent<Slider>();
            _progressBar.minValue = 0f;
            _progressBar.maxValue = 1f;
            _progressBar.value = 0f;
            _progressBar.interactable = false;
            _progressBar.transition = Selectable.Transition.None;
            var sliderRt = (RectTransform)sliderGo.transform;
            SetCenterAnchor(sliderRt, new Vector2(0f, -40f), new Vector2(360f, 6f));

            // Slider 내부 — Background + FillArea/Fill
            CreateStretchedImage(sliderGo.transform, "Background", TrackColor);

            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            fillAreaGo.transform.SetParent(sliderGo.transform, false);
            var fillAreaRt = (RectTransform)fillAreaGo.transform;
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = Vector2.zero;
            fillAreaRt.offsetMax = Vector2.zero;

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fill = fillGo.GetComponent<Image>();
            fill.color = WhiteFill;  // 주황 배경 위 흰색 fill — 강한 대비
            var fillRt = fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            _progressBar.fillRect = fillRt;

            // 진행률 % 텍스트
            _percentText = CreateText(
                canvasGo.transform, "Percent", "0%",
                fontSize: 18, color: BodyColor, bold: false,
                anchoredPos: new Vector2(0f, -70f), size: new Vector2(200f, 30f))
                .GetComponent<TMP_Text>();

            // 하단 팁 텍스트 (multi-line, wrapping)
            var tipRt = CreateText(
                canvasGo.transform, "Tip", string.Empty,
                fontSize: 16, color: CaptionColor, bold: false,
                anchoredPos: new Vector2(0f, 80f), size: new Vector2(720f, 60f));
            tipRt.anchorMin = new Vector2(0.5f, 0f);
            tipRt.anchorMax = new Vector2(0.5f, 0f);
            tipRt.pivot = new Vector2(0.5f, 0f);
            tipRt.anchoredPosition = new Vector2(0f, 80f);
            _tipText = tipRt.GetComponent<TMP_Text>();
            _tipText.enableWordWrapping = true;
        }

        private static void CreateStretchedImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static RectTransform CreateText(Transform parent, string name, string text,
            int fontSize, Color color, bool bold,
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            SetCenterAnchor(rt, anchoredPos, size);
            return rt;
        }

        private static void SetCenterAnchor(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }
    }
}
