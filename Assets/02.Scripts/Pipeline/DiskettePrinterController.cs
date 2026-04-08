using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.SkillDiskette;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Pipeline
{
    /// <summary>
    /// 3D Printer 컨트롤러.
    /// - 토글 버튼으로 크래프팅 프롬프트 바 열기/닫기
    /// - 크래프팅 완료 시 DisketteShelfUI에 카드 추가
    /// - 프리셋 디스켓도 ShelfUI에 등록
    /// </summary>
    public class DiskettePrinterController : MonoBehaviour
    {
        [Header("디스켓 선반 UI")]
        [SerializeField] private DisketteShelfUI _shelfUI;

        [Header("프롬프트 바 UI")]
        [SerializeField] private GameObject _promptBarPanel;
        [SerializeField] private TMP_InputField _promptInput;
        [SerializeField] private Button _craftButton;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("토글 버튼")]
        [SerializeField] private Button _toggleButton;
        [SerializeField] private TextMeshProUGUI _toggleLabel;

        // ── DI ──
        private IClaudeService _claudeService;

        [Inject]
        public void Construct(IClaudeService claudeService)
        {
            _claudeService = claudeService;
        }

        // ── 내부 ──
        private SkillDisketteFactory _factory;
        private bool _isCrafting;
        private bool _promptBarOpen;

        // ══════════════════════════════════════════════
        //  초기화
        // ══════════════════════════════════════════════

        private void Start()
        {
            _factory = new SkillDisketteFactory();
            _factory.Initialize(null); // 프리팹 불필요 (UI 기반)

            if (_craftButton != null)
                _craftButton.onClick.AddListener(OnCraftClicked);

            if (_promptInput != null)
                _promptInput.onSubmit.AddListener(_ => OnCraftClicked());

            if (_toggleButton != null)
                _toggleButton.onClick.AddListener(TogglePromptBar);

            // 프롬프트 바 초기 상태: 닫힘
            SetPromptBarOpen(false);
            SetStatus("");

            // 프리셋 디스켓을 ShelfUI에 등록
            LoadPresetsToShelf();
        }

        // ══════════════════════════════════════════════
        //  프리셋 → ShelfUI
        // ══════════════════════════════════════════════

        private void LoadPresetsToShelf()
        {
            if (_shelfUI == null) return;

            var presets = _factory.GetAllPresets();
            foreach (var preset in presets)
                _shelfUI.AddDiskette(preset);

            Debug.Log($"[Printer] 프리셋 {presets.Count}개 선반 UI에 등록");
        }

        // ══════════════════════════════════════════════
        //  토글
        // ══════════════════════════════════════════════

        private void TogglePromptBar()
        {
            SetPromptBarOpen(!_promptBarOpen);
        }

        private void SetPromptBarOpen(bool open)
        {
            _promptBarOpen = open;

            if (_promptBarPanel != null)
                _promptBarPanel.SetActive(open);

            if (_toggleLabel != null)
                _toggleLabel.SetText(open ? "크래프팅 닫기" : "크래프팅");
        }

        // ══════════════════════════════════════════════
        //  크래프팅
        // ══════════════════════════════════════════════

        private void OnCraftClicked()
        {
            if (_isCrafting) return;
            if (_promptInput == null) return;

            var prompt = _promptInput.text.Trim();
            if (string.IsNullOrEmpty(prompt)) return;

            CraftAsync(prompt).Forget();
        }

        private async UniTaskVoid CraftAsync(string prompt)
        {
            _isCrafting = true;
            SetCraftUI(false);
            SetStatus("크래프팅 중...");

            try
            {
                var ct = this.GetCancellationTokenOnDestroy();
                // writer 에이전트로 크래프팅 (문서/스킬 생성 전담)
                var result = await _claudeService.CraftDisketteAsync("writer", prompt, ct);

                if (result != null && result.IsValid)
                {
                    // 런타임 SO 생성
                    var so = OpenDesk.SkillDiskette.SkillDiskette.CreateRuntime(
                        skillId: $"custom-{System.Guid.NewGuid().ToString("N")[..12]}",
                        displayName: result.skillName,
                        description: result.description,
                        category: result.ParseCategory(),
                        promptContent: result.promptContent,
                        isCustomCrafted: true,
                        craftPrompt: prompt
                    );

                    // ShelfUI에 추가
                    if (_shelfUI != null)
                        _shelfUI.AddDiskette(so);

                    SetStatus($"'{result.skillName}' 생성 완료!");
                    _promptInput.text = "";

                    Debug.Log($"[Printer] 크래프팅 완료: {result.skillName}");
                }
                else
                {
                    SetStatus("크래프팅 실패 - 다시 시도해주세요");
                }
            }
            catch (System.OperationCanceledException)
            {
                SetStatus("크래프팅 취소됨");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Printer] 크래프팅 오류: {e.Message}");
                SetStatus("오류 발생 - 다시 시도해주세요");
            }
            finally
            {
                _isCrafting = false;
                SetCraftUI(true);
            }
        }

        // ══════════════════════════════════════════════
        //  UI 헬퍼
        // ══════════════════════════════════════════════

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.SetText(text);
        }

        private void SetCraftUI(bool interactable)
        {
            if (_craftButton != null)
                _craftButton.interactable = interactable;
        }
    }
}
