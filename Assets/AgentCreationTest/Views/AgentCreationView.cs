using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using AgentCreationTest.Models;
using AgentCreationTest.ViewModels;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AgentCreationTest.Views
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class AgentCreationView : MonoBehaviour
    {
        [SerializeField] private bool isAddFlow;

        public event Action<AgentDraft> AgentCreated;

        // Fired once the internal ViewModel is constructed in OnEnable. External
        // bridges (e.g. AgentPreviewBinder) hook this to subscribe to wardrobe
        // changes without racing the OnEnable order.
        public event Action<AgentCreationViewModel> ViewModelReady;

        // Pointer drag delta (pixels) over the 3D preview frame. Bridges hook
        // this to rotate the underlying character/rig.
        public event Action<Vector2> PreviewDragged;

        // Fires once after the visual tree is built and the rail list elements
        // are queried, so an external controller (AgentPreviewActionRail) can
        // populate the buttons without racing OnEnable.
        public event Action ActionRailsReady;

        public AgentCreationViewModel ViewModel => _viewModel;

        private const string StepActiveClass = "step--active";
        private const string StepEnteringClass = "step--entering";
        private const string ProgressDotActiveClass = "progress-dot--active";
        private const string ProgressDotDoneClass = "progress-dot--done";
        private const string TraitChipClass = "trait-chip";
        private const string TraitChipSelectedClass = "trait-chip--selected";
        private const string TraitChipDisabledClass = "trait-chip--disabled";
        private const string OptionCellClass = "option-cell";
        private const string OptionCellSelectedClass = "option-cell--selected";
        private const string PartTabClass = "part-tab";
        private const string PartTabActiveClass = "part-tab--active";
        private const string ModelCardClass = "model-card";
        private const string ModelCardSelectedClass = "model-card--selected";
        private const string ModelCardLockedClass = "model-card--locked";

        // Preview commit feedback. 캐릭터 머리 위 이름/역할 버블과 캐릭터 밑 트레잇 칩 영역은
        // 각자 commit 시점에 등장 + 짧은 ding 펄스를 받는다. 캐릭터 자체는 펄스 없음.
        private const string NameBubbleHiddenClass = "preview-name-bubble--hidden";
        private const string NameBubbleDingClass = "preview-name-bubble--ding";
        private const string TraitsBarHiddenClass = "preview-traits-bar--hidden";
        private const string TraitsBarDingClass = "preview-traits-bar--ding";
        private const long DingDwellMs = 180;

        private UIDocument _document;
        private AgentCreationViewModel _viewModel;
        private AgentAvatarPainter _avatarPainter;

        private Label _title;
        private Label _previewName;
        private Label _previewRole;
        private VisualElement _previewTraits;
        private VisualElement _nameBubble;
        private VisualElement _traitsBar;

        private readonly VisualElement[] _stepRoots = new VisualElement[5];

        // Step 1 / 2
        private TextField _nameInput;
        private TextField _roleInput;
        private Button _step1Next;
        private Button _step2Prev;
        private Button _step2Next;

        // Step 3
        private VisualElement _traitGrid;
        private VisualElement _selectedTraitsBlock;
        private VisualElement _selectedTraitsRow;
        private Button _step3Prev;
        private Button _step3Next;
        private TextField _customTraitInput;
        private bool _isAddingCustom;

        // Step 4
        private VisualElement _partTabs;
        private VisualElement _partTabIndicator;
        private VisualElement _optionGrid;
        private Button _randomizeBtn;
        private Button _step4Prev;
        private Button _step4Next;
        private WardrobePart _activePart = WardrobePart.Skin;

        // Step 5
        private VisualElement _modelList;
        private Button _step5Prev;
        private Button _step5Next;

        // Action rails — only visible during step-4 (wardrobe). Bound to
        // AgentPreviewActionRail (or any external controller) via the public
        // list accessors below.
        private VisualElement _actionRails;
        private VisualElement _expressionRailList;
        private VisualElement _animationRailList;
        private const string ActionRailsHiddenClass = "action-rails--hidden";

        public VisualElement ExpressionRailList => _expressionRailList;
        public VisualElement AnimationRailList => _animationRailList;

        // Hair-colour rail lives inside the step-4 card (above 랜덤). Only the
        // Hair part tab shows it — every other tab hides the rail so the form
        // doesn't suggest those parts can be tinted.
        private VisualElement _hairColorRail;
        private VisualElement _hairColorList;
        // App UI ColorField at the end of the row — free-pick fallback when
        // none of the presets match. Typed as VisualElement on the View side
        // so this file doesn't need to add a hard reference to Unity.AppUI
        // just for the cache; controller (which already imports App UI)
        // casts it back to ColorField for callback wiring.
        private VisualElement _hairColorCustom;
        public VisualElement HairColorList => _hairColorList;
        public VisualElement HairColorCustom => _hairColorCustom;
        private const string HairColorRailHiddenClass = "hair-color-rail--hidden";

        // 3D preview integration
        private VisualElement _avatarFrame;
        private VisualElement _avatarStage;
        private RenderTexture _previewTexture;
        private bool _isDraggingPreview;

        // Header — progress dots only
        private readonly VisualElement[] _progressDots = new VisualElement[5];

        // Mouth selection retired — facial expressions are now driven by the
        // eye option's EyeExpressionSetSO (one PSD per emotion key). The Mouth
        // enum value and persistence path stay around so existing saved drafts
        // still load, but the wizard no longer surfaces a mouth picker.
        private static readonly (WardrobePart Part, string Label)[] PartDefs =
        {
            (WardrobePart.Skin,   "피부"),
            (WardrobePart.Hair,   "머리"),
            (WardrobePart.Eyes,   "눈"),
            (WardrobePart.Top,    "상의"),
            (WardrobePart.Bottom, "하의"),
            (WardrobePart.Shoes,  "신발"),
        };

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogError("[AgentCreationView] UIDocument or rootVisualElement is null. Assign UXML to the UIDocument.");
                return;
            }

            _viewModel = new AgentCreationViewModel();
            _viewModel.SetIsAddFlow(isAddFlow);

            CacheElements(_document.rootVisualElement);
            _avatarPainter = new AgentAvatarPainter(_document.rootVisualElement);

            BuildPartTabs();
            BuildModelList();
            RegisterCallbacks();
            AttachShortcutHints();
            RenderTraitGrid();
            ViewModelReady?.Invoke(_viewModel);
            ActionRailsReady?.Invoke();
            if (_previewTexture != null) ApplyPreviewTexture();
            RenderSelectedTraits();
            RenderOptionGrid();
            RefreshAll();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            _viewModel = null;
            _avatarPainter = null;
        }

        // ─── element wiring ────────────────────────────────────

        private void CacheElements(VisualElement root)
        {
            _title         = root.Q<Label>("creation-title");
            _previewName   = root.Q<Label>("preview-name");
            _previewRole   = root.Q<Label>("preview-role");
            _previewTraits = root.Q<VisualElement>("preview-traits");
            _nameBubble    = root.Q<VisualElement>("preview-name-bubble");
            _traitsBar     = root.Q<VisualElement>("preview-traits-bar");

            for (int i = 0; i < _stepRoots.Length; i++)
            {
                _stepRoots[i] = root.Q<VisualElement>($"step-{i + 1}");
            }

            _nameInput  = root.Q<TextField>("name-input");
            _roleInput  = root.Q<TextField>("role-input");
            _step1Next  = root.Q<Button>("step-1-next");
            _step2Prev  = root.Q<Button>("step-2-prev");
            _step2Next  = root.Q<Button>("step-2-next");

            _traitGrid           = root.Q<VisualElement>("trait-grid");
            _selectedTraitsBlock = root.Q<VisualElement>("selected-traits-block");
            _selectedTraitsRow   = root.Q<VisualElement>("selected-traits-row");
            _step3Prev           = root.Q<Button>("step-3-prev");
            _step3Next           = root.Q<Button>("step-3-next");

            _partTabs    = root.Q<VisualElement>("part-tabs");
            _optionGrid  = root.Q<VisualElement>("option-grid");
            _randomizeBtn = root.Q<Button>("randomize-btn");
            _step4Prev   = root.Q<Button>("step-4-prev");
            _step4Next   = root.Q<Button>("step-4-next");

            _modelList = root.Q<VisualElement>("model-list");
            _step5Prev = root.Q<Button>("step-5-prev");
            _step5Next = root.Q<Button>("step-5-next");

            _avatarFrame = root.Q<VisualElement>(className: "avatar-frame");
            _avatarStage = root.Q<VisualElement>("avatar-stage");

            _actionRails         = root.Q<VisualElement>("action-rails");
            _expressionRailList  = root.Q<VisualElement>("expression-rail-list");
            _animationRailList   = root.Q<VisualElement>("animation-rail-list");
            _hairColorRail       = root.Q<VisualElement>("hair-color-rail");
            _hairColorList       = root.Q<VisualElement>("hair-color-list");
            _hairColorCustom     = root.Q<VisualElement>("hair-color-custom");

            for (int i = 0; i < _progressDots.Length; i++)
            {
                _progressDots[i] = root.Q<VisualElement>($"progress-dot-{i + 1}");
            }
        }

        // ─── 3D preview integration ────────────────────────────

        // Attaches a RenderTexture as the background of .avatar-frame and hides
        // the 2D avatar tree. Safe to call before OnEnable — the texture is
        // remembered and applied once the visual tree is wired.
        public void SetPreviewTexture(RenderTexture renderTexture)
        {
            _previewTexture = renderTexture;
            if (_avatarFrame == null) return;
            ApplyPreviewTexture();
        }

        public void ClearPreviewTexture()
        {
            _previewTexture = null;
            if (_avatarFrame == null) return;
            _avatarFrame.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            if (_avatarStage != null) _avatarStage.style.display = DisplayStyle.Flex;
        }

        private void ApplyPreviewTexture()
        {
            if (_avatarFrame == null) return;
            if (_previewTexture == null)
            {
                _avatarFrame.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                if (_avatarStage != null) _avatarStage.style.display = DisplayStyle.Flex;
                return;
            }
            _avatarFrame.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_previewTexture));
            if (_avatarStage != null) _avatarStage.style.display = DisplayStyle.None;
        }

        private void RegisterCallbacks()
        {
            if (_nameInput != null) _nameInput.RegisterValueChangedCallback(OnNameChanged);
            if (_roleInput != null) _roleInput.RegisterValueChangedCallback(OnRoleChanged);

            if (_step1Next != null) _step1Next.clicked += OnNextClicked;
            if (_step2Prev != null) _step2Prev.clicked += OnPrevClicked;
            if (_step2Next != null) _step2Next.clicked += OnNextClicked;
            if (_step3Prev != null) _step3Prev.clicked += OnPrevClicked;
            if (_step3Next != null) _step3Next.clicked += OnNextClicked;
            if (_step4Prev != null) _step4Prev.clicked += OnPrevClicked;
            if (_step4Next != null) _step4Next.clicked += OnNextClicked;
            if (_step5Prev != null) _step5Prev.clicked += OnPrevClicked;
            if (_step5Next != null) _step5Next.clicked += OnNextClicked;

            if (_randomizeBtn != null) _randomizeBtn.clicked += OnRandomiseClicked;

            if (_avatarFrame != null)
            {
                _avatarFrame.RegisterCallback<PointerDownEvent>(OnPreviewPointerDown);
                _avatarFrame.RegisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
                _avatarFrame.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp);
                _avatarFrame.RegisterCallback<PointerCaptureOutEvent>(OnPreviewPointerCaptureOut);
            }

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Traits.CollectionChanged += OnTraitsCollectionChanged;
            _viewModel.AgentCompleted += OnAgentCompleted;
            _viewModel.OptionCountsChanged += OnOptionCountsChanged;
            _viewModel.PreviewCommitted += OnPreviewCommitted;
        }

        private void UnregisterCallbacks()
        {
            if (_nameInput != null) _nameInput.UnregisterValueChangedCallback(OnNameChanged);
            if (_roleInput != null) _roleInput.UnregisterValueChangedCallback(OnRoleChanged);

            if (_step1Next != null) _step1Next.clicked -= OnNextClicked;
            if (_step2Prev != null) _step2Prev.clicked -= OnPrevClicked;
            if (_step2Next != null) _step2Next.clicked -= OnNextClicked;
            if (_step3Prev != null) _step3Prev.clicked -= OnPrevClicked;
            if (_step3Next != null) _step3Next.clicked -= OnNextClicked;
            if (_step4Prev != null) _step4Prev.clicked -= OnPrevClicked;
            if (_step4Next != null) _step4Next.clicked -= OnNextClicked;
            if (_step5Prev != null) _step5Prev.clicked -= OnPrevClicked;
            if (_step5Next != null) _step5Next.clicked -= OnNextClicked;

            if (_randomizeBtn != null) _randomizeBtn.clicked -= OnRandomiseClicked;

            if (_avatarFrame != null)
            {
                _avatarFrame.UnregisterCallback<PointerDownEvent>(OnPreviewPointerDown);
                _avatarFrame.UnregisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
                _avatarFrame.UnregisterCallback<PointerUpEvent>(OnPreviewPointerUp);
                _avatarFrame.UnregisterCallback<PointerCaptureOutEvent>(OnPreviewPointerCaptureOut);
            }
            _isDraggingPreview = false;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Traits.CollectionChanged -= OnTraitsCollectionChanged;
                _viewModel.AgentCompleted -= OnAgentCompleted;
                _viewModel.OptionCountsChanged -= OnOptionCountsChanged;
                _viewModel.PreviewCommitted -= OnPreviewCommitted;
            }
        }

        // ─── 3D preview drag ───────────────────────────────────

        private void OnPreviewPointerDown(PointerDownEvent evt)
        {
            // Only react when there's actually a 3D preview to rotate.
            if (_previewTexture == null || _avatarFrame == null) return;
            if (evt.button != 0) return;
            _isDraggingPreview = true;
            _avatarFrame.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPreviewPointerMove(PointerMoveEvent evt)
        {
            if (!_isDraggingPreview) return;
            var delta = (Vector2)evt.deltaPosition;
            if (delta.sqrMagnitude > 0f) PreviewDragged?.Invoke(delta);
        }

        private void OnPreviewPointerUp(PointerUpEvent evt)
        {
            if (!_isDraggingPreview) return;
            _isDraggingPreview = false;
            if (_avatarFrame != null && _avatarFrame.HasPointerCapture(evt.pointerId))
            {
                _avatarFrame.ReleasePointer(evt.pointerId);
            }
        }

        private void OnPreviewPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _isDraggingPreview = false;
        }

        // ─── ViewModel reactions ───────────────────────────────

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AgentCreationViewModel.Step):
                    RefreshStepVisibility();
                    RefreshNavButtons();
                    break;
                case nameof(AgentCreationViewModel.PreviewName):
                    if (_previewName != null) _previewName.text = _viewModel.PreviewName;
                    break;
                case nameof(AgentCreationViewModel.PreviewRole):
                    if (_previewRole != null) _previewRole.text = _viewModel.PreviewRole;
                    break;
                case nameof(AgentCreationViewModel.PreviewTraits):
                    RefreshPreviewTraits();
                    break;
                case nameof(AgentCreationViewModel.HasCommittedNameOrRole):
                case nameof(AgentCreationViewModel.HasCommittedTraits):
                    RefreshPreviewVisibility();
                    break;
                case nameof(AgentCreationViewModel.Wardrobe):
                    _avatarPainter?.Apply(_viewModel.Wardrobe);
                    RenderOptionGrid();
                    break;
                case nameof(AgentCreationViewModel.CanAdvance):
                case nameof(AgentCreationViewModel.NextLabel):
                    RefreshNavButtons();
                    break;
                case nameof(AgentCreationViewModel.Title):
                case nameof(AgentCreationViewModel.StepLabel):
                    if (_title != null) _title.text = _viewModel.Title;
                    break;
                case nameof(AgentCreationViewModel.ModelId):
                    RenderModelSelectionState();
                    break;
            }
        }

        private void OnTraitsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 라이브 trait 토글은 step 3 의 picker (trait-grid + selected-traits-row) 만 갱신한다.
            // 좌측 미리보기 카드는 commit 시점(GoNext)에만 PreviewTraits PropertyChanged 로 갱신된다.
            RenderTraitGrid();
            RenderSelectedTraits();
        }

        private void OnPreviewCommitted()
        {
            // PropertyChanged 핸들러에 의존하지 않고 commit 직후에 직접 모든 미리보기를 갱신한다.
            // (PropertyChanged 가 어떤 이유로 누락되어도 한 번의 commit 으로 카드/칩/가시성/펄스가 모두 적용된다.)
            if (_previewName != null) _previewName.text = _viewModel.PreviewName;
            if (_previewRole != null) _previewRole.text = _viewModel.PreviewRole;
            RefreshPreviewTraits();
            RefreshPreviewVisibility();
            PlayDing();
        }

        private void OnOptionCountsChanged()
        {
            RenderOptionGrid();
        }

        private void OnAgentCompleted(AgentDraft draft)
        {
            AgentCreated?.Invoke(draft);
        }

        // ─── input handlers ────────────────────────────────────

        private void OnNameChanged(ChangeEvent<string> evt)
        {
            if (_viewModel != null) _viewModel.Name = evt.newValue ?? string.Empty;
        }

        private void OnRoleChanged(ChangeEvent<string> evt)
        {
            if (_viewModel != null) _viewModel.Role = evt.newValue ?? string.Empty;
        }

        private void OnNextClicked()
        {
            _viewModel?.GoNext();
        }

        private void OnPrevClicked()
        {
            _viewModel?.GoBack();
        }

        private void OnRandomiseClicked()
        {
            _viewModel?.RandomizeWardrobe();
        }

        // Ctrl + Enter (Win/Linux) 또는 ⌘ + Enter (Mac) → "다음".
        //
        // UI Toolkit 의 KeyDownEvent 는 포커스된 element 에만 dispatch 되어
        // 페이지의 빈 영역에서 단축키가 안 잡힌다. New Input System 의 Keyboard.current 로
        // Update 에서 폴링하면 포커스 위치와 무관하게 동작한다 (프로젝트 표준).
        // CanAdvance 가 막으면 GoNext 가 알아서 noop.
        private void Update()
        {
            if (_viewModel == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            bool ctrlOrCmd =
                kb.ctrlKey.isPressed
                || kb.leftCommandKey.isPressed
                || kb.rightCommandKey.isPressed;
            if (!ctrlOrCmd) return;

            bool enterDown =
                kb.enterKey.wasPressedThisFrame
                || kb.numpadEnterKey.wasPressedThisFrame;
            if (!enterDown) return;

            _viewModel.GoNext();
        }

        // 모든 "다음" 버튼 우측에 단축키 안내 라벨을 동적으로 덧붙인다.
        // Mac 은 ⌘ ↵, 그 외 OS 는 Ctrl ↵.
        private void AttachShortcutHints()
        {
            string hintText = IsMacPlatform() ? "⌘ ↵" : "Ctrl ↵";
            AttachShortcutHint(_step1Next, hintText);
            AttachShortcutHint(_step2Next, hintText);
            AttachShortcutHint(_step3Next, hintText);
            AttachShortcutHint(_step4Next, hintText);
            AttachShortcutHint(_step5Next, hintText);
        }

        private static bool IsMacPlatform() =>
            Application.platform == RuntimePlatform.OSXEditor
            || Application.platform == RuntimePlatform.OSXPlayer;

        private static void AttachShortcutHint(Button btn, string hintText)
        {
            if (btn == null) return;
            var hint = new Label(hintText);
            hint.AddToClassList("nav-shortcut-hint");
            hint.pickingMode = PickingMode.Ignore;   // 버튼 클릭을 가로채지 않게
            btn.Add(hint);
        }

        // ─── rendering ─────────────────────────────────────────

        private void RefreshAll()
        {
            if (_title != null) _title.text = _viewModel.Title;
            if (_previewName != null) _previewName.text = _viewModel.PreviewName;
            if (_previewRole != null) _previewRole.text = _viewModel.PreviewRole;
            if (_nameInput != null) _nameInput.SetValueWithoutNotify(_viewModel.Name);
            if (_roleInput != null) _roleInput.SetValueWithoutNotify(_viewModel.Role);

            _avatarPainter?.Apply(_viewModel.Wardrobe);
            RefreshPreviewTraits();
            RefreshPreviewVisibility();
            RefreshStepVisibility();
            RefreshNavButtons();
            RenderModelSelectionState();
        }

        private void RefreshStepVisibility()
        {
            for (int i = 0; i < _stepRoots.Length; i++)
            {
                var el = _stepRoots[i];
                if (el == null) continue;
                bool active = i + 1 == _viewModel.Step;
                bool wasActive = el.ClassListContains(StepActiveClass);

                if (active)
                {
                    if (!wasActive)
                    {
                        // Newly active step — kick the slide-up + fade-in.
                        // Equivalent to React reference's:
                        //   setShown(false); setTimeout(() => setShown(true), 60);
                        el.AddToClassList(StepActiveClass);
                        el.AddToClassList(StepEnteringClass);
                        el.schedule
                          .Execute(() => el.RemoveFromClassList(StepEnteringClass))
                          .StartingIn(60);
                    }
                }
                else
                {
                    el.RemoveFromClassList(StepActiveClass);
                    el.RemoveFromClassList(StepEnteringClass);
                }
            }
            RefreshProgressDots();
            RefreshActionRailsVisibility();
            RefreshHairColorRailVisibility();
        }

        // Action rails belong to the wardrobe step only. Toggling here keeps
        // the rails out of layout on every other step so the preview pane
        // reclaims the full body width.
        private void RefreshActionRailsVisibility()
        {
            if (_actionRails == null) return;
            const int WardrobeStep = 4;
            bool show = _viewModel.Step == WardrobeStep;
            if (show) _actionRails.RemoveFromClassList(ActionRailsHiddenClass);
            else      _actionRails.AddToClassList(ActionRailsHiddenClass);
        }

        private void RefreshProgressDots()
        {
            int currentIndex = _viewModel.Step - 1;
            for (int i = 0; i < _progressDots.Length; i++)
            {
                var dot = _progressDots[i];
                if (dot == null) continue;
                bool isActive = i == currentIndex;
                bool isDone = i < currentIndex;
                if (isActive) dot.AddToClassList(ProgressDotActiveClass);
                else dot.RemoveFromClassList(ProgressDotActiveClass);
                if (isDone) dot.AddToClassList(ProgressDotDoneClass);
                else dot.RemoveFromClassList(ProgressDotDoneClass);
            }
        }

        private void RefreshNavButtons()
        {
            bool canAdvance = _viewModel.CanAdvance;
            SetEnabled(_step1Next, canAdvance);
            SetEnabled(_step2Next, canAdvance);
            SetEnabled(_step3Next, canAdvance);
            SetEnabled(_step4Next, canAdvance);
            SetEnabled(_step5Next, canAdvance);
            if (_step5Next != null) _step5Next.text = _viewModel.NextLabel;
        }

        private static void SetEnabled(Button btn, bool enabled)
        {
            if (btn != null) btn.SetEnabled(enabled);
        }

        private void RefreshPreviewTraits()
        {
            if (_previewTraits == null) return;
            _previewTraits.Clear();
            // 트레잇 칩은 commit 된 trait 만 노출 — 트레잇 영역 가시성은 RefreshPreviewVisibility 가 따로 통제한다.
            foreach (var trait in _viewModel.PreviewTraits)
            {
                var chip = new Label(trait);
                chip.AddToClassList("preview-traits-bar__chip");
                chip.AddToClassList("od-caption");
                _previewTraits.Add(chip);
            }
        }

        private void RefreshPreviewVisibility()
        {
            if (_viewModel == null) return;
            _nameBubble?.EnableInClassList(NameBubbleHiddenClass, !_viewModel.HasCommittedNameOrRole);
            _traitsBar?.EnableInClassList(TraitsBarHiddenClass, !_viewModel.HasCommittedTraits);
        }

        // commit 시점에 이름/역할 버블과 트레잇 바에 ding 클래스를 잠깐 부착했다 떼면
        // USS 의 scale/translate 전환이 펄스로 재생된다. 캐릭터 자체에는 펄스 없음.
        private void PlayDing()
        {
            PulseClass(_nameBubble, NameBubbleDingClass);
            PulseClass(_traitsBar, TraitsBarDingClass);
        }

        private void PulseClass(VisualElement element, string dingClass)
        {
            if (element == null) return;
            element.AddToClassList(dingClass);
            element.schedule
                .Execute(() => element.RemoveFromClassList(dingClass))
                .StartingIn(DingDwellMs);
        }

        // ─── traits ────────────────────────────────────────────

        private void RenderTraitGrid()
        {
            if (_traitGrid == null) return;
            _traitGrid.Clear();

            foreach (var trait in AgentCreationViewModel.TraitPool)
            {
                _traitGrid.Add(BuildTraitChip(trait, isPool: true));
            }

            foreach (var trait in _viewModel.Traits)
            {
                if (Contains(AgentCreationViewModel.TraitPool, trait)) continue;
                _traitGrid.Add(BuildTraitChip(trait, isPool: false));
            }

            _traitGrid.Add(BuildAddCustomElement());
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        private VisualElement BuildTraitChip(string trait, bool isPool)
        {
            var btn = new Button(() => _viewModel?.ToggleTrait(trait)) { text = trait };
            btn.AddToClassList(TraitChipClass);
            btn.AddToClassList("od-body-sm");

            bool selected = _viewModel.Traits.Contains(trait);
            bool disabled = !selected && _viewModel.Traits.Count >= AgentCreationViewModel.MaxTraits;

            if (selected) btn.AddToClassList(TraitChipSelectedClass);
            else if (disabled) btn.AddToClassList(TraitChipDisabledClass);
            btn.SetEnabled(!disabled || selected);

            return btn;
        }

        private VisualElement BuildAddCustomElement()
        {
            if (_isAddingCustom)
            {
                var row = new VisualElement();
                row.AddToClassList("trait-input-row");

                _customTraitInput = new TextField();
                _customTraitInput.maxLength = 10;
                _customTraitInput.AddToClassList("od-body-sm");
                row.Add(_customTraitInput);

                var cancel = new Button(() =>
                {
                    _customTraitInput = null;
                    _isAddingCustom = false;
                    RenderTraitGrid();
                }) { text = "×" };
                cancel.AddToClassList("trait-input-cancel");
                row.Add(cancel);

                _customTraitInput.schedule.Execute(() =>
                {
                    if (_customTraitInput == null) return;
                    var inner = _customTraitInput.Q("unity-text-input");
                    inner?.Focus();
                }).StartingIn(16);

                _customTraitInput.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitCustomTrait();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                    else if (evt.keyCode == KeyCode.Escape)
                    {
                        _customTraitInput = null;
                        _isAddingCustom = false;
                        RenderTraitGrid();
                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);

                _customTraitInput.RegisterCallback<FocusOutEvent>(_ => CommitCustomTrait());
                return row;
            }

            bool atCap = _viewModel.Traits.Count >= AgentCreationViewModel.MaxTraits;
            var add = new Button(() =>
            {
                if (atCap) return;
                _isAddingCustom = true;
                RenderTraitGrid();
            })
            {
                text = "+ 직접 추가",
            };
            add.AddToClassList(TraitChipClass);
            add.AddToClassList("trait-chip__add");
            add.AddToClassList("od-body-sm");
            if (atCap) add.AddToClassList(TraitChipDisabledClass);
            add.SetEnabled(!atCap);
            return add;
        }

        private void CommitCustomTrait()
        {
            if (_customTraitInput == null) return;
            var value = _customTraitInput.value;
            _customTraitInput = null;
            _isAddingCustom = false;
            if (!string.IsNullOrWhiteSpace(value)) _viewModel.TryAddCustomTrait(value);
            RenderTraitGrid();
        }

        private void RenderSelectedTraits()
        {
            if (_selectedTraitsRow == null || _selectedTraitsBlock == null) return;
            _selectedTraitsRow.Clear();

            if (_viewModel.Traits.Count == 0)
            {
                _selectedTraitsBlock.style.display = DisplayStyle.None;
                return;
            }

            _selectedTraitsBlock.style.display = DisplayStyle.Flex;
            foreach (var trait in _viewModel.Traits)
            {
                var chip = new VisualElement();
                chip.AddToClassList("selected-trait");

                var label = new Label(trait);
                label.AddToClassList("od-body-sm");
                chip.Add(label);

                var remove = new Button(() => _viewModel.ToggleTrait(trait)) { text = "×" };
                remove.AddToClassList("selected-trait__remove");
                chip.Add(remove);

                _selectedTraitsRow.Add(chip);
            }
        }

        // ─── wardrobe ──────────────────────────────────────────

        private void BuildPartTabs()
        {
            if (_partTabs == null) return;
            _partTabs.Clear();

            // Sliding indicator behind the tabs. Added first so it renders
            // beneath the labels — pill positioned absolutely inside .part-tabs.
            _partTabIndicator = new VisualElement();
            _partTabIndicator.AddToClassList("part-tab-indicator");
            // Skip the slide animation on the very first positioning.
            _partTabIndicator.AddToClassList("part-tab-indicator--no-transition");
            _partTabs.Add(_partTabIndicator);

            foreach (var def in PartDefs)
            {
                var part = def.Part;
                var btn = new Button(() => SetActivePart(part)) { text = def.Label };
                btn.AddToClassList(PartTabClass);
                btn.AddToClassList("od-body-sm");
                if (part == _activePart) btn.AddToClassList(PartTabActiveClass);
                btn.userData = part;
                _partTabs.Add(btn);
            }

            // Wait until the tab row has resolved its layout, then snap the
            // indicator under the initial active tab. Subsequent changes use
            // the USS transition (no-transition class is removed first).
            _partTabs.RegisterCallback<GeometryChangedEvent>(OnPartTabsGeometry);
        }

        private void OnPartTabsGeometry(GeometryChangedEvent _)
        {
            UpdateTabIndicator();
            if (_partTabIndicator == null) return;
            // One-shot guard: while --no-transition is still on, schedule its
            // removal 50ms later. The earlier resolvedStyle.width check was
            // bogus — resolvedStyle doesn't update mid-frame after setting
            // inline style, so the schedule never ran and the class stayed
            // on permanently, freezing all subsequent tab transitions.
            if (_partTabIndicator.ClassListContains("part-tab-indicator--no-transition"))
            {
                _partTabIndicator.schedule
                    .Execute(() => _partTabIndicator.RemoveFromClassList("part-tab-indicator--no-transition"))
                    .StartingIn(50);
            }
        }

        private void SetActivePart(WardrobePart part)
        {
            if (_activePart == part) return;
            _activePart = part;
            RefreshPartTabClasses();
            UpdateTabIndicator();
            RenderOptionGrid();
            RefreshHairColorRailVisibility();
        }

        // Hair tint is the only part-level chrome that lives next to the
        // wardrobe grid, so the rail tracks the active tab — show on Hair,
        // hide everywhere else. Step-level visibility is handled separately
        // because the rail also vanishes when the wizard moves off step-4.
        private void RefreshHairColorRailVisibility()
        {
            if (_hairColorRail == null) return;
            bool show = _viewModel != null
                && _viewModel.Step == 4
                && _activePart == WardrobePart.Hair;
            if (show) _hairColorRail.RemoveFromClassList(HairColorRailHiddenClass);
            else      _hairColorRail.AddToClassList(HairColorRailHiddenClass);
        }

        private void RefreshPartTabClasses()
        {
            if (_partTabs == null) return;
            foreach (var child in _partTabs.Children())
            {
                if (child.userData is WardrobePart p)
                {
                    if (p == _activePart) child.AddToClassList(PartTabActiveClass);
                    else child.RemoveFromClassList(PartTabActiveClass);
                }
            }
        }

        // Underbar — thin black line at the bottom of the active tab.
        // Tweak these to taste; bumping inset trims both ends symmetrically,
        // bumping thickness gives a chunkier line.
        private const float TabIndicatorThickness = 2f;
        private const float TabIndicatorInset     = 8f;

        private void UpdateTabIndicator()
        {
            if (_partTabIndicator == null || _partTabs == null) return;
            foreach (var child in _partTabs.Children())
            {
                if (child.userData is WardrobePart p && p == _activePart)
                {
                    var rect = child.layout;
                    if (float.IsNaN(rect.width) || rect.width <= 0f)
                    {
                        Debug.Log($"[Indicator] active tab '{p}' has invalid layout (w={rect.width}) — skipping");
                        return;
                    }
                    _partTabIndicator.style.left   = rect.x + TabIndicatorInset;
                    _partTabIndicator.style.top    = rect.y + rect.height - TabIndicatorThickness;
                    _partTabIndicator.style.width  = rect.width - (TabIndicatorInset * 2f);
                    _partTabIndicator.style.height = TabIndicatorThickness;
                    Debug.Log($"[Indicator] active='{p}' rect=({rect.x:F1},{rect.y:F1},{rect.width:F1}x{rect.height:F1})");
                    return;
                }
            }
        }

        // Eyes/Mouth always have a face — every other slot (skin / hair / top /
        // bottom / shoes) supports the explicit "none" sentinel (Wardrobe.None).
        private static bool PartAllowsNone(WardrobePart part) =>
            part != WardrobePart.Eyes && part != WardrobePart.Mouth;

        private void RenderOptionGrid()
        {
            if (_optionGrid == null) return;
            _optionGrid.Clear();

            int currentIndex = _viewModel.Wardrobe.Get(_activePart);
            int optionCount = _viewModel.GetOptionCount(_activePart);

            if (PartAllowsNone(_activePart))
            {
                _optionGrid.Add(BuildNoneCell(currentIndex));
            }

            for (int i = 0; i < optionCount; i++)
            {
                int idx = i;
                var cell = new Button(() => _viewModel.SetWardrobePart(_activePart, idx));
                cell.text = string.Empty;
                cell.AddToClassList(OptionCellClass);
                if (idx == currentIndex) cell.AddToClassList(OptionCellSelectedClass);

                var swatch = new Label((idx + 1).ToString());
                swatch.AddToClassList("option-swatch");
                swatch.AddToClassList("od-body-sm");

                var swatchColor = SwatchColorFor(_activePart, idx);
                if (swatchColor.HasValue)
                {
                    swatch.style.backgroundColor = swatchColor.Value;
                    swatch.style.color = ContrastingTextColor(swatchColor.Value);
                }

                cell.Add(swatch);

                var check = new Label("✓");
                check.AddToClassList("option-check");
                cell.Add(check);

                _optionGrid.Add(cell);
            }
        }

        private Button BuildNoneCell(int currentIndex)
        {
            var cell = new Button(() => _viewModel.SetWardrobePart(_activePart, Wardrobe.None));
            cell.text = string.Empty;
            cell.AddToClassList(OptionCellClass);
            if (currentIndex == Wardrobe.None) cell.AddToClassList(OptionCellSelectedClass);

            var swatch = new Label("✕");
            swatch.AddToClassList("option-swatch");
            swatch.AddToClassList("option-swatch--none");
            swatch.AddToClassList("od-body-sm");
            cell.Add(swatch);

            var check = new Label("✓");
            check.AddToClassList("option-check");
            cell.Add(check);

            return cell;
        }

        private static Color? SwatchColorFor(WardrobePart part, int index)
        {
            string hex = null;
            switch (part)
            {
                case WardrobePart.Skin:   hex = AgentPalette.SkinColors[index];   break;
                case WardrobePart.Hair:   hex = AgentPalette.HairColors[index];   break;
                case WardrobePart.Top:    hex = AgentPalette.TopColors[index];    break;
                case WardrobePart.Bottom: hex = AgentPalette.BottomColors[index]; break;
                case WardrobePart.Shoes:  hex = AgentPalette.ShoesColors[index];  break;
            }
            if (hex == null) return null;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : (Color?)null;
        }

        private static Color ContrastingTextColor(Color bg)
        {
            // Quick perceived-luminance contrast pick.
            float l = 0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b;
            return l > 0.55f ? new Color(0.16f, 0.16f, 0.15f) : Color.white;
        }

        // ─── model list ────────────────────────────────────────

        private void BuildModelList()
        {
            if (_modelList == null) return;
            _modelList.Clear();

            foreach (var option in AgentCreationViewModel.ModelOptions)
            {
                var captured = option;
                var card = new Button(() =>
                {
                    if (captured.IsLocked) return;
                    _viewModel.ModelId = captured.Id;
                });
                card.text = string.Empty;
                card.AddToClassList(ModelCardClass);
                if (captured.IsLocked) card.AddToClassList(ModelCardLockedClass);
                card.userData = captured;

                var radio = new VisualElement();
                radio.AddToClassList("model-radio");
                var dot = new VisualElement();
                dot.AddToClassList("model-radio__dot");
                radio.Add(dot);
                card.Add(radio);

                var text = new VisualElement();
                text.AddToClassList("model-text");

                var nameRow = new VisualElement();
                nameRow.AddToClassList("model-name-row");
                var nameLbl = new Label(captured.Name);
                nameLbl.AddToClassList("model-name");
                nameLbl.AddToClassList("od-body-md");
                nameRow.Add(nameLbl);
                if (captured.IsLocked)
                {
                    var lockLbl = new Label("[잠김]");
                    lockLbl.AddToClassList("model-lock");
                    lockLbl.AddToClassList("od-caption");
                    nameRow.Add(lockLbl);
                }
                text.Add(nameRow);

                var desc = new Label(captured.Description);
                desc.AddToClassList("model-desc");
                desc.AddToClassList("od-body-sm");
                text.Add(desc);

                card.Add(text);

                if (!string.IsNullOrEmpty(captured.Badge))
                {
                    var badge = new Label(captured.Badge);
                    badge.AddToClassList("model-badge");
                    badge.AddToClassList("od-caption");
                    card.Add(badge);
                }

                _modelList.Add(card);
            }

            RenderModelSelectionState();
        }

        private void RenderModelSelectionState()
        {
            if (_modelList == null) return;
            foreach (var child in _modelList.Children())
            {
                if (child.userData is AgentCreationViewModel.ModelOption opt)
                {
                    if (opt.Id == _viewModel.ModelId) child.AddToClassList(ModelCardSelectedClass);
                    else child.RemoveFromClassList(ModelCardSelectedClass);
                }
            }
        }
    }
}
