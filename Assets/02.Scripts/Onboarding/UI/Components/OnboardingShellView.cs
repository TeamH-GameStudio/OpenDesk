using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services.Licensing;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using OpenDesk.Onboarding.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Onboarding.UI.Components
{
    /// <summary>
    /// 온보딩 UIDocument 컨트롤러. 5개 step root를 토글하며 sub-view들의 라이프사이클을 관리한다.
    /// FlowController와는 method injection으로 양방향 연결 (둘 다 RegisterComponent로 등록되어 lazy resolve).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OnboardingShellView : MonoBehaviour, IOnboardingShellView
    {
        [SerializeField] private Texture2D _blurredBackground;

        private UIDocument _document;
        private IUserProfileService _userProfileService;
        private IPlanService _planService;
        private IGoogleAuthService _authService;
        private IOnboardingFlowController _flowController;
        private ILicenseService _licenseService;
        private IDeviceFingerprintService _fingerprintService;

        private VisualElement _stepWelcome;
        private VisualElement _stepPlan;
        private VisualElement _stepAuth;
        private VisualElement _stepLicense;
        private VisualElement _stepUser;
        private VisualElement _stepLoading;

        private OnbWelcomeView _welcomeView;
        private OnbPlanView _planView;
        private OnbAuthView _authView;
        private OnbLicenseView _licenseView;
        private OnbUserView _userView;
        private OnbLoadingView _loadingView;

        private OnbWelcomeViewModel _welcomeVm;
        private OnbPlanViewModel _planVm;
        private OnbAuthViewModel _authVm;
        private OnbLicenseViewModel _licenseVm;
        private OnbUserViewModel _userVm;

        private readonly Dictionary<OnboardingFlowStep, VisualElement> _stepRoots = new();

        [Inject]
        public void Construct(
            IUserProfileService userProfileService,
            IPlanService planService,
            IGoogleAuthService authService,
            IOnboardingFlowController flowController,
            ILicenseService licenseService = null,
            IDeviceFingerprintService fingerprintService = null)
        {
            _userProfileService = userProfileService;
            _planService = planService;
            _authService = authService;
            _flowController = flowController;
            _licenseService = licenseService;
            _fingerprintService = fingerprintService;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = _document?.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[OnboardingShellView] rootVisualElement가 null입니다. UIDocument의 SourceAsset이 설정되어 있는지 확인하세요.");
                return;
            }

            _stepWelcome = root.Q<VisualElement>("onb-step-welcome");
            _stepPlan = root.Q<VisualElement>("onb-step-plan");
            _stepAuth = root.Q<VisualElement>("onb-step-auth");
            _stepLicense = root.Q<VisualElement>("onb-step-license");
            _stepUser = root.Q<VisualElement>("onb-step-user");
            _stepLoading = root.Q<VisualElement>("onb-step-loading");

            _stepRoots[OnboardingFlowStep.Welcome] = _stepWelcome;
            _stepRoots[OnboardingFlowStep.Plan] = _stepPlan;
            _stepRoots[OnboardingFlowStep.Auth] = _stepAuth;
            _stepRoots[OnboardingFlowStep.License] = _stepLicense;
            _stepRoots[OnboardingFlowStep.User] = _stepUser;
            _stepRoots[OnboardingFlowStep.Loading] = _stepLoading;

            ApplyBlurredBackground(root);

            BuildSubViews();
            // EnforceTextColor 호출 제거 — App UI Panel 루트가 자체 테마(light)를 제공하므로
            // PanelSettings 다크 ThemeStyleSheet 의 텍스트 override 가 발생하지 않는다.
            // 메서드 본체는 회귀 발생 시 빠른 롤백을 위해 일시 보존 (검증 후 제거 예정).

            // 단방향 연결: ShellView → FlowController. FlowController의 [Inject] 의존성을 끊어 순환 회피.
            _flowController?.AttachShellView(this);
        }

        /// <summary>
        /// [DEPRECATED — App UI Panel 도입으로 불필요해짐. 회귀 검증 후 제거 예정]
        /// PanelSettings의 ThemeStyleSheet(주로 Dark Theme)가 텍스트를 회색으로 override하는 케이스를
        /// 차단한다. inline style은 USS보다 우선하므로 안전. placeholder/뱃지/아이콘 등 의도적으로
        /// 다른 컬러인 요소는 클래스 목록으로 제외한다.
        /// </summary>
        private static void EnforceTextColor(VisualElement root)
        {
            var primary = new Color(0.078f, 0.078f, 0.075f); // --n900 (#141413)
            var placeholder = new Color(0.69f, 0.68f, 0.65f); // --n400

            foreach (var label in root.Query<Label>().ToList())
            {
                if (label.ClassListContains("onb-input-placeholder"))
                {
                    label.style.color = placeholder;
                    continue;
                }
                if (label.ClassListContains("onb-plan-card__badge")) continue;
                if (label.ClassListContains("onb-privacy-notice__icon")) continue;

                label.style.color = primary;
            }

            foreach (var tf in root.Query<TextField>().ToList())
            {
                tf.style.color = primary;
                foreach (var inner in tf.Query<TextElement>().ToList())
                {
                    inner.style.color = primary;
                }
            }
        }

        private void OnDisable()
        {
            DisposeSubViews();
        }

        private void ApplyBlurredBackground(VisualElement root)
        {
            if (_blurredBackground == null) return;
            foreach (var bg in root.Query<VisualElement>(className: "onb-frame--blurred").ToList())
            {
                bg.style.backgroundImage = new StyleBackground(_blurredBackground);
            }
        }

        private void BuildSubViews()
        {
            DisposeSubViews();

            var ct = this.GetCancellationTokenOnDestroy();

            // §1
            _welcomeVm = new OnbWelcomeViewModel();
            _welcomeVm.StartRequested += OnWelcomeStart;
            _welcomeView = new OnbWelcomeView(_stepWelcome, _welcomeVm);

            // §2
            _planVm = new OnbPlanViewModel(_planService);
            _planVm.PlanSelected += OnPlanSelected;
            _planVm.BackRequested += OnBack;
            _planView = new OnbPlanView(_stepPlan, _planVm, ct);

            // §3
            _authVm = new OnbAuthViewModel(_authService);
            _authVm.AuthSucceeded += OnAuthSucceeded;
            _authVm.BackRequested += OnBack;
            _authView = new OnbAuthView(_stepAuth, _authVm, ct);

            // §3.5 License
            if (_licenseService != null && _stepLicense != null)
            {
                _licenseVm = new OnbLicenseViewModel(_licenseService, _fingerprintService);
                _licenseVm.ActivationSucceeded += OnLicenseActivated;
                _licenseVm.SkipRequested += OnLicenseSkipped;
                _licenseVm.BackRequested += OnBack;
                _licenseView = new OnbLicenseView(_stepLicense, _licenseVm, ct);
            }

            // §4
            _userVm = new OnbUserViewModel(_userProfileService);
            _userVm.UserProfileCommitted += OnUserCommitted;
            _userVm.BackRequested += OnBack;
            _userView = new OnbUserView(_stepUser, _userVm, ct);
        }

        private void DisposeSubViews()
        {
            if (_welcomeVm != null) _welcomeVm.StartRequested -= OnWelcomeStart;
            if (_planVm != null)
            {
                _planVm.PlanSelected -= OnPlanSelected;
                _planVm.BackRequested -= OnBack;
            }
            if (_authVm != null)
            {
                _authVm.AuthSucceeded -= OnAuthSucceeded;
                _authVm.BackRequested -= OnBack;
            }
            if (_licenseVm != null)
            {
                _licenseVm.ActivationSucceeded -= OnLicenseActivated;
                _licenseVm.SkipRequested -= OnLicenseSkipped;
                _licenseVm.BackRequested -= OnBack;
            }
            if (_userVm != null)
            {
                _userVm.UserProfileCommitted -= OnUserCommitted;
                _userVm.BackRequested -= OnBack;
            }

            _welcomeView?.Dispose();
            _planView?.Dispose();
            _authView?.Dispose();
            _licenseView?.Dispose();
            _userView?.Dispose();
            _loadingView?.Dispose();

            _welcomeView = null;
            _planView = null;
            _authView = null;
            _licenseView = null;
            _userView = null;
            _loadingView = null;
        }

        private void OnWelcomeStart() => _flowController?.Advance();
        private void OnBack() => _flowController?.GoBack();
        private void OnPlanSelected(PlanTier _) => _flowController?.Advance();
        private void OnAuthSucceeded(string _) => _flowController?.Advance();
        private void OnLicenseActivated() => _flowController?.Advance();
        private void OnLicenseSkipped() => _flowController?.Advance();
        private void OnUserCommitted(UserProfile _) => _flowController?.Advance();

        // ── IOnboardingShellView ─────────────────────────────────

        public void ShowStep(OnboardingFlowStep step)
        {
            foreach (var pair in _stepRoots)
            {
                if (pair.Value == null) continue;
                pair.Value.EnableInClassList("onb-step--active", pair.Key == step);
            }
        }

        public void ShowLoadingMessage(string message)
        {
            if (_stepLoading == null) return;

            _loadingView?.Dispose();
            var vm = new OnbLoadingViewModel();
            vm.SetMessage(message);
            _loadingView = new OnbLoadingView(_stepLoading, vm);

            ShowStep(OnboardingFlowStep.Loading);
        }

        public async UniTask RunLoadingAsync(string agentName, CancellationToken ct)
        {
            if (_stepLoading == null) return;

            _loadingView?.Dispose();
            var vm = new OnbLoadingViewModel();
            _loadingView = new OnbLoadingView(_stepLoading, vm);

            await vm.RunAsync(agentName, ct);
        }
    }
}
