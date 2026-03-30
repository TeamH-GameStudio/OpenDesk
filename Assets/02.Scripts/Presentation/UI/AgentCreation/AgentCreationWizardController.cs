using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.AgentCreation
{
    /// <summary>
    /// 에이전트 제작 온보딩 위저드 컨트롤러.
    /// 좌측: 캐릭터 프리뷰 (항상 표시)
    /// 우측: Step별 패널 (1 Step = 1 Panel)
    /// 하단: 공통 이전/다음 버튼 + Progress Bar
    ///
    /// 완료 시 PlayerPrefs 저장 → 오피스 씬 로드.
    /// </summary>
    public class AgentCreationWizardController : MonoBehaviour
    {
        private const int TotalSteps = 6;
        private const string OfficeSceneName = "AgentOfficeScene";

        // ── 루트 ────────────────────────────────────────────
        [Header("루트")]
        [SerializeField] private GameObject _wizardRoot;

        // ── 좌측: 캐릭터 프리뷰 ─────────────────────────────
        [Header("좌측 - 캐릭터 프리뷰")]
        [SerializeField] private GameObject _characterPanel;
        [SerializeField] private Image _characterPreviewImage;
        [SerializeField] private TMP_Text _characterNameLabel;

        // ── 우측: Step 패널들 ───────────────────────────────
        [Header("우측 - Step 패널")]
        [SerializeField] private GameObject _stepNamePanel;      // Step 1
        [SerializeField] private GameObject _stepRolePanel;      // Step 2
        [SerializeField] private GameObject _stepModelPanel;     // Step 3 (AI 모델)
        [SerializeField] private GameObject _stepTonePanel;      // Step 4
        [SerializeField] private GameObject _stepAvatarPanel;    // Step 5 (3D 아바타)
        [SerializeField] private GameObject _stepConfirmPanel;   // Step 6

        // ── 공통 UI ────────────────────────────────────────
        [Header("공통")]
        [SerializeField] private TMP_Text _stepTitleText;
        [SerializeField] private TMP_Text _stepDescText;
        [SerializeField] private TMP_Text _stepCountText;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private Button _prevButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private TMP_Text _nextButtonText;

        // ── Step 1: 이름 입력 ───────────────────────────────
        [Header("Step 1 - 이름")]
        [SerializeField] private TMP_InputField _nameInput;
        [SerializeField] private TMP_Text _nameValidationText;

        // ── Step 2: 역할 선택 (칩 버튼) ─────────────────────
        [Header("Step 2 - 역할")]
        [SerializeField] private RoleChipButton[] _roleChips;

        // ── Step 3: AI 모델 선택 (카드) ─────────────────────
        [Header("Step 3 - AI 모델")]
        [SerializeField] private ModelCard[] _modelCards;

        // ── Step 4: 말투 선택 ───────────────────────────────
        [Header("Step 4 - 말투")]
        [SerializeField] private ToneChipButton[] _toneChips;

        // ── Step 5: 아바타(3D 모델) 선택 ────────────────────
        [Header("Step 5 - 아바타")]
        [SerializeField] private AvatarCard[] _avatarCards;

        // ── Step 6: 최종 확인 ───────────────────────────────
        [Header("Step 6 - 확인")]
        [SerializeField] private TMP_Text _confirmNameText;
        [SerializeField] private TMP_Text _confirmRoleText;
        [SerializeField] private TMP_Text _confirmModelText;
        [SerializeField] private TMP_Text _confirmToneText;
        [SerializeField] private TMP_Text _confirmAvatarText;
        [SerializeField] private Button _createButton;
        [SerializeField] private GameObject _creatingOverlay;
        [SerializeField] private TMP_Text _creatingStatusText;

        // ── 상태 ────────────────────────────────────────────
        private AgentCreationStep _currentStep = AgentCreationStep.Hidden;
        private readonly AgentCreationData _data = new();
        private GameObject[] _allStepPanels;

        /// <summary>에이전트 생성 완료 시 발행</summary>
        public event Action<AgentCreationData> OnAgentCreated;

        private static readonly Dictionary<AgentRole, string> RoleDisplayNames = new()
        {
            { AgentRole.Planning,    "기획" },
            { AgentRole.Development, "개발" },
            { AgentRole.Design,      "디자인" },
            { AgentRole.Legal,       "법률" },
            { AgentRole.Marketing,   "마케팅" },
            { AgentRole.Research,    "리서치" },
            { AgentRole.Support,     "고객지원" },
            { AgentRole.Finance,     "재무" },
        };

        private static readonly Dictionary<AgentAIModel, (string name, string desc)> ModelDisplayInfo = new()
        {
            { AgentAIModel.GPT4o,        ("GPT-4o",            "가장 똑똑하고 다재다능해요") },
            { AgentAIModel.ClaudeSonnet,  ("Claude 3.5 Sonnet", "코딩과 글쓰기에 탁월해요") },
            { AgentAIModel.GeminiPro,     ("Gemini 1.5 Pro",    "방대한 데이터를 빠르게 처리해요") },
        };

        private static readonly Dictionary<AgentTone, string> ToneDisplayNames = new()
        {
            { AgentTone.Friendly, "친절한" },
            { AgentTone.Logical,  "논리적인" },
            { AgentTone.Humorous, "유머러스한" },
            { AgentTone.Formal,   "격식체" },
            { AgentTone.Casual,   "편안한" },
        };

        // ================================================================
        //  초기화
        // ================================================================

        private void Start()
        {
            _allStepPanels = new[]
            {
                _stepNamePanel, _stepRolePanel, _stepModelPanel,
                _stepTonePanel, _stepAvatarPanel, _stepConfirmPanel
            };

            BindButtons();
            DisableNonClaudeModels();
            TransitionTo(AgentCreationStep.NameInput);
        }

        /// <summary>Claude 외 모델 카드 비활성화 + Claude 자동 선택</summary>
        private void DisableNonClaudeModels()
        {
            if (_modelCards == null) return;
            foreach (var card in _modelCards)
            {
                if (card.Model == AgentAIModel.ClaudeSonnet) continue;

                // 비활성화 (회색 처리 + 클릭 불가)
                if (card.Button != null)
                {
                    card.Button.interactable = false;
                    var colors = card.Button.colors;
                    colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                    card.Button.colors = colors;
                }
                if (card.RecommendTag != null) card.RecommendTag.SetActive(false);
                if (card.DescText != null) card.DescText.text = "(준비 중)";
            }

            // Claude 자동 선택
            _data.AIModel = AgentAIModel.ClaudeSonnet;

            // 아바타 카드 1개만 있으면 자동 선택
            if (_avatarCards != null && _avatarCards.Length == 1)
                _data.AvatarPrefabName = _avatarCards[0].PrefabName;
        }

        private void BindButtons()
        {
            _prevButton?.onClick.AddListener(GoPrevious);
            _nextButton?.onClick.AddListener(GoNext);

            // Step 1
            _nameInput?.onValueChanged.AddListener(OnNameChanged);

            // Step 2
            if (_roleChips != null)
                foreach (var chip in _roleChips)
                    if (chip.Button != null)
                    {
                        var role = chip.Role;
                        chip.Button.onClick.AddListener(() => SelectRole(role));
                    }

            // Step 3
            if (_modelCards != null)
                foreach (var card in _modelCards)
                    if (card.Button != null)
                    {
                        var model = card.Model;
                        card.Button.onClick.AddListener(() => SelectModel(model));
                    }

            // Step 4
            if (_toneChips != null)
                foreach (var chip in _toneChips)
                    if (chip.Button != null)
                    {
                        var tone = chip.Tone;
                        chip.Button.onClick.AddListener(() => SelectTone(tone));
                    }

            // Step 5
            if (_avatarCards != null)
                foreach (var card in _avatarCards)
                    if (card.Button != null)
                    {
                        var prefabName = card.PrefabName;
                        card.Button.onClick.AddListener(() => SelectAvatar(prefabName));
                    }

            // Step 6
            _createButton?.onClick.AddListener(() => CreateAgent().Forget());
        }

        // ================================================================
        //  Step 전환
        // ================================================================

        private void TransitionTo(AgentCreationStep step)
        {
            _currentStep = step;
            Debug.Log($"[AgentWizard] → {step}");

            HideAllPanels();

            if (step == AgentCreationStep.Hidden)
            {
                if (_wizardRoot != null) _wizardRoot.SetActive(false);
                return;
            }

            if (_wizardRoot != null) _wizardRoot.SetActive(true);

            var (title, desc, stepNum, panel) = GetStepContent(step);

            if (_stepTitleText != null) _stepTitleText.text = title;
            if (_stepDescText != null)  _stepDescText.text = desc;
            if (_stepCountText != null) _stepCountText.text = stepNum > 0 ? $"Step {stepNum} / {TotalSteps}" : "";
            if (_progressBar != null)   _progressBar.value = (float)stepNum / TotalSteps;

            if (panel != null) panel.SetActive(true);

            UpdateNavigationButtons(step);
            OnStepEnter(step);
        }

        private void UpdateNavigationButtons(AgentCreationStep step)
        {
            if (_prevButton != null)
                _prevButton.gameObject.SetActive(step != AgentCreationStep.NameInput);

            if (_nextButton != null)
                _nextButton.gameObject.SetActive(step != AgentCreationStep.Confirm);

            if (_nextButtonText != null)
                _nextButtonText.text = "다음 ▶";

            UpdateNextButtonInteractable();
        }

        private void UpdateNextButtonInteractable()
        {
            if (_nextButton == null) return;

            _nextButton.interactable = _currentStep switch
            {
                AgentCreationStep.NameInput    => !string.IsNullOrWhiteSpace(_data.AgentName),
                AgentCreationStep.RoleSelect   => _data.Role != AgentRole.None,
                AgentCreationStep.ModelSelect  => _data.AIModel != AgentAIModel.None,
                AgentCreationStep.ToneSelect   => _data.Tone != AgentTone.None,
                AgentCreationStep.AvatarSelect => !string.IsNullOrWhiteSpace(_data.AvatarPrefabName),
                _ => true
            };
        }

        private void OnStepEnter(AgentCreationStep step)
        {
            switch (step)
            {
                case AgentCreationStep.NameInput:
                    if (_nameInput != null) _nameInput.text = _data.AgentName;
                    UpdateCharacterPreview();
                    break;
                case AgentCreationStep.RoleSelect:
                    HighlightSelectedRole();
                    break;
                case AgentCreationStep.ModelSelect:
                    HighlightSelectedModel();
                    break;
                case AgentCreationStep.ToneSelect:
                    HighlightSelectedTone();
                    break;
                case AgentCreationStep.AvatarSelect:
                    HighlightSelectedAvatar();
                    break;
                case AgentCreationStep.Confirm:
                    PopulateConfirmPanel();
                    break;
            }
        }

        private (string title, string desc, int stepNum, GameObject panel) GetStepContent(AgentCreationStep step)
        {
            return step switch
            {
                AgentCreationStep.NameInput => (
                    "이름 정하기",
                    "함께 일할 에이전트의 이름을 정해주세요.",
                    1, _stepNamePanel),
                AgentCreationStep.RoleSelect => (
                    "역할 부여하기",
                    "이 에이전트는 어떤 전문 분야를 갖게 될까요?",
                    2, _stepRolePanel),
                AgentCreationStep.ModelSelect => (
                    "AI 모델 선택",
                    "에이전트의 두뇌가 될 AI 모델을 골라주세요.",
                    3, _stepModelPanel),
                AgentCreationStep.ToneSelect => (
                    "말투 설정",
                    "어떤 말투로 대화하고 싶으신가요?",
                    4, _stepTonePanel),
                AgentCreationStep.AvatarSelect => (
                    "외형 선택",
                    "사무실에서 어떤 모습으로 보일까요?",
                    5, _stepAvatarPanel),
                AgentCreationStep.Confirm => (
                    "최종 확인",
                    "설정을 확인하고 에이전트를 생성합니다.",
                    6, _stepConfirmPanel),
                _ => ("", "", 0, null)
            };
        }

        // ================================================================
        //  네비게이션
        // ================================================================

        private void GoNext()
        {
            var next = _currentStep switch
            {
                AgentCreationStep.NameInput    => AgentCreationStep.RoleSelect,
                AgentCreationStep.RoleSelect   => AgentCreationStep.ModelSelect,
                AgentCreationStep.ModelSelect  => AgentCreationStep.ToneSelect,
                AgentCreationStep.ToneSelect   => AgentCreationStep.AvatarSelect,
                AgentCreationStep.AvatarSelect => AgentCreationStep.Confirm,
                _ => _currentStep
            };

            if (next != _currentStep) TransitionTo(next);
        }

        private void GoPrevious()
        {
            var prev = _currentStep switch
            {
                AgentCreationStep.RoleSelect   => AgentCreationStep.NameInput,
                AgentCreationStep.ModelSelect  => AgentCreationStep.RoleSelect,
                AgentCreationStep.ToneSelect   => AgentCreationStep.ModelSelect,
                AgentCreationStep.AvatarSelect => AgentCreationStep.ToneSelect,
                AgentCreationStep.Confirm      => AgentCreationStep.AvatarSelect,
                _ => _currentStep
            };

            if (prev != _currentStep) TransitionTo(prev);
        }

        // ================================================================
        //  Step 1: 이름
        // ================================================================

        private void OnNameChanged(string value)
        {
            _data.AgentName = value.Trim();

            if (_nameValidationText != null)
            {
                if (string.IsNullOrWhiteSpace(value))
                    _nameValidationText.text = "이름을 입력해주세요.";
                else if (value.Trim().Length < 2)
                    _nameValidationText.text = "2글자 이상 입력해주세요.";
                else
                    _nameValidationText.text = "";
            }

            UpdateCharacterPreview();
            UpdateNextButtonInteractable();
        }

        private void UpdateCharacterPreview()
        {
            if (_characterNameLabel != null)
                _characterNameLabel.text = string.IsNullOrWhiteSpace(_data.AgentName) ? "???" : _data.AgentName;
        }

        // ================================================================
        //  Step 2: 역할
        // ================================================================

        private void SelectRole(AgentRole role)
        {
            _data.Role = role;
            HighlightSelectedRole();
            UpdateNextButtonInteractable();
        }

        private void HighlightSelectedRole()
        {
            if (_roleChips == null) return;
            foreach (var chip in _roleChips)
                SetChipHighlight(chip.Button, chip.SelectedIndicator, chip.Role == _data.Role);
        }

        // ================================================================
        //  Step 3: AI 모델
        // ================================================================

        private void SelectModel(AgentAIModel model)
        {
            _data.AIModel = model;
            HighlightSelectedModel();
            UpdateNextButtonInteractable();
        }

        private void HighlightSelectedModel()
        {
            if (_modelCards == null) return;
            foreach (var card in _modelCards)
                SetChipHighlight(card.Button, card.SelectedIndicator, card.Model == _data.AIModel);
        }

        // ================================================================
        //  Step 4: 말투
        // ================================================================

        private void SelectTone(AgentTone tone)
        {
            _data.Tone = tone;
            HighlightSelectedTone();
            UpdateNextButtonInteractable();
        }

        private void HighlightSelectedTone()
        {
            if (_toneChips == null) return;
            foreach (var chip in _toneChips)
                SetChipHighlight(chip.Button, chip.SelectedIndicator, chip.Tone == _data.Tone);
        }

        // ================================================================
        //  Step 5: 아바타
        // ================================================================

        private void SelectAvatar(string prefabName)
        {
            _data.AvatarPrefabName = prefabName;
            Debug.Log($"[AgentWizard] 아바타 선택: {prefabName}");
            HighlightSelectedAvatar();
            UpdateNextButtonInteractable();
        }

        private void HighlightSelectedAvatar()
        {
            if (_avatarCards == null) return;
            foreach (var card in _avatarCards)
                SetChipHighlight(card.Button, card.SelectedIndicator, card.PrefabName == _data.AvatarPrefabName);
        }

        // ================================================================
        //  Step 6: 최종 확인 + 생성
        // ================================================================

        private void PopulateConfirmPanel()
        {
            if (_confirmNameText != null)
                _confirmNameText.text = _data.AgentName;
            if (_confirmRoleText != null)
                _confirmRoleText.text = RoleDisplayNames.GetValueOrDefault(_data.Role, "-");
            if (_confirmModelText != null)
                _confirmModelText.text = ModelDisplayInfo.TryGetValue(_data.AIModel, out var info) ? info.name : "-";
            if (_confirmToneText != null)
                _confirmToneText.text = ToneDisplayNames.GetValueOrDefault(_data.Tone, "-");
            if (_confirmAvatarText != null)
                _confirmAvatarText.text = string.IsNullOrWhiteSpace(_data.AvatarPrefabName) ? "-" : _data.AvatarPrefabName;

            if (_createButton != null) _createButton.interactable = true;
            if (_creatingOverlay != null) _creatingOverlay.SetActive(false);
        }

        private async UniTaskVoid CreateAgent()
        {
            if (!_data.IsValid)
            {
                Debug.LogWarning("[AgentWizard] 데이터 유효하지 않음");
                return;
            }

            Debug.Log($"[AgentWizard] 에이전트 생성 시작: {_data.AgentName}");

            if (_createButton != null) _createButton.interactable = false;
            if (_creatingOverlay != null) _creatingOverlay.SetActive(true);

            // 생성 연출
            var messages = new[]
            {
                "설정하신 두뇌와 외형을 연결하고 있어요...",
                "에이전트를 초기화하고 있어요...",
                "거의 다 됐어요!",
            };

            foreach (var msg in messages)
            {
                if (_creatingStatusText != null) _creatingStatusText.text = msg;
                await UniTask.Delay(800, cancellationToken: destroyCancellationToken);
            }

            // PlayerPrefs 저장
            AgentDataStore.Save(_data, _data.AvatarPrefabName);

            OnAgentCreated?.Invoke(_data);

            if (_creatingStatusText != null)
                _creatingStatusText.text = $"'{_data.AgentName}' 에이전트가 생성되었습니다!\n사무실로 이동합니다...";

            await UniTask.Delay(1500, cancellationToken: destroyCancellationToken);

            // 오피스 씬으로 전환
            Debug.Log($"[AgentWizard] 오피스 씬 로드: {OfficeSceneName}");
            SceneManager.LoadScene(OfficeSceneName);
        }

        // ================================================================
        //  외부 API
        // ================================================================

        public void StartWizard()
        {
            _data.Reset();
            TransitionTo(AgentCreationStep.NameInput);
        }

        public AgentCreationData CurrentData => _data;

        // ================================================================
        //  유틸리티
        // ================================================================

        private void HideAllPanels()
        {
            if (_allStepPanels == null) return;
            foreach (var p in _allStepPanels)
                if (p != null) p.SetActive(false);
        }

        private static void SetChipHighlight(Button button, GameObject indicator, bool selected)
        {
            if (indicator != null) indicator.SetActive(selected);
            if (button != null)
            {
                var colors = button.colors;
                colors.normalColor = selected
                    ? new Color(0.2f, 0.6f, 1f, 1f)
                    : new Color(0.9f, 0.9f, 0.9f, 1f);
                button.colors = colors;
            }
        }

        // ================================================================
        //  Inspector용 직렬화 구조체
        // ================================================================

        [Serializable]
        public struct RoleChipButton
        {
            public AgentRole Role;
            public Button Button;
            public GameObject SelectedIndicator;
        }

        [Serializable]
        public struct ModelCard
        {
            public AgentAIModel Model;
            public Button Button;
            public GameObject SelectedIndicator;
            public TMP_Text NameText;
            public TMP_Text DescText;
            public GameObject RecommendTag;
        }

        [Serializable]
        public struct ToneChipButton
        {
            public AgentTone Tone;
            public Button Button;
            public GameObject SelectedIndicator;
        }

        [Serializable]
        public struct AvatarCard
        {
            public string PrefabName;       // 프리팹 에셋 이름 (Resources 로드용)
            public string DisplayName;      // UI 표시명
            public Button Button;
            public GameObject SelectedIndicator;
            public Image PreviewImage;      // 미리보기 이미지
        }
    }
}
