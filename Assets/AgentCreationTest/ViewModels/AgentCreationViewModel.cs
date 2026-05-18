using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentCreationTest.Common;
using AgentCreationTest.Models;

namespace AgentCreationTest.ViewModels
{
    public sealed class AgentCreationViewModel : ObservableObject
    {
        public const int FirstStep = 1;
        public const int LastStep = 5;
        public const int MaxTraits = 3;

        public static readonly IReadOnlyList<string> TraitPool = new[]
        {
            "차분한", "활기찬", "꼼꼼한", "느긋한", "재치있는", "진지한",
            "친근한", "정중한", "창의적인", "논리적인", "공감하는", "분석적인",
        };

        public static readonly IReadOnlyList<ModelOption> ModelOptions = new[]
        {
            new ModelOption("claude-sonnet-4-6", "Claude Sonnet 4.6", "균형잡힌 기본 모델", false, "현재 사용 중"),
            new ModelOption("claude-opus-4-7",   "Claude Opus 4.7",   "가장 강력한 모델",   true,  null),
            new ModelOption("claude-haiku-4-5",  "Claude Haiku 4.5",  "가장 빠른 모델",     true,  null),
        };

        private readonly ObservableCollection<string> _traits = new ObservableCollection<string>();
        private int _step = FirstStep;
        private string _name = string.Empty;
        private string _role = string.Empty;
        private Wardrobe _wardrobe = Wardrobe.Default;
        private string _modelId = ModelOptions[0].Id;
        private bool _isAddFlow;

        // ─── Committed preview mirror ──────────────────────────
        // 라이브 입력(Name/Role/Traits)은 사용자가 타이핑/선택할 때마다 갱신되지만,
        // 좌측 미리보기 카드에는 "다음" 버튼을 누른 시점에만 반영된다.
        // Wardrobe는 committed에 포함되지 않는다 — 의상 클릭은 즉시 캐릭터에 반영되어야 하므로.
        private string _committedName = string.Empty;
        private string _committedRole = string.Empty;
        private IReadOnlyList<string> _committedTraits = Array.Empty<string>();

        public event Action<AgentDraft> AgentCompleted;
        public event Action OptionCountsChanged;

        // 한 번의 Commit() 호출마다 발행 — View 가 카드/캐릭터에 띠링 펄스를 재생할 신호.
        public event Action PreviewCommitted;

        // External resolver (set by AgentPreviewBinder once the catalogue loads)
        // tells the View how many cells to render per slot. Falls back to the
        // 2D-preview palette size (9) so the wizard renders sensibly even before
        // a catalogue is wired.
        public Func<WardrobePart, int> OptionCountResolver { get; private set; }

        public int GetOptionCount(WardrobePart part)
        {
            if (OptionCountResolver != null)
            {
                int n = OptionCountResolver(part);
                if (n > 0) return n;
            }
            return AgentPalette.OptionCount;
        }

        public void SetOptionCountResolver(Func<WardrobePart, int> resolver)
        {
            OptionCountResolver = resolver;
            OptionCountsChanged?.Invoke();
        }

