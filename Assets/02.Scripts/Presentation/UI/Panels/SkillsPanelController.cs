using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.UI.SkillMarket;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>스킬 마켓플레이스 패널 — 검색/설치/삭제/샌드박스 토글</summary>
    public class SkillsPanelController : MonoBehaviour
    {
        [Header("검색")]
        [SerializeField] private TMP_InputField _searchField;
        [SerializeField] private Button         _searchButton;

        [Header("탭")]
        [SerializeField] private Button[] _tabButtons = new Button[3]; // 추천/설치됨/전체

        [Header("스킬 그리드")]
        [SerializeField] private RectTransform _skillContainer;
        [SerializeField] private GameObject    _skillCardPrefab;
        [SerializeField] private GameObject    _loadingIndicator;

        [Header("신규 마켓 진입점")]
        [Tooltip("새 SkillMarket 패널을 여는 진입 버튼. 비어 있으면 신규 마켓 통합 미사용.")]
        [SerializeField] private Button _openMarketButton;

        [Inject] private ISkillMarketService _skillMarket;
        private SkillMarketView _newMarketView;

        private int _currentTab; // 0=추천, 1=설치됨, 2=전체

        private void Start()
        {
            // 신규 마켓 패널 진입 버튼 — 씬에 컴포넌트가 있을 때만 동작.
            if (_openMarketButton != null)
            {
                _openMarketButton.onClick.AddListener(OnOpenNewMarketClicked);
            }

            if (_skillMarket == null) return;

            _searchButton?.onClick.AddListener(() => RefreshSkills());

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int idx = i;
                _tabButtons[i]?.onClick.AddListener(() =>
                {
                    _currentTab = idx;
                    RefreshSkills();
                });
            }

            _skillMarket.OnSkillChanged.Subscribe(_ => RefreshSkills()).AddTo(this);

            RefreshSkills();
        }

        private void OnOpenNewMarketClicked()
        {
            if (_newMarketView == null)
                _newMarketView = FindFirstObjectByType<SkillMarketView>(FindObjectsInactive.Include);

            if (_newMarketView == null)
            {
                Debug.LogWarning("[SkillsPanel] SkillMarketView 미발견 — 씬에 UIDocument + SkillMarketView 를 배치하세요.");
                return;
            }

            // 진입 단계에선 에이전트 컨텍스트가 없을 수 있으므로 빈 컨텍스트로 오픈.
            // ChatPanel 컨텍스트가 있을 때는 ChatPanelController 가 currentAgentId / role 을 전달.
            _newMarketView.Open(agentId: string.Empty, role: AgentRole.None);
        }

        private async void RefreshSkills()
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(true);

            // 기존 카드 제거
            if (_skillContainer == null) { if (_loadingIndicator != null) _loadingIndicator.SetActive(false); return; }
            foreach (Transform child in _skillContainer)
                Destroy(child.gameObject);

            IReadOnlyList<SkillEntry> skills = _currentTab switch
            {
                0 => await _skillMarket.GetFeaturedSkillsAsync(),
                1 => await _skillMarket.GetInstalledSkillsAsync(),
                _ => await _skillMarket.SearchSkillsAsync(_searchField?.text ?? ""),
            };

            foreach (var skill in skills)
                CreateSkillCard(skill);

            if (_loadingIndicator != null) _loadingIndicator.SetActive(false);
        }

        private void CreateSkillCard(SkillEntry skill)
        {
            if (_skillCardPrefab == null) return;

            var obj = Instantiate(_skillCardPrefab, _skillContainer);

            // 텍스트 필드 찾아서 설정
            SetChildText(obj, "NameText", skill.Name);
            SetChildText(obj, "AuthorText", $"by {skill.Author}");
            SetChildText(obj, "DescriptionText", skill.Description);
            SetChildText(obj, "CategoryText", skill.Category);
            SetChildText(obj, "RatingText", $"{skill.Rating:F1}*");
            SetChildText(obj, "DownloadsText", $"{skill.Downloads:N0}");

            // 설치/삭제 버튼
            var actionBtn = obj.transform.Find("ActionButton")?.GetComponent<Button>();
            var actionText = actionBtn?.GetComponentInChildren<TMP_Text>();
            if (actionBtn != null && actionText != null)
            {
                actionText.text = skill.IsInstalled ? "삭제" : "설치";
                var id = skill.Id;
                var installed = skill.IsInstalled;
                actionBtn.onClick.AddListener(() =>
                {
                    if (installed)
                        _skillMarket.UninstallSkillAsync(id).Forget();
                    else
                        _skillMarket.InstallSkillAsync(id).Forget();
                });
            }

            // 샌드박스 토글
            var sandboxToggle = obj.GetComponentInChildren<Toggle>();
            if (sandboxToggle != null)
            {
                sandboxToggle.isOn = skill.IsSandboxed;
                sandboxToggle.gameObject.SetActive(skill.IsInstalled);
                var id = skill.Id;
                sandboxToggle.onValueChanged.AddListener(isOn =>
                    _skillMarket.SetSandboxModeAsync(id, isOn).Forget());
            }
        }

        private static void SetChildText(GameObject parent, string childName, string text)
        {
            var child = parent.transform.Find(childName);
            var tmp = child?.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = text;
        }
    }
}
