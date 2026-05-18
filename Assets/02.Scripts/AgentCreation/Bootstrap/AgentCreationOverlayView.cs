using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Services;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.AgentCreation.Bootstrap
{
    /// <summary>
    /// 위저드 완료 직후 풀스크린 로딩 오버레이.
    /// AgentDraftSaveTrigger.Saved → 표시. IAgentCreationBridge.OfficeSetupCompleted → 숨김.
    ///
    /// 별도 UIDocument 를 새로 만들지 않고, 위저드 씬에 이미 존재하는 UIDocument 의
    /// rootVisualElement 에 자식으로 append. 마지막에 추가된 자식이 자연스럽게 z-order 상위에
    /// 올라와 위저드 위를 덮으므로 sortingOrder 분리도 불필요.
    /// </summary>
    public sealed class AgentCreationOverlayView : MonoBehaviour
    {
        [Tooltip("위저드 씬의 기존 UIDocument — AgentCreationView 가 사용하는 것과 동일.")]
        [SerializeField] private UIDocument _document;
        [SerializeField] private AgentDraftSaveTrigger _saveTrigger;
        [SerializeField] private string _message = "사무실에 동료가 합류 중...";

        private VisualElement _root;
        private Label _label;

        private IAgentCreationBridge _bridge;

        [Inject]
        public void Construct(IAgentCreationBridge bridge)
        {
            _bridge = bridge;
        }

        private void OnEnable()
        {
            BuildOverlay();
            SetVisible(false);

#pragma warning disable CS0618 // Saved 는 후방 호환 — Step 5 에서 IAgentRepository 구독으로 이전 예정.
            if (_saveTrigger != null)
                _saveTrigger.Saved += OnSaved;
#pragma warning restore CS0618
            if (_bridge != null)
                _bridge.OfficeSetupCompleted += OnSetupCompleted;
        }

        private void OnDisable()
        {
#pragma warning disable CS0618
            if (_saveTrigger != null)
                _saveTrigger.Saved -= OnSaved;
#pragma warning restore CS0618
            if (_bridge != null)
                _bridge.OfficeSetupCompleted -= OnSetupCompleted;

            // 동적으로 생성한 요소만 정리 — UIDocument 자체는 위저드 소유.
            if (_root != null && _root.parent != null)
                _root.parent.Remove(_root);
            _root = null;
        }

        // ────────────────────────────────────────────────────────
        //  핸들러
        // ────────────────────────────────────────────────────────

        private void OnSaved(AgentDraftRecord record, string path)
        {
            if (_label != null) _label.text = _message;
            SetVisible(true);
        }

        private void OnSetupCompleted()
        {
            SetVisible(false);
        }

        // ────────────────────────────────────────────────────────
        //  비주얼
        // ────────────────────────────────────────────────────────

        private void BuildOverlay()
        {
            if (_document == null)
            {
                Debug.LogError("[AgentCreationOverlayView] UIDocument 미할당 — 위저드의 UIDocument 를 SerializeField 로 연결해야 합니다.");
                return;
            }
            var root = _document.rootVisualElement;
            if (root == null) return;

            _root = new VisualElement();
            _root.name = "agent-creation-overlay";
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.78f));
            _root.pickingMode = PickingMode.Position; // 입력 차단
            // 마지막에 add 되어 형제들 위에 그려짐 — 자동으로 위저드를 덮는다.
            root.Add(_root);

            var spinner = new CircularProgress { name = "agent-creation-overlay-spinner" };
            spinner.style.width = 64;
            spinner.style.height = 64;
            spinner.style.marginBottom = 24;
            _root.Add(spinner);

            _label = new Label(_message);
            _label.style.color = new StyleColor(new Color(1f, 1f, 1f, 0.95f));
            _label.style.fontSize = 18;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _root.Add(_label);
        }

        private void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
