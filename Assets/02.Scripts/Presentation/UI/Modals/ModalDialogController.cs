using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenDesk.Presentation.UI.Modals
{
    /// <summary>
    /// 범용 모달 다이얼로그 (확인/취소, 에러 표시)
    /// </summary>
    public class ModalDialogController : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button   _confirmButton;
        [SerializeField] private Button   _cancelButton;
        [SerializeField] private TMP_Text _confirmButtonText;

        private Action _onConfirm;
        private Action _onCancel;

        private void Awake()
        {
            _confirmButton?.onClick.AddListener(OnConfirmClicked);
            _cancelButton?.onClick.AddListener(OnCancelClicked);
            gameObject.SetActive(false);
        }

        /// <summary>확인/취소 다이얼로그</summary>
        public void ShowConfirm(string title, string message, Action onConfirm, Action onCancel = null)
        {
            if (_titleText != null)       _titleText.text = title;
            if (_messageText != null)     _messageText.text = message;
            if (_confirmButtonText != null) _confirmButtonText.text = "확인";

            _onConfirm = onConfirm;
            _onCancel  = onCancel;

            _cancelButton?.gameObject.SetActive(true);
            gameObject.SetActive(true);
        }

        /// <summary>에러 다이얼로그 (닫기 버튼만)</summary>
        public void ShowError(string title, string message)
        {
            if (_titleText != null)       _titleText.text = title;
            if (_messageText != null)     _messageText.text = message;
            if (_confirmButtonText != null) _confirmButtonText.text = "닫기";

            _onConfirm = null;
            _onCancel  = null;

            _cancelButton?.gameObject.SetActive(false);
            gameObject.SetActive(true);
        }

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            gameObject.SetActive(false);
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            gameObject.SetActive(false);
        }
    }
}
