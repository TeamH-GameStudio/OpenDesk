using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using AgentCreationTest.Models;
using AgentCreationTest.ViewModels;
using UnityEngine;
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

        public AgentCreationViewModel ViewModel => _viewModel;

        private const string StepActiveClass = "step--active";
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

        private UIDocument _document;
        private AgentCreationViewModel _viewModel;
        private AgentAvatarPainter _avatarPainter;

        private Label _title;
        private Label _previewName;
        private Label _previewRole;
        private VisualElement _previewTraits;

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
        private VisualElement _optionGrid;
        private Button _randomizeBtn;
        private Button _step4Prev;
        private Button _step4Next;
        private WardrobePart _activePart = WardrobePart.Skin;

        // Step 5
        private VisualElement _modelList;
        private Button _step5Prev;
        private Button _step5Next;

        // 3D preview integration
        private VisualElement _avatarFrame;
        private VisualElement _avatarStage;
        private RenderTexture _previewTexture;

        private static readonly (WardrobePart Part, string Label)[] PartDefs =
        {
            (WardrobePart.Skin,   "피부"),
            (WardrobePart.Hair,   "머리"),
            (WardrobePart.Eyes,   "눈"),
            (WardrobePart.Mouth,  "입"),
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
            RenderTraitGrid();
            ViewModelReady?.Invoke(_viewModel);
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

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Traits.CollectionChanged += OnTraitsCollectionChanged;
            _viewModel.AgentCompleted += OnAgentCompleted;
            _viewModel.OptionCountsChanged += OnOptionCountsChanged;
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

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Traits.CollectionChanged -= OnTraitsCollectionChanged;
                _viewModel.AgentCompleted -= OnAgentCompleted;
                _viewModel.OptionCountsChanged -= OnOptionCountsChanged;
            }
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
            RenderTraitGrid();
            RenderSelectedTraits();
            RefreshPreviewTraits();
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
                if (active) el.AddToClassList(StepActiveClass);
                else el.RemoveFromClassList(StepActiveClass);
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
            // Per spec, preview chips show only after step 3.
            if (_viewModel.Step < 3) return;
            foreach (var trait in _viewModel.PreviewTraits)
            {
                var chip = new Label(trait);
                chip.AddToClassList("preview-card__trait");
                chip.AddToClassList("od-caption");
                _previewTraits.Add(chip);
            }
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
        }

        private void SetActivePart(WardrobePart part)
        {
            if (_activePart == part) return;
            _activePart = part;
            RefreshPartTabClasses();
            RenderOptionGrid();
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

        private void RenderOptionGrid()
        {
            if (_optionGrid == null) return;
            _optionGrid.Clear();

            int currentIndex = _viewModel.Wardrobe.Get(_activePart);
            int optionCount = _viewModel.GetOptionCount(_activePart);
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