        public int Step
        {
            get => _step;
            private set
            {
                if (SetField(ref _step, value))
                {
                    Raise(nameof(StepLabel));
                    Raise(nameof(IsFirstStep));
                    Raise(nameof(NextLabel));
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public bool IsFirstStep => _step == FirstStep;
        public bool IsLastStep => _step == LastStep;

        public string Title => _isAddFlow ? "새 동료 만들기" : "첫 동료를 만들어요";

        public string StepLabel => _isAddFlow ? $"{_step}/{LastStep}" : $"4/4 · {_step}/{LastStep}";

        public string NextLabel => IsLastStep ? "동료 만들기" : "다음";

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value ?? string.Empty))
                {
                    // PreviewName 은 commit 시점에만 갱신 — 라이브 타이핑이 미리보기에 새지 않게 한다.
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public string Role
        {
            get => _role;
            set
            {
                if (SetField(ref _role, value ?? string.Empty))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public ObservableCollection<string> Traits => _traits;

        public Wardrobe Wardrobe
        {
            get => _wardrobe;
            private set
            {
                if (!ReferenceEquals(_wardrobe, value))
                {
                    _wardrobe = value;
                    Raise(nameof(Wardrobe));
                }
            }
        }

        public string ModelId
        {
            get => _modelId;
            set
            {
                if (SetField(ref _modelId, value))
                {
                    Raise(nameof(CanAdvance));
                }
            }
        }

        public string PreviewName =>
            string.IsNullOrWhiteSpace(_committedName) ? "..." : _committedName;

        public string PreviewRole =>
            string.IsNullOrWhiteSpace(_committedRole) ? "아직 정해지지 않았어요" : _committedRole;

        public IReadOnlyList<string> PreviewTraits => _committedTraits;

        // 미리보기는 두 영역으로 쪼개진다 — 캐릭터 머리 위 이름/역할 버블, 캐릭터 밑 트레잇 칩.
        // 각 영역은 그쪽 데이터가 commit 되어 있을 때만 표시된다.
        public bool HasCommittedNameOrRole =>
            !string.IsNullOrWhiteSpace(_committedName)
            || !string.IsNullOrWhiteSpace(_committedRole);

        public bool HasCommittedTraits => _committedTraits.Count > 0;

        public bool CanAdvance
        {
            get
            {
                switch (_step)
                {
                    case 1: return !string.IsNullOrWhiteSpace(_name);
                    case 2: return !string.IsNullOrWhiteSpace(_role) && _role.Trim().Length >= 2;
                    case 3: return _traits.Count > 0;
                    case 4: return true;
                    case 5: return !string.IsNullOrEmpty(_modelId);
                    default: return false;
                }
            }
        }

        public void SetIsAddFlow(bool isAddFlow)
        {
            if (_isAddFlow == isAddFlow) return;
            _isAddFlow = isAddFlow;
            Raise(nameof(Title));
            Raise(nameof(StepLabel));
        }

        public void GoNext()
        {
            if (!CanAdvance) return;

            // "다음" 클릭은 항상 commit. 라이브 입력이 미리보기 카드에 반영되는 유일한 시점이다.
            // GoBack 은 일부러 commit 하지 않는다 — 사용자가 되돌아가는 동안에도 마지막 commit 상태가 유지되어야 한다.
            Commit();

            if (IsLastStep)
            {
                AgentCompleted?.Invoke(new AgentDraft(
                    _name.Trim(), _role.Trim(), _traits.ToArray(), _wardrobe, _modelId));
                return;
            }
            Step = _step + 1;
        }

        // 라이브 입력 → committed 미러로 복사하고 미리보기 변경 알림을 한 번에 발행한다.
        // SetField 가 아닌 Raise 를 쓰는 이유: commit 마다 ding 펄스를 재생해야 하므로,
        // 값이 동일해 변경이 없는 경우에도 항상 알림이 가야 한다.
        private void Commit()
        {
            _committedName = _name?.Trim() ?? string.Empty;
            _committedRole = _role?.Trim() ?? string.Empty;
            _committedTraits = _traits.ToArray();

            Raise(nameof(PreviewName));
            Raise(nameof(PreviewRole));
            Raise(nameof(PreviewTraits));
            Raise(nameof(HasCommittedNameOrRole));
            Raise(nameof(HasCommittedTraits));
            PreviewCommitted?.Invoke();
        }

        public void GoBack()
        {
            if (IsFirstStep) return;
            Step = _step - 1;
        }

        public bool ToggleTrait(string trait)
        {
            if (string.IsNullOrWhiteSpace(trait)) return false;
            if (_traits.Contains(trait))
            {
                _traits.Remove(trait);
                Raise(nameof(CanAdvance));
                return true;
            }
            if (_traits.Count >= MaxTraits) return false;
            _traits.Add(trait);
            Raise(nameof(CanAdvance));
            return true;
        }

        public bool TryAddCustomTrait(string trait)
        {
            var trimmed = (trait ?? string.Empty).Trim();
            if (trimmed.Length == 0) return false;
            if (_traits.Count >= MaxTraits) return false;
            if (_traits.Contains(trimmed)) return false;
            _traits.Add(trimmed);
            Raise(nameof(CanAdvance));
            return true;
        }

        public void SetWardrobePart(WardrobePart part, int index)
        {
            Wardrobe = _wardrobe.With(part, index);
        }

        // 머리 색상 — "#RRGGBB" 또는 "#RRGGBBAA". null/empty 면 "사용자가 색 안 골랐음" 상태로 되돌림.
        // 위저드 View(AgentPreviewActionRail) 가 swatch 클릭/ColorField 변경 시 Color→hex 변환 후 호출.
        public void SetHairColor(string hexColor)
        {
            Wardrobe = _wardrobe.WithHairColor(hexColor);
        }

        public void RandomizeWardrobe(System.Random random = null)
        {
            // Random must roll inside the *catalog's* option count, not the
            // 2D-preview palette. The palette is fixed at 9 but the live 3D
            // catalog may ship fewer entries per slot — rolling 0..8 against
            // a 3-option eye catalog lands on indices 3..8 which fall through
            // to the catalog's DefaultOption (no expression set), breaking
            // the expression rail. GetOptionCount honours OptionCountResolver
            // when the catalog has been wired, else falls back to the palette.
            random = random ?? new System.Random();

            int Pick(WardrobePart part)
            {
                int count = GetOptionCount(part);
                return count > 0 ? random.Next(count) : 0;
            }

            Wardrobe = new Wardrobe(
                Pick(WardrobePart.Skin),
                Pick(WardrobePart.Hair),
                Pick(WardrobePart.Eyes),
                Pick(WardrobePart.Mouth),
                Pick(WardrobePart.Top),
                Pick(WardrobePart.Bottom),
                Pick(WardrobePart.Shoes));
        }

        public sealed class ModelOption
        {
            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public bool IsLocked { get; }
            public string Badge { get; }

            public ModelOption(string id, string name, string description, bool isLocked, string badge)
            {
                Id = id;
                Name = name;
                Description = description;
                IsLocked = isLocked;
                Badge = badge;
            }
        }
    }
}
