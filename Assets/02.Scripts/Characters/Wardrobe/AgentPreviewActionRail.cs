using System;
using System.Collections.Generic;
using AgentCreationTest.Views;
using OpenDesk.Characters.Wardrobe.Expressions;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
// App UI ships its own Button class — alias `Button` to the UI Toolkit one
// because every rail pill in this file is the stock UIElements button.
using Button = UnityEngine.UIElements.Button;

namespace OpenDesk.Characters.Wardrobe
{
    // Populates the wardrobe step's twin action rails with clickable pills.
    //
    //   Left rail (expression-rail-list)  → data-driven: queries the currently
    //                                       equipped eye option's
    //                                       EyeExpressionSetSO and renders one
    //                                       pill per authored expression. The
    //                                       rail rebuilds automatically when
    //                                       the user picks a different eye in
    //                                       the wardrobe grid.
    //   Right rail (animation-rail-list) → inspector-authored: clip names are
    //                                       independent of the wardrobe data,
    //                                       so authors set them on this
    //                                       component directly.
    //
    // The rail driving expressions is intentionally NOT a hardcoded inspector
    // list — that would force every eye style to expose the same emotion set
    // regardless of which PSDs the artist actually shipped for that style.
    public sealed class AgentPreviewActionRail : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("UI Toolkit view that owns the rail list elements.")]
        [SerializeField] private AgentCreationView _view;

        [Tooltip("Drives facial expression swaps via the eye option's expression set.")]
        [SerializeField] private WardrobeApplier _applier;

        [Tooltip("Animator on the preview rig. Play() is called with the entry's StateName.")]
        [SerializeField] private Animator _previewAnimator;

        [Serializable]
        public sealed class AnimationEntry
        {
            [Tooltip("Short label rendered on the pill (1-2 chars / emoji).")]
            public string Label;
            [Tooltip("Animator state name (or clip name when controller plays clips directly).")]
            public string StateName;
            [Tooltip("Optional Animator layer index. Leave at 0 unless the controller uses extra layers.")]
            public int Layer = 0;
        }

        [Serializable]
        public sealed class HairColorEntry
        {
            [Tooltip("Display name for tooling only — the swatch itself is colour-only.")]
            public string Label;
            [Tooltip("Colour written to the hair material's _BaseColor property via MPB.")]
            public Color Color = Color.white;
        }

        [Header("Animation rail (right column)")]
        [SerializeField]
        private List<AnimationEntry> _animations = new List<AnimationEntry>
        {
            new AnimationEntry { Label = "기본", StateName = "Idle"     },
            new AnimationEntry { Label = "타자", StateName = "Typing"   },
            new AnimationEntry { Label = "걷기", StateName = "Walk"     },
            new AnimationEntry { Label = "환호", StateName = "Cheering" },
        };

        [Header("Hair colour rail (above 랜덤)")]
        [Tooltip("First entry is treated as the default and should match WardrobeApplier._defaultHairColor so the boot-state swatch highlights.")]
        [SerializeField]
        private List<HairColorEntry> _hairColors = new List<HairColorEntry>
        {
            new HairColorEntry { Label = "Gray",      Color = Hex("#555555") },
            new HairColorEntry { Label = "Black",     Color = Hex("#2A201A") },
            new HairColorEntry { Label = "Brown",     Color = Hex("#6B4A2E") },
            new HairColorEntry { Label = "ChestNut",  Color = Hex("#8C6B3F") },
            new HairColorEntry { Label = "Honey",     Color = Hex("#A8835A") },
            new HairColorEntry { Label = "Ash",       Color = Hex("#9B8E7A") },
            new HairColorEntry { Label = "Platinum",  Color = Hex("#E8DCC8") },
            new HairColorEntry { Label = "Strawberry",Color = Hex("#C97F5A") },
            new HairColorEntry { Label = "Rose",      Color = Hex("#C78CA0") },
            new HairColorEntry { Label = "Lavender",  Color = Hex("#9A8FB8") },
            new HairColorEntry { Label = "Mint",      Color = Hex("#7FB69A") },
            new HairColorEntry { Label = "Sky",       Color = Hex("#7BA8C7") },
        };

        [Header("Expression rail fallback")]
        [Tooltip("Shown when the equipped eye option has no expression set wired — keeps the rail from looking broken during authoring.")]
        [SerializeField] private bool _showDefaultFallback = true;

        private const string BtnClass         = "action-rail__btn";
        private const string BtnSelectedClass = "action-rail__btn--selected";
        private const string HairSwatchClass         = "hair-color-swatch";
        private const string HairSwatchSelectedClass = "hair-color-swatch--selected";

        private Button _selectedExpressionBtn;
        private Button _selectedAnimationBtn;
        private Button _selectedHairColorBtn;

        // Bound on first BuildHairColorRail call so the callback survives the
        // rebuild — the field itself isn't re-created across rebuilds.
        // Two callbacks because App UI's ColorField fires ChangingEvent<Color>
        // continuously while the user drags the picker and ChangeEvent<Color>
        // once the picker closes. Subscribing to both gives live preview AND
        // a guaranteed final value.
        private ColorField _hairColorCustomField;
        private EventCallback<ChangeEvent<Color>> _hairColorCustomCallback;
        private EventCallback<ChangingEvent<Color>> _hairColorCustomChangingCallback;

        // Helper for the inspector default list — Color hex literals don't
        // compile, so we route through ColorUtility. Falls back to magenta so
        // a typo in a literal is impossible to miss.
        private static Color Hex(string html)
        {
            return ColorUtility.TryParseHtmlString(html, out var c) ? c : Color.magenta;
        }

        private void OnEnable()
        {
            if (_view == null)
            {
                Debug.LogError("[AgentPreviewActionRail] AgentCreationView reference missing.", this);
                return;
            }
            _view.ActionRailsReady += OnActionRailsReady;

            if (_applier != null) _applier.EyesOptionChanged += OnEyesOptionChanged;

            // View may have already fired ActionRailsReady before we subscribed —
            // in that case the rail list elements are already queryable.
            if (_view.ExpressionRailList != null && _view.AnimationRailList != null)
                OnActionRailsReady();
        }

        private void OnDisable()
        {
            if (_view != null) _view.ActionRailsReady -= OnActionRailsReady;
            if (_applier != null) _applier.EyesOptionChanged -= OnEyesOptionChanged;
            if (_hairColorCustomField != null)
            {
                if (_hairColorCustomCallback != null)
                    _hairColorCustomField.UnregisterValueChangedCallback(_hairColorCustomCallback);
                if (_hairColorCustomChangingCallback != null)
                    _hairColorCustomField.UnregisterValueChangingCallback(_hairColorCustomChangingCallback);
                _hairColorCustomCallback = null;
                _hairColorCustomChangingCallback = null;
                _hairColorCustomField = null;
            }
            _selectedExpressionBtn = null;
            _selectedAnimationBtn = null;
            _selectedHairColorBtn = null;
        }

        private void OnActionRailsReady()
        {
            BuildAnimationRail();
            BuildHairColorRail();
            // Seed the expression rail with whatever eye option is already
            // equipped at first render. WardrobeApplier may have called
            // ApplyEyes during ApplyDefaults() before this controller awoke,
            // so EyesOptionChanged won't fire again — we read it directly.
            BuildExpressionRailFor(_applier != null ? _applier.CurrentEyesOption : null);
        }

        private void OnEyesOptionChanged(WardrobePartOptionSO option)
        {
            BuildExpressionRailFor(option);
        }

        private void BuildExpressionRailFor(WardrobePartOptionSO eyeOption)
        {
            var list = _view.ExpressionRailList;
            if (list == null) return;
            list.Clear();
            _selectedExpressionBtn = null;

            var set = eyeOption != null ? eyeOption.ExpressionSet : null;
            if (set == null || set.Expressions == null || set.Expressions.Count == 0)
            {
                // No expression set authored on this eye — show the Default
                // pill as a single-state fallback so the rail isn't empty.
                if (_showDefaultFallback)
                    list.Add(BuildExpressionButton(AgentExpressionKey.Default));
                return;
            }

            // Deduplicate by key — the SO is a list of (key, texture) entries,
            // so a careless author could double up; render each key at most once.
            var seen = new HashSet<AgentExpressionKey>();
            foreach (var entry in set.Expressions)
            {
                if (entry.Texture == null) continue;
                if (!seen.Add(entry.Key)) continue;
                list.Add(BuildExpressionButton(entry.Key));
            }
        }

        private Button BuildExpressionButton(AgentExpressionKey key)
        {
            var btn = new Button(() => OnExpressionClicked(key))
            {
                text = AgentExpressionLabels.ToKorean(key),
            };
            btn.AddToClassList(BtnClass);
            btn.userData = key;

            // Pre-select whichever pill matches the applier's current
            // expression so the rail reflects state, not a guess.
            if (_applier != null && _applier.CurrentExpression == key)
            {
                btn.AddToClassList(BtnSelectedClass);
                _selectedExpressionBtn = btn;
            }
            return btn;
        }

        private void BuildAnimationRail()
        {
            var list = _view.AnimationRailList;
            if (list == null) return;
            list.Clear();
            _selectedAnimationBtn = null;

            foreach (var entry in _animations)
            {
                if (entry == null) continue;
                var local = entry;
                var btn = new Button(() => OnAnimationClicked(local))
                {
                    text = local.Label ?? string.Empty,
                };
                btn.AddToClassList(BtnClass);
                btn.userData = local;
                list.Add(btn);
            }
        }

        private void BuildHairColorRail()
        {
            var list = _view.HairColorList;
            if (list == null) return;
            list.Clear();
            _selectedHairColorBtn = null;

            // First-entry fallback. When nothing is applied yet AND no preset
            // matches the applier's seed colour, we still highlight the first
            // swatch so the rail never reads as "nothing selected".
            Button firstBtn = null;

            foreach (var entry in _hairColors)
            {
                if (entry == null) continue;
                var local = entry;
                var btn = new Button(() => OnHairColorClicked(local)) { text = string.Empty };
                btn.AddToClassList(HairSwatchClass);
                btn.tooltip = local.Label;
                btn.userData = local;
                btn.style.backgroundColor = local.Color;
                list.Add(btn);
                if (firstBtn == null) firstBtn = btn;

                if (_applier != null
                    && _applier.CurrentHairColor.HasValue
                    && ApproximatelyEqual(_applier.CurrentHairColor.Value, local.Color))
                {
                    if (_selectedHairColorBtn != null)
                        _selectedHairColorBtn.RemoveFromClassList(HairSwatchSelectedClass);
                    btn.AddToClassList(HairSwatchSelectedClass);
                    _selectedHairColorBtn = btn;
                }
            }

            if (_selectedHairColorBtn == null && firstBtn != null)
            {
                firstBtn.AddToClassList(HairSwatchSelectedClass);
                _selectedHairColorBtn = firstBtn;
            }

            BindHairColorCustom();
        }

        // Wires the App UI ColorField docked at the end of the preset row.
        // The element itself isn't rebuilt across part-tab switches (it's a
        // static UXML child), so the callback is registered exactly once —
        // subsequent BuildHairColorRail calls are no-ops on this path.
        private void BindHairColorCustom()
        {
            if (_hairColorCustomField != null) return;
            var element = _view.HairColorCustom;
            if (element == null) return;
            _hairColorCustomField = element as ColorField;
            if (_hairColorCustomField == null)
            {
                Debug.LogWarning("[AgentPreviewActionRail] hair-color-custom is not an Unity.AppUI.UI.ColorField — free-pick wiring skipped.", this);
                return;
            }

            // Seed the field with the applier's current colour so opening the
            // picker shows whichever colour is already on the agent, not a
            // stale default.
            if (_applier != null && _applier.CurrentHairColor.HasValue)
                _hairColorCustomField.SetValueWithoutNotify(_applier.CurrentHairColor.Value);

            _hairColorCustomCallback = OnHairColorCustomChanged;
            _hairColorCustomField.RegisterValueChangedCallback(_hairColorCustomCallback);

            // Live preview while dragging — App UI fires ChangingEvent<Color>
            // on every picker tick before the final ChangeEvent<Color> on close.
            _hairColorCustomChangingCallback = OnHairColorCustomChanging;
            _hairColorCustomField.RegisterValueChangingCallback(_hairColorCustomChangingCallback);
        }

        private void OnHairColorCustomChanged(ChangeEvent<Color> evt)
        {
            ApplyFreePickHairColor(evt.newValue);
        }

        private void OnHairColorCustomChanging(ChangingEvent<Color> evt)
        {
            ApplyFreePickHairColor(evt.newValue);
        }

        // Shared path for the picker's live-drag (ChangingEvent) and final
        // (ChangeEvent) callbacks — both want to write the colour through and
        // drop preset highlights.
        private void ApplyFreePickHairColor(Color color)
        {
            if (_applier == null) return;
            _applier.SetHairColor(color);
            PushHairColorToViewModel(color);

            // Clear preset highlights — the chosen colour is free-pick, no
            // preset matches it exactly. (We don't try to auto-match against
            // presets because the picker generates arbitrary hex values.)
            if (_selectedHairColorBtn != null)
                _selectedHairColorBtn.RemoveFromClassList(HairSwatchSelectedClass);
            _selectedHairColorBtn = null;
        }

        private void OnHairColorClicked(HairColorEntry entry)
        {
            if (_applier == null)
            {
                Debug.LogWarning("[AgentPreviewActionRail] WardrobeApplier not assigned — hair colour click ignored.", this);
                return;
            }
            _applier.SetHairColor(entry.Color);
            PushHairColorToViewModel(entry.Color);
            HighlightHairColor(entry);
            // Keep the ColorField in sync so opening the picker afterwards
            // shows the preset colour instead of a stale value. SetValueWithoutNotify
            // avoids re-entering OnHairColorCustomChanged.
            if (_hairColorCustomField != null)
                _hairColorCustomField.SetValueWithoutNotify(entry.Color);
        }

        // ViewModel 동기화 — preview applier 의 시각 반영과 별개로, Save 시점에 ViewModel.Wardrobe.HairColor
        // 가 그대로 record.wardrobe.hairColor 로 흘러가도록 한다. _view 또는 ViewModel 이 아직 준비되지 않은
        // 초기 프레임에는 silent skip (다음 클릭에서 자연 복구).
        private void PushHairColorToViewModel(Color color)
        {
            var vm = _view != null ? _view.ViewModel : null;
            if (vm == null) return;
            vm.SetHairColor("#" + ColorUtility.ToHtmlStringRGBA(color));
        }

        // Highlights the swatch whose userData matches `entry`.
        private void HighlightHairColor(HairColorEntry entry)
        {
            if (_selectedHairColorBtn != null)
                _selectedHairColorBtn.RemoveFromClassList(HairSwatchSelectedClass);
            _selectedHairColorBtn = null;

            var list = _view.HairColorList;
            if (list == null) return;
            foreach (var child in list.Children())
            {
                if (child is Button btn && ReferenceEquals(btn.userData, entry))
                {
                    btn.AddToClassList(HairSwatchSelectedClass);
                    _selectedHairColorBtn = btn;
                    return;
                }
            }
        }

        // Inspector colour values round-trip through serialisation as floats,
        // so direct == comparison with the literal Hex() output occasionally
        // misses by a 1/255 rounding error. Compare per-channel with a small
        // epsilon to keep "this is the swatch you just clicked" reliable.
        private static bool ApproximatelyEqual(Color a, Color b)
        {
            const float Eps = 0.004f; // ≈ 1/255
            return Mathf.Abs(a.r - b.r) < Eps
                && Mathf.Abs(a.g - b.g) < Eps
                && Mathf.Abs(a.b - b.b) < Eps;
        }

        private void OnExpressionClicked(AgentExpressionKey key)
        {
            if (_applier == null)
            {
                Debug.LogWarning("[AgentPreviewActionRail] WardrobeApplier not assigned — expression click ignored.", this);
                return;
            }
            _applier.SetEyeExpression(key);
            HighlightExpression(key);
        }

        private void OnAnimationClicked(AnimationEntry entry)
        {
            if (_previewAnimator == null)
            {
                Debug.LogWarning("[AgentPreviewActionRail] Preview Animator not assigned — animation click ignored.", this);
                return;
            }
            if (string.IsNullOrEmpty(entry.StateName)) return;

            // Animator.Play() accepts both state names and clip names; we rely
            // on that flexibility so authors can wire clips directly without
            // setting up an AnimatorController state machine.
            _previewAnimator.Play(entry.StateName, entry.Layer, 0f);
            HighlightAnimation(entry);
        }

        private void HighlightExpression(AgentExpressionKey key)
        {
            if (_selectedExpressionBtn != null)
                _selectedExpressionBtn.RemoveFromClassList(BtnSelectedClass);
            _selectedExpressionBtn = null;
            var list = _view.ExpressionRailList;
            if (list == null) return;
            foreach (var child in list.Children())
            {
                if (child is Button btn && btn.userData is AgentExpressionKey k && k == key)
                {
                    btn.AddToClassList(BtnSelectedClass);
                    _selectedExpressionBtn = btn;
                    return;
                }
            }
        }

        private void HighlightAnimation(AnimationEntry entry)
        {
            if (_selectedAnimationBtn != null)
                _selectedAnimationBtn.RemoveFromClassList(BtnSelectedClass);
            _selectedAnimationBtn = null;
            var list = _view.AnimationRailList;
            if (list == null) return;
            foreach (var child in list.Children())
            {
                if (child is Button btn && ReferenceEquals(btn.userData, entry))
                {
                    btn.AddToClassList(BtnSelectedClass);
                    _selectedAnimationBtn = btn;
                    return;
                }
            }
        }
    }
}
