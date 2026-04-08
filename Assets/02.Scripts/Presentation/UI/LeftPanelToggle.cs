using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenDesk.Presentation.UI
{
    /// <summary>
    /// 에이전트 클릭 시 좌측 패널을 활성화하는 간단한 토글.
    /// AgentClickHandler가 에이전트를 감지하면 이 패널을 켬.
    /// 빈 공간 클릭 또는 ESC로 닫기.
    /// </summary>
    public class LeftPanelToggle : MonoBehaviour
    {
        [SerializeField] private GameObject _leftPanel;

        public bool IsOpen => _leftPanel != null && _leftPanel.activeSelf;

        /// <summary>좌측 패널 열기</summary>
        public void Show()
        {
            if (_leftPanel != null)
                _leftPanel.SetActive(true);
        }

        /// <summary>좌측 패널 닫기</summary>
        public void Hide()
        {
            if (_leftPanel != null)
                _leftPanel.SetActive(false);
        }

        /// <summary>토글</summary>
        public void Toggle()
        {
            if (_leftPanel != null)
                _leftPanel.SetActive(!_leftPanel.activeSelf);
        }

        private void Update()
        {
            // ESC로 패널 닫기
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Hide();
        }
    }
}
