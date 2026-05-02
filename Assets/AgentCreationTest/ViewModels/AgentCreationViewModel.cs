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

        public event Action<AgentDraft> AgentCompleted;
        public event Action OptionCountsChanged;

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
                    Raise(nameof(PreviewName));
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
                    Raise(nameof(PreviewRole));
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

        public string PreviewName => string.IsNullOrWhiteSpace(_name) ? "..." : _name.Trim();

        public string PreviewRole => string.IsNullOrWhiteSpace(_role) ? "아직 정해지지 않았어요" : _role.Trim();

        public IReadOnlyList<string> PreviewTraits =>
            _traits.Count > 0 ? (IReadOnlyList<string>)_traits.ToArray() : new[] { "천천히 만들어가는 중" };

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
            if (IsLastStep)
            {
                AgentCompleted?.Invoke(new AgentDraft(
                    _name.Trim(), _role.Trim(), _traits.ToArray(), _wardrobe, _modelId));
                return;
            }
            Step = _step + 1;
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

        public void RandomizeWardrobe(Random random = null)
        {
            random = random ?? new Random();
            Wardrobe = new Wardrobe(
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount),
                random.Next(AgentPalette.OptionCount));
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
