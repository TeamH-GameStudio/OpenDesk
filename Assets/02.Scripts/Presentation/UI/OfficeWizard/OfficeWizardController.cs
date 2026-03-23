using System;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.OfficeWizard
{
    /// <summary>
    /// Office 최초 실행 환영 마법사 컨트롤러
    /// AI 모델 선택 → 채널 연결 → 첫 대화 → 완료
    /// 비전공자 친화 단계별 가이드
    /// </summary>
    public class OfficeWizardController : MonoBehaviour
    {
        // ── 마법사 루트 ──────────────────────────────────────
        [Header("마법사 루트")]
        [SerializeField] private GameObject _wizardOverlay;

        // ── 단계 패널 ────────────────────────────────────────
        [Header("단계 패널")]
        [SerializeField] private GameObject _welcomePanel;
        [SerializeField] private GameObject _modelChoicePanel;
        [SerializeField] private GameObject _ollamaSetupPanel;
        [SerializeField] private GameObject _apiKeySetupPanel;
        [SerializeField] private GameObject _channelSetupPanel;
        [SerializeField] private GameObject _testChatPanel;
        [SerializeField] private GameObject _completePanel;

        // ── 환영 패널 ────────────────────────────────────────
        [Header("환영")]
        [SerializeField] private Button _welcomeStartButton;

        // ── 모델 선택 패널 ───────────────────────────────────
        [Header("AI 모델 선택")]
        [SerializeField] private Button _freeModelButton;
        [SerializeField] private Button _apiKeyModelButton;
        [SerializeField] private Button _modelSkipButton;
        [SerializeField] private Button _modelDiffToggle;
        [SerializeField] private GameObject _modelDiffPanel;

        // ── Ollama 설정 패널 ─────────────────────────────────
        [Header("Ollama 설정")]
        [SerializeField] private TMP_Text _ollamaStatusText;
        [SerializeField] private Slider   _ollamaProgressSlider;
        [SerializeField] private Button   _ollamaNextButton;

        // ── API 키 설정 패널 ─────────────────────────────────
        [Header("API 키 설정")]
        [SerializeField] private Button _providerAnthropicBtn;
        [SerializeField] private Button _providerOpenAIBtn;
        [SerializeField] private Button _providerGoogleBtn;
        [SerializeField] private Button _providerOtherBtn;
        [SerializeField] private GameObject _apiKeyInputArea;
        [SerializeField] private TMP_Text _selectedProviderText;
        [SerializeField] private TMP_InputField _apiKeyInput;
        [SerializeField] private Button _apiKeyValidateBtn;
        [SerializeField] private TMP_Text _apiKeyStatusText;
        [SerializeField] private Button _apiKeyNextButton;

        // ── 채널 설정 패널 ───────────────────────────────────
        [Header("채널 설정")]
        [SerializeField] private Button _channelTelegramBtn;
        [SerializeField] private Button _channelDiscordBtn;
        [SerializeField] private Button _channelSlackBtn;
        [SerializeField] private Button _channelSkipButton;
        [SerializeField] private GameObject _channelTokenArea;
        [SerializeField] private TMP_Text _selectedChannelText;
        [SerializeField] private TMP_InputField _channelTokenInput;
        [SerializeField] private Button _channelConnectBtn;
        [SerializeField] private TMP_Text _channelStatusText;
        [SerializeField] private Button _channelNextButton;
        [SerializeField] private Button _channelDiffToggle;
        [SerializeField] private GameObject _channelDiffPanel;

        // ── 테스트 채팅 패널 ─────────────────────────────────
        [Header("테스트 채팅")]
        [SerializeField] private Button _suggestion1Btn;
        [SerializeField] private Button _suggestion2Btn;
        [SerializeField] private Button _suggestion3Btn;
        [SerializeField] private TMP_InputField _testChatInput;
        [SerializeField] private Button _testChatSendBtn;
        [SerializeField] private TMP_Text _testChatResponseText;
        [SerializeField] private Button _testChatDoneBtn;

        // ── 완료 패널 ────────────────────────────────────────
        [Header("완료")]
        [SerializeField] private TMP_Text _setupSummaryText;
        [SerializeField] private Button _finishButton;

        // ── 공통 ────────────────────────────────────────────
        [Header("공통")]
        [SerializeField] private TMP_Text _wizardTitleText;
        [SerializeField] private TMP_Text _wizardDescText;
        [SerializeField] private Slider   _wizardProgressBar;
        [SerializeField] private TMP_Text _wizardStepText;
        [SerializeField] private Button   _backButton;

        // ── DI ──────────────────────────────────────────────
        [Inject] private IApiKeyVaultService _vault;
        [Inject] private IChannelService     _channelService;
        [Inject] private IOpenClawBridgeService _bridge;

        // ── 상태 ────────────────────────────────────────────
        private OfficeWizardState _currentState = OfficeWizardState.Hidden;
        private readonly System.Collections.Generic.Stack<OfficeWizardState> _stateHistory = new();
        private GameObject[] _allPanels;
        private string _selectedProvider = "";
        private ChannelType _selectedChannelType;
        private string _setupModelName = "미설정";
        private string _setupChannelName = "미설정";
        private bool _modelDiffVisible;
        private bool _channelDiffVisible;

        private const string SetupDoneKey = "OpenDesk_OfficeSetupDone";

        // ================================================================
        //  초기화
        // ================================================================

        private void Start()
        {
            _allPanels = new[]
            {
                _welcomePanel, _modelChoicePanel, _ollamaSetupPanel,
                _apiKeySetupPanel, _channelSetupPanel, _testChatPanel, _completePanel
            };

            // 최초 실행 체크
            var isDone = PlayerPrefs.GetInt(SetupDoneKey, 0) == 1;
            if (isDone)
            {
                if (_wizardOverlay != null) _wizardOverlay.SetActive(false);
                Debug.Log("[Wizard] Office 설정 완료 상태 — 마법사 숨김");
                return;
            }

            Debug.Log("[Wizard] Office 최초 실행 — 환영 마법사 시작");
            BindButtons();
            TransitionTo(OfficeWizardState.Welcome);
        }

        private void BindButtons()
        {
            // 뒤로가기
            _backButton?.onClick.AddListener(GoBack);

            // 환영
            _welcomeStartButton?.onClick.AddListener(() => TransitionTo(OfficeWizardState.ModelChoice));

            // 모델 선택
            _freeModelButton?.onClick.AddListener(() => TransitionTo(OfficeWizardState.OllamaSetup));
            _apiKeyModelButton?.onClick.AddListener(() =>
            {
                if (_apiKeyInputArea != null) _apiKeyInputArea.SetActive(false);
                TransitionTo(OfficeWizardState.ApiKeySetup);
            });
            _modelSkipButton?.onClick.AddListener(() =>
            {
                _setupModelName = "나중에 설정";
                TransitionTo(OfficeWizardState.ChannelSetup);
            });
            _modelDiffToggle?.onClick.AddListener(() =>
            {
                _modelDiffVisible = !_modelDiffVisible;
                if (_modelDiffPanel != null) _modelDiffPanel.SetActive(_modelDiffVisible);
            });

            // Ollama
            _ollamaNextButton?.onClick.AddListener(() =>
            {
                _setupModelName = "Ollama (무료)";
                TransitionTo(OfficeWizardState.ChannelSetup);
            });

            // API 키 — 프로바이더 선택
            _providerAnthropicBtn?.onClick.AddListener(() => SelectProvider("anthropic", "Anthropic (Claude)"));
            _providerOpenAIBtn?.onClick.AddListener(() => SelectProvider("openai", "OpenAI (ChatGPT)"));
            _providerGoogleBtn?.onClick.AddListener(() => SelectProvider("google", "Google (Gemini)"));
            _providerOtherBtn?.onClick.AddListener(() => SelectProvider("deepseek", "DeepSeek"));

            _apiKeyValidateBtn?.onClick.AddListener(() => ValidateApiKey().Forget());
            _apiKeyNextButton?.onClick.AddListener(() =>
            {
                _setupModelName = _selectedProviderText?.text ?? "API 키";
                TransitionTo(OfficeWizardState.ChannelSetup);
            });

            // 채널
            _channelTelegramBtn?.onClick.AddListener(() => SelectChannel(ChannelType.Telegram, "Telegram"));
            _channelDiscordBtn?.onClick.AddListener(() => SelectChannel(ChannelType.Discord, "Discord"));
            _channelSlackBtn?.onClick.AddListener(() => SelectChannel(ChannelType.Slack, "Slack"));
            _channelSkipButton?.onClick.AddListener(() =>
            {
                _setupChannelName = "미설정";
                TransitionTo(OfficeWizardState.TestChat);
            });
            _channelConnectBtn?.onClick.AddListener(() => ConnectChannel().Forget());
            _channelNextButton?.onClick.AddListener(() => TransitionTo(OfficeWizardState.TestChat));
            _channelDiffToggle?.onClick.AddListener(() =>
            {
                _channelDiffVisible = !_channelDiffVisible;
                if (_channelDiffPanel != null) _channelDiffPanel.SetActive(_channelDiffVisible);
            });

            // 테스트 채팅
            _suggestion1Btn?.onClick.AddListener(() => SendTestMessage("안녕! 넌 뭘 할 수 있어?"));
            _suggestion2Btn?.onClick.AddListener(() => SendTestMessage("오늘 날씨 알려줘"));
            _suggestion3Btn?.onClick.AddListener(() => SendTestMessage("내 파일 정리해줘"));
            _testChatSendBtn?.onClick.AddListener(() =>
            {
                var msg = _testChatInput?.text ?? "";
                if (!string.IsNullOrWhiteSpace(msg)) SendTestMessage(msg);
            });
            _testChatDoneBtn?.onClick.AddListener(() => TransitionTo(OfficeWizardState.Complete));

            // 완료
            _finishButton?.onClick.AddListener(FinishWizard);
        }

        // ================================================================
        //  상태 전환
        // ================================================================

        private void TransitionTo(OfficeWizardState state)
        {
            // 히스토리 기록 (Hidden 제외, 같은 상태 연속 방지)
            if (_currentState != OfficeWizardState.Hidden && _currentState != state)
                _stateHistory.Push(_currentState);

            _currentState = state;
            Debug.Log($"[Wizard] → {state}");

            HideAllPanels();
            if (_modelDiffPanel != null) _modelDiffPanel.SetActive(false);
            if (_channelDiffPanel != null) _channelDiffPanel.SetActive(false);

            if (state == OfficeWizardState.Hidden)
            {
                if (_wizardOverlay != null) _wizardOverlay.SetActive(false);
                return;
            }

            if (_wizardOverlay != null) _wizardOverlay.SetActive(true);

            // 뒤로가기 버튼: Welcome/Hidden에서는 숨김
            if (_backButton != null)
                _backButton.gameObject.SetActive(
                    state != OfficeWizardState.Welcome &&
                    state != OfficeWizardState.Hidden &&
                    state != OfficeWizardState.Complete);

            var (title, desc, progress, stepText, panel) = GetStateContent(state);
            if (_wizardTitleText != null)    _wizardTitleText.text    = title;
            if (_wizardDescText != null)     _wizardDescText.text     = desc;
            if (_wizardProgressBar != null)  _wizardProgressBar.value = progress;
            if (_wizardStepText != null)     _wizardStepText.text     = stepText;
            if (panel != null)               panel.SetActive(true);
        }

        private void GoBack()
        {
            if (_stateHistory.Count == 0) return;
            var prev = _stateHistory.Pop();
            Debug.Log($"[Wizard] ← 뒤로: {prev}");

            // 히스토리에 현재 상태를 추가하지 않도록 직접 전환
            _currentState = prev;
            HideAllPanels();
            if (_modelDiffPanel != null) _modelDiffPanel.SetActive(false);
            if (_channelDiffPanel != null) _channelDiffPanel.SetActive(false);

            if (_backButton != null)
                _backButton.gameObject.SetActive(
                    prev != OfficeWizardState.Welcome && prev != OfficeWizardState.Hidden);

            var (title, desc, progress, stepText, panel) = GetStateContent(prev);
            if (_wizardTitleText != null)    _wizardTitleText.text    = title;
            if (_wizardDescText != null)     _wizardDescText.text     = desc;
            if (_wizardProgressBar != null)  _wizardProgressBar.value = progress;
            if (_wizardStepText != null)     _wizardStepText.text     = stepText;
            if (panel != null)               panel.SetActive(true);
        }

        private (string title, string desc, float progress, string step, GameObject panel) GetStateContent(
            OfficeWizardState state)
        {
            return state switch
            {
                OfficeWizardState.Welcome => (
                    "AI 비서 환경이 준비되었어요!",
                    "몇 가지만 더 설정하면 바로 사용할 수 있어요.\n언제든 설정에서 변경할 수 있으니 부담 없이 진행하세요.",
                    0.05f, "", _welcomePanel),

                OfficeWizardState.ModelChoice => (
                    "AI 비서의 '두뇌'를 선택해주세요",
                    "AI 비서가 생각하고 대답하는 데 사용할 AI 모델을 선택합니다.\n나중에 언제든 변경할 수 있어요.",
                    0.2f, "Step 1 / 4", _modelChoicePanel),

                OfficeWizardState.OllamaSetup => (
                    "무료 AI 모델 설정 중",
                    "내 컴퓨터에서 직접 AI를 실행합니다.\n인터넷 없이도 사용할 수 있어요!",
                    0.35f, "Step 1 / 4", _ollamaSetupPanel),

                OfficeWizardState.ApiKeySetup => (
                    "AI 서비스 연결",
                    "사용할 AI 서비스를 선택하고 API 키를 입력해주세요.\n각 서비스 사이트에서 무료로 발급받을 수 있어요.",
                    0.35f, "Step 1 / 4", _apiKeySetupPanel),

                OfficeWizardState.ChannelSetup => (
                    "대화 채널 연결 (선택)",
                    "이 프로그램 외에 평소 사용하는 메신저로도\nAI 비서와 대화할 수 있어요.\n설정하지 않아도 이 프로그램에서 직접 대화 가능!",
                    0.55f, "Step 2 / 4", _channelSetupPanel),

                OfficeWizardState.TestChat => (
                    "AI 비서에게 첫 인사를 해보세요!",
                    "아래 추천 메시지를 클릭하거나 직접 입력해보세요.\n어떤 말이든 좋아요!",
                    0.8f, "Step 3 / 4", _testChatPanel),

                OfficeWizardState.Complete => (
                    "모든 준비가 끝났어요!",
                    "설정은 언제든 상단 탭에서 변경할 수 있어요.",
                    1f, "Step 4 / 4", _completePanel),

                _ => ("", "", 0f, "", null),
            };
        }

        // ================================================================
        //  프로바이더 / 채널 선택 핸들러
        // ================================================================

        private void SelectProvider(string id, string displayName)
        {
            _selectedProvider = id;
            if (_selectedProviderText != null)
                _selectedProviderText.text = displayName;
            if (_apiKeyInputArea != null)
                _apiKeyInputArea.SetActive(true);
            if (_apiKeyStatusText != null)
                _apiKeyStatusText.text = "";
            if (_apiKeyNextButton != null)
                _apiKeyNextButton.gameObject.SetActive(false);
        }

        private async UniTaskVoid ValidateApiKey()
        {
            var key = _apiKeyInput?.text ?? "";
            if (string.IsNullOrWhiteSpace(key))
            {
                if (_apiKeyStatusText != null) _apiKeyStatusText.text = "키를 입력해주세요.";
                return;
            }

            if (_apiKeyStatusText != null) _apiKeyStatusText.text = "확인 중...";

            try
            {
                if (_vault != null)
                {
                    await _vault.SaveKeyAsync(_selectedProvider, key);
                    var status = await _vault.ValidateKeyAsync(_selectedProvider);
                    var valid = status == ApiKeyStatus.Valid;

                    if (_apiKeyStatusText != null)
                        _apiKeyStatusText.text = valid ? "✓ 키가 확인되었어요!" : "✗ 유효하지 않은 키예요. 다시 확인해주세요.";
                    if (_apiKeyNextButton != null)
                        _apiKeyNextButton.gameObject.SetActive(valid);
                }
                else
                {
                    // Mock 모드
                    await UniTask.Delay(1000);
                    if (_apiKeyStatusText != null) _apiKeyStatusText.text = "✓ 키가 확인되었어요! (Mock)";
                    if (_apiKeyNextButton != null) _apiKeyNextButton.gameObject.SetActive(true);
                }
            }
            catch (Exception ex)
            {
                if (_apiKeyStatusText != null) _apiKeyStatusText.text = $"오류: {ex.Message}";
            }
        }

        private void SelectChannel(ChannelType type, string displayName)
        {
            _selectedChannelType = type;
            _setupChannelName = displayName;
            if (_selectedChannelText != null) _selectedChannelText.text = displayName;
            if (_channelTokenArea != null) _channelTokenArea.SetActive(true);
            if (_channelStatusText != null) _channelStatusText.text = "";
            if (_channelNextButton != null) _channelNextButton.gameObject.SetActive(false);
        }

        private async UniTaskVoid ConnectChannel()
        {
            var token = _channelTokenInput?.text ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                if (_channelStatusText != null) _channelStatusText.text = "토큰을 입력해주세요.";
                return;
            }

            if (_channelStatusText != null) _channelStatusText.text = "연결 중...";

            try
            {
                if (_channelService != null)
                {
                    await _channelService.ConfigureChannelAsync(_selectedChannelType, token);
                    var result = await _channelService.TestConnectionAsync(_selectedChannelType);
                    var ok = result == ChannelStatus.Connected;

                    if (_channelStatusText != null)
                        _channelStatusText.text = ok ? "✓ 연결 성공!" : "✗ 연결 실패. 토큰을 확인해주세요.";
                    if (_channelNextButton != null)
                        _channelNextButton.gameObject.SetActive(ok);
                }
                else
                {
                    await UniTask.Delay(1000);
                    if (_channelStatusText != null) _channelStatusText.text = "✓ 연결 성공! (Mock)";
                    if (_channelNextButton != null) _channelNextButton.gameObject.SetActive(true);
                }
            }
            catch (Exception ex)
            {
                if (_channelStatusText != null) _channelStatusText.text = $"오류: {ex.Message}";
            }
        }

        // ================================================================
        //  테스트 채팅
        // ================================================================

        private void SendTestMessage(string message)
        {
            if (_testChatInput != null) _testChatInput.text = message;

            if (_bridge != null && _bridge.IsConnected)
            {
                _bridge.SendMessageAsync("main", message).Forget();
                if (_testChatResponseText != null)
                    _testChatResponseText.text = "AI 비서가 생각하고 있어요...";
            }
            else
            {
                // Mock 응답
                if (_testChatResponseText != null)
                    _testChatResponseText.text = $"(Mock 모드) \"{message}\"를 받았어요!\n실제 연결 시 AI가 답변합니다.";
            }

            if (_testChatDoneBtn != null) _testChatDoneBtn.gameObject.SetActive(true);
        }

        // ================================================================
        //  완료
        // ================================================================

        private void FinishWizard()
        {
            PlayerPrefs.SetInt(SetupDoneKey, 1);
            PlayerPrefs.Save();

            Debug.Log("[Wizard] Office 설정 완료!");
            TransitionTo(OfficeWizardState.Hidden);
        }

        /// <summary>설정 탭에서 "초기 설정 다시 하기" 버튼이 호출</summary>
        public void RestartWizard()
        {
            PlayerPrefs.DeleteKey(SetupDoneKey);
            PlayerPrefs.Save();
            TransitionTo(OfficeWizardState.Welcome);
        }

        // ================================================================
        //  유틸리티
        // ================================================================

        private void HideAllPanels()
        {
            if (_allPanels == null) return;
            foreach (var p in _allPanels)
                if (p != null) p.SetActive(false);
        }
    }
}
