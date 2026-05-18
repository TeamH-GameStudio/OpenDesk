using OpenDesk.Presentation.Character;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Office
{
    /// <summary>
    /// 오피스 씬에서 저장된 에이전트가 0명일 때 노출되는 빈 상태 CTA.
    /// AgentRosterBootstrapper.OnEmptyRoster 구독 → 표시.
    /// AgentRosterBootstrapper.OnAgentSpawned 구독 → 숨김.
    /// 버튼 클릭 → AgentCreationOpener.Open() — AgentCreationScene 으로 Single 모드 전환.
    ///
    /// 인스펙터 작업: 같은 GameObject 의 UIDocument.Source Asset 에
    /// OfficeRosterEmptyView.uxml 을 연결하면 끝. (UXML 내부의 Style src 가 USS 자동 참조.)
    ///
    /// 1명 이상 스폰되면 자동 숨김 — Additive 추가 흐름 후 재등장 안 함.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OfficeRosterEmptyView : MonoBehaviour
    {
        private UIDocument _document;
        private VisualElement _root;
        private Button _ctaButton;

        private AgentRosterBootstrapper _roster;
        private AgentCreationOpener _opener;

        [Inject]
        public void Construct(AgentRosterBootstrapper roster, AgentCreationOpener opener)
        {
            _roster = roster;
            _opener = opener;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            BuildView();
            SetVisible(false);

            if (_roster != null)
            {
                _roster.OnEmptyRoster += OnEmptyRoster;
                _roster.OnAgentSpawned += OnAgentSpawned;
            }
        }

        private void OnDisable()
        {
            if (_roster != null)
            {
                _roster.OnEmptyRoster -= OnEmptyRoster;
                _roster.OnAgentSpawned -= OnAgentSpawned;
            }
            if (_ctaButton != null)
                _ctaButton.clicked -= OnCtaClicked;
        }

        // ────────────────────────────────────────────────────────
        //  핸들러
        // ────────────────────────────────────────────────────────

        private void OnEmptyRoster() => SetVisible(true);
        private void OnAgentSpawned(string _) => SetVisible(false);

        private void OnCtaClicked()
        {
            if (_opener == null)
            {
                Debug.LogError("[OfficeRosterEmptyView] AgentCreationOpener 미주입.");
                return;
            }
            _opener.Open();
        }

        // ────────────────────────────────────────────────────────
        //  뷰 구성 — UXML 로드
        // ────────────────────────────────────────────────────────

        private void BuildView()
        {
            if (_document == null)
            {
                Debug.LogError("[OfficeRosterEmptyView] UIDocument 컴포넌트 누락.");
                return;
            }

            var root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[OfficeRosterEmptyView] rootVisualElement 가 null — UIDocument 의 Source Asset 에 OfficeRosterEmptyView.uxml 을 연결하세요.");
                return;
            }

            _root = root.Q<VisualElement>("office-roster-empty");
            _ctaButton = root.Q<Button>("office-empty-cta");

            if (_root == null)
            {
                Debug.LogError("[OfficeRosterEmptyView] UXML 트리에서 'office-roster-empty' 를 찾지 못했습니다 — UXML 이름을 확인하세요.");
                return;
            }
            if (_ctaButton == null)
            {
                Debug.LogWarning("[OfficeRosterEmptyView] 'office-empty-cta' 버튼을 찾지 못했습니다 — CTA 클릭 동작 없음.");
            }
            else
            {
                _ctaButton.clicked += OnCtaClicked;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
