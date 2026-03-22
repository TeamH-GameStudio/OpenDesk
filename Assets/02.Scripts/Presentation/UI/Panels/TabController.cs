using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Panels
{
    /// <summary>
    /// 탭 전환 컨트롤러 — 버튼 클릭으로 패널 스왑
    /// Inspector에서 탭 버튼과 패널을 1:1로 연결
    /// </summary>
    public class TabController : MonoBehaviour
    {
        [SerializeField] private Button[]     _tabButtons;
        [SerializeField] private GameObject[] _tabPanels;
        [SerializeField] private Color        _activeTabColor   = new(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private Color        _inactiveTabColor = new(0.3f, 0.3f, 0.3f, 1f);
        [SerializeField] private int          _defaultTabIndex  = 0;

        private int _currentTab = -1;

        private void Start()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int index = i; // 클로저 캡처
                _tabButtons[i].onClick.AddListener(() => SwitchToTab(index));
            }

            SwitchToTab(_defaultTabIndex);
        }

        public void SwitchToTab(int index)
        {
            if (index == _currentTab) return;
            if (index < 0 || index >= _tabPanels.Length) return;

            _currentTab = index;

            // 패널 활성화/비활성화
            for (int i = 0; i < _tabPanels.Length; i++)
            {
                if (_tabPanels[i] != null)
                    _tabPanels[i].SetActive(i == index);
            }

            // 탭 버튼 색상
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null) continue;
                var colors = _tabButtons[i].colors;
                colors.normalColor = i == index ? _activeTabColor : _inactiveTabColor;
                _tabButtons[i].colors = colors;
            }
        }
    }
}
