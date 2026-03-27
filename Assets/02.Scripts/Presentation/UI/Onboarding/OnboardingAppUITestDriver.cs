using Unity.AppUI.Core;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpenDesk.Presentation.UI.Onboarding
{
    /// <summary>
    /// App UI 온보딩 테스트 드라이버 — VContainer 없이 UI 상태만 확인
    /// 테스트 씬 전용: OnboardingAppUIController 없이 독립 실행
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class OnboardingAppUITestDriver : MonoBehaviour
    {
        private Panel          _panel;
        private Text           _stepLabel;
        private LinearProgress _progress;
        private Text           _titleLabel;
        private Text           _descLabel;
        private VisualElement  _contentArea;

        private int _currentStep;

        private readonly (float progress, string step, string title, string desc)[] _steps =
        {
            (0.05f, "",          "준비하고 있어요",       "AI 비서 환경을 확인하고 있습니다."),
            (0.15f, "Step 1 / 3", "컴퓨터 환경 확인 중", "필요한 도구들이 있는지 확인하고 있어요.\n약 10초 소요됩니다."),
            (0.50f, "Step 2 / 3", "AI 비서 설치 중",     "AI 비서 프로그램을 다운로드하고 설치하고 있어요.\n인터넷 속도에 따라 2~5분 소요됩니다."),
            (0.75f, "Step 3 / 3", "AI 비서 연결 중",     "설치된 AI 비서와 이 프로그램을 연결하고 있어요."),
            (1.00f, "",           "모든 준비 완료!",      "AI 비서 환경이 성공적으로 구축되었습니다.\n아래 버튼을 눌러 시작하세요!"),
        };

        private void Start()
        {
            var doc = GetComponent<UIDocument>();
            BuildUI(doc.rootVisualElement);
            ShowStep(0);
        }

        private void BuildUI(VisualElement root)
        {
            _panel = new Panel { theme = "dark", scale = "medium" };
            _panel.style.flexGrow = 1;
            root.Add(_panel);

            var container = new VisualElement();
            container.style.flexGrow      = 1;
            container.style.flexDirection = FlexDirection.Column;
            container.style.paddingTop    = new StyleLength(new Length(5, LengthUnit.Percent));
            container.style.paddingBottom = new StyleLength(new Length(5, LengthUnit.Percent));
            container.style.paddingLeft   = new StyleLength(new Length(10, LengthUnit.Percent));
            container.style.paddingRight  = new StyleLength(new Length(10, LengthUnit.Percent));
            _panel.Add(container);

            // 스텝 카운터 + 진행바
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems    = Align.Center;
            headerRow.style.marginBottom  = 16;

            _stepLabel = new Text { text = "" };
            _stepLabel.style.marginRight = 12;

            _progress = new LinearProgress { value = 0f };
            _progress.style.flexGrow = 1;

            headerRow.Add(_stepLabel);
            headerRow.Add(_progress);
            container.Add(headerRow);

            // 타이틀
            _titleLabel = new Text { text = "" };
            _titleLabel.size             = TextSize.XL;
            _titleLabel.primary          = true;
            _titleLabel.style.marginBottom = 8;
            container.Add(_titleLabel);

            // 설명
            _descLabel = new Text { text = "" };
            _descLabel.style.marginBottom = 32;
            _descLabel.style.whiteSpace   = WhiteSpace.Normal;
            container.Add(_descLabel);

            // 콘텐츠 영역 (스텝별 UI)
            _contentArea = new VisualElement();
            _contentArea.style.flexGrow    = 1;
            _contentArea.style.marginBottom = 32;
            container.Add(_contentArea);

            // 이전 / 다음 버튼
            var navRow = new VisualElement();
            navRow.style.flexDirection = FlexDirection.Row;
            navRow.style.justifyContent = Justify.SpaceBetween;

            var prevBtn = new ActionButton { label = "← 이전" };
            prevBtn.clicked += () => ShowStep(_currentStep - 1);

            var nextBtn = new ActionButton { label = "다음 →" };
            nextBtn.accent  = true;
            nextBtn.clicked += () => ShowStep(_currentStep + 1);

            navRow.Add(prevBtn);
            navRow.Add(nextBtn);
            container.Add(navRow);
        }

        private void ShowStep(int index)
        {
            if (index < 0 || index >= _steps.Length) return;
            _currentStep = index;

            var (progress, step, title, desc) = _steps[index];
            _progress.value   = progress;
            _stepLabel.text   = step;
            _titleLabel.text  = title;
            _descLabel.text   = desc;

            _contentArea.Clear();

            switch (index)
            {
                case 0: // 초기
                    var spinner0 = new CircularProgress();
                    spinner0.style.width  = 48;
                    spinner0.style.height = 48;
                    spinner0.style.alignSelf = Align.Center;
                    _contentArea.Add(spinner0);
                    break;

                case 1: // 환경 스캔
                    var spinner1 = new CircularProgress();
                    spinner1.style.width  = 64;
                    spinner1.style.height = 64;
                    spinner1.style.alignSelf = Align.Center;
                    spinner1.style.marginBottom = 16;
                    var scanText = new Text { text = "시스템 환경을 확인하고 있어요..." };
                    scanText.style.alignSelf = Align.Center;
                    _contentArea.Add(spinner1);
                    _contentArea.Add(scanText);
                    break;

                case 2: // 설치 진행
                    var installLabel = new Text { text = "설치 진행률" };
                    installLabel.style.marginBottom = 8;
                    var installBar = new LinearProgress { value = 0.45f };
                    installBar.style.marginBottom = 12;
                    var installStatus = new Text { text = "OpenClaw 다운로드 중... (45%)" };
                    _contentArea.Add(installLabel);
                    _contentArea.Add(installBar);
                    _contentArea.Add(installStatus);
                    break;

                case 3: // Gateway 연결
                    var urlField = new Unity.AppUI.UI.TextField
                    {
                        placeholder = "http://localhost:3000"
                    };
                    urlField.style.marginBottom = 12;
                    var btnRow = new VisualElement();
                    btnRow.style.flexDirection = FlexDirection.Row;
                    var connectBtn = new ActionButton { label = "연결" };
                    connectBtn.accent = true;
                    connectBtn.style.marginRight = 8;
                    connectBtn.clicked += () =>
                        Toast.Build(_panel, "연결 시도 중...", NotificationDuration.Short).Show();
                    var offlineBtn = new ActionButton { label = "오프라인으로 시작" };
                    btnRow.Add(connectBtn);
                    btnRow.Add(offlineBtn);
                    _contentArea.Add(urlField);
                    _contentArea.Add(btnRow);
                    break;

                case 4: // 완료
                    var checkIcon = new Icon { iconName = "check_circle" };
                    checkIcon.style.width    = 64;
                    checkIcon.style.height   = 64;
                    checkIcon.style.alignSelf = Align.Center;
                    checkIcon.style.marginBottom = 24;
                    var enterBtn = new ActionButton { label = "시작하기" };
                    enterBtn.accent    = true;
                    enterBtn.style.alignSelf = Align.Center;
                    enterBtn.clicked  += () =>
                        Toast.Build(_panel, "시작합니다!", NotificationDuration.Short)
                            .SetStyle(NotificationStyle.Positive)
                            .Show();
                    _contentArea.Add(checkIcon);
                    _contentArea.Add(enterBtn);
                    break;
            }
        }
    }
}
