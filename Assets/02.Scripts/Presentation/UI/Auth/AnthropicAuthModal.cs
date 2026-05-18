using System;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services.Auth;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Auth
{
    /// <summary>
    /// Anthropic 인증 모달 — API Key 입력 탭과 'Claude 계정 로그인'(OAuth) 탭.
    /// 두 방식 모두 OpenDesk 격리 환경(persistentDataPath + ~/.opendesk/claude-cli)에 저장되며,
    /// 글로벌 ~/.claude/ 는 변경되지 않는다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AnthropicAuthModal : MonoBehaviour
    {
        private IAnthropicCredentialService _credentials;
        private IAuthLoginService _authLogin;

        [Inject]
        public void Construct(IAnthropicCredentialService credentials, IAuthLoginService authLogin = null)
        {
            _credentials = credentials;
            _authLogin = authLogin;
        }

        private UIDocument _document;
        private VisualElement _root;

        private Button _tabKey;
        private Button _tabOAuth;
        private VisualElement _paneKey;
        private VisualElement _paneOAuth;

        private TextField _keyInput;
        private Button _keySave;
        private Button _keyDelete;

        private Label _oauthStatus;
        private Label _oauthCode;
        private Label _oauthUrl;
        private VisualElement _oauthCodeRow;
        private VisualElement _oauthUrlRow;
        private Button _oauthCopyCode;
        private Button _oauthOpen;
        private Button _oauthStart;
        private Button _oauthCancel;

        private Label _error;
        private Button _close;

        private UniTaskCompletionSource<bool> _pendingTcs;
        private IDisposable _authSub;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement?.Q<VisualElement>("anthropic-auth-modal");
            if (_root == null)
            {
                Debug.LogError("[AnthropicAuthModal] UXML 루트 'anthropic-auth-modal' 를 찾지 못함");
                return;
            }

            _tabKey = _root.Q<Button>("anthropic-auth-tab-key");
            _tabOAuth = _root.Q<Button>("anthropic-auth-tab-oauth");
            _paneKey = _root.Q<VisualElement>("anthropic-auth-key-pane");
            _paneOAuth = _root.Q<VisualElement>("anthropic-auth-oauth-pane");

            _keyInput = _root.Q<TextField>("anthropic-auth-key-input");
            _keySave = _root.Q<Button>("anthropic-auth-key-save");
            _keyDelete = _root.Q<Button>("anthropic-auth-key-delete");

            _oauthStatus = _root.Q<Label>("anthropic-auth-oauth-status");
            _oauthCode = _root.Q<Label>("anthropic-auth-oauth-code");
            _oauthUrl = _root.Q<Label>("anthropic-auth-oauth-url");
            _oauthCodeRow = _root.Q<VisualElement>("anthropic-auth-oauth-code-row");
            _oauthUrlRow = _root.Q<VisualElement>("anthropic-auth-oauth-url-row");
            _oauthCopyCode = _root.Q<Button>("anthropic-auth-oauth-copy-code");
            _oauthOpen = _root.Q<Button>("anthropic-auth-oauth-open");
            _oauthStart = _root.Q<Button>("anthropic-auth-oauth-start");
            _oauthCancel = _root.Q<Button>("anthropic-auth-oauth-cancel");

            _error = _root.Q<Label>("anthropic-auth-error");
            _close = _root.Q<Button>("anthropic-auth-close");

            if (_tabKey != null) _tabKey.clicked += () => SelectTab(true);
            if (_tabOAuth != null) _tabOAuth.clicked += () => SelectTab(false);

            if (_keySave != null) _keySave.clicked += HandleKeySave;
            if (_keyDelete != null) _keyDelete.clicked += HandleKeyDelete;

            if (_oauthStart != null) _oauthStart.clicked += HandleOAuthStart;
            if (_oauthCancel != null) _oauthCancel.clicked += HandleOAuthCancel;
            if (_oauthOpen != null) _oauthOpen.clicked += HandleOpenBrowser;
            if (_oauthCopyCode != null) _oauthCopyCode.clicked += HandleCopyCode;

            if (_close != null) _close.clicked += HandleClose;

            if (_authLogin != null)
                _authSub = _authLogin.OnState.Subscribe(HandleAuthState);

            Hide();
        }

        private void OnDisable()
        {
            _authSub?.Dispose();
            _authSub = null;
        }

        // ── 외부 진입점 ────────────────────────────────────────

        /// <summary>모달을 띄우고 사용자 액션 결과를 await 한다. true=인증 완료, false=닫기/취소.</summary>
        public UniTask<bool> AskAsync()
        {
            _pendingTcs?.TrySetResult(false);
            _pendingTcs = new UniTaskCompletionSource<bool>();

            ResetUi();
            Show();
            return _pendingTcs.Task;
        }

        // ── 탭 전환 ────────────────────────────────────────────

        private void SelectTab(bool keyTab)
        {
            if (_paneKey != null) _paneKey.style.display = keyTab ? DisplayStyle.Flex : DisplayStyle.None;
            if (_paneOAuth != null) _paneOAuth.style.display = keyTab ? DisplayStyle.None : DisplayStyle.Flex;

            if (_tabKey != null)
            {
                if (keyTab) _tabKey.AddToClassList("anthropic-auth-modal__tab--active");
                else _tabKey.RemoveFromClassList("anthropic-auth-modal__tab--active");
            }
            if (_tabOAuth != null)
            {
                if (!keyTab) _tabOAuth.AddToClassList("anthropic-auth-modal__tab--active");
                else _tabOAuth.RemoveFromClassList("anthropic-auth-modal__tab--active");
            }
        }

        // ── API Key ───────────────────────────────────────────

        private void HandleKeySave()
        {
            if (_credentials == null || _keyInput == null) return;
            var key = _keyInput.value?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                ShowError("API Key 를 입력해주세요");
                return;
            }
            if (!key.StartsWith("sk-ant-", StringComparison.Ordinal))
            {
                ShowError("Anthropic API Key 형식이 아닙니다 (sk-ant-... 로 시작)");
                return;
            }
            SaveKeyAsync(key).Forget();
        }

        private async UniTaskVoid SaveKeyAsync(string key)
        {
            try
            {
                await _credentials.SetApiKeyAsync(key);
                Resolve(true);
            }
            catch (Exception ex)
            {
                ShowError($"저장 실패: {ex.Message}");
            }
        }

        private void HandleKeyDelete()
        {
            if (_credentials == null) return;
            DeleteKeyAsync().Forget();
        }

        private async UniTaskVoid DeleteKeyAsync()
        {
            try
            {
                await _credentials.DeleteApiKeyAsync();
                if (_keyInput != null) _keyInput.value = string.Empty;
                ShowError("API Key 삭제됨");
            }
            catch (Exception ex)
            {
                ShowError($"삭제 실패: {ex.Message}");
            }
        }

        // ── OAuth ─────────────────────────────────────────────

        private void HandleOAuthStart()
        {
            if (_authLogin == null)
            {
                ShowError("OAuth 서비스가 등록되지 않았습니다");
                return;
            }
            if (_error != null) _error.text = string.Empty;
            _authLogin.Start();
        }

        private void HandleOAuthCancel()
        {
            _authLogin?.Cancel();
        }

        private void HandleOpenBrowser()
        {
            if (_oauthUrl == null || string.IsNullOrEmpty(_oauthUrl.text)) return;
            Application.OpenURL(_oauthUrl.text);
        }

        private void HandleCopyCode()
        {
            if (_oauthCode == null || string.IsNullOrEmpty(_oauthCode.text)) return;
            GUIUtility.systemCopyBuffer = _oauthCode.text;
        }

        private void HandleAuthState(AuthLoginState state)
        {
            if (state == null) return;
            if (_oauthStatus != null) _oauthStatus.text = state.Message ?? string.Empty;

            var hasUrl = !string.IsNullOrEmpty(state.Url);
            var hasCode = !string.IsNullOrEmpty(state.Code);
            if (_oauthUrl != null && hasUrl) _oauthUrl.text = state.Url;
            if (_oauthCode != null && hasCode) _oauthCode.text = state.Code;

            SetHidden(_oauthUrlRow, !hasUrl);
            SetHidden(_oauthCodeRow, !hasCode);

            switch (state.Phase)
            {
                case AuthLoginPhase.Success:
                    Resolve(true);
                    break;
                case AuthLoginPhase.Failed:
                    ShowError(state.Message);
                    break;
                case AuthLoginPhase.Cancelled:
                    ShowError("취소됨");
                    break;
            }
        }

        // ── 공통 ──────────────────────────────────────────────

        private void HandleClose()
        {
            _authLogin?.Cancel();
            Resolve(false);
        }

        private void Resolve(bool ok)
        {
            Hide();
            _pendingTcs?.TrySetResult(ok);
            _pendingTcs = null;
        }

        private void ResetUi()
        {
            if (_keyInput != null) _keyInput.value = string.Empty;
            if (_oauthStatus != null) _oauthStatus.text = "시작 버튼을 누르면 격리 환경에서 'claude login' 이 실행됩니다.";
            if (_oauthCode != null) _oauthCode.text = string.Empty;
            if (_oauthUrl != null) _oauthUrl.text = string.Empty;
            SetHidden(_oauthUrlRow, true);
            SetHidden(_oauthCodeRow, true);
            if (_error != null) _error.text = string.Empty;
            SelectTab(true);
        }

        private void ShowError(string msg)
        {
            if (_error != null) _error.text = msg;
        }

        private static void SetHidden(VisualElement el, bool hidden)
        {
            if (el == null) return;
            if (hidden) el.AddToClassList("anthropic-auth-modal__pane--hidden");
            else el.RemoveFromClassList("anthropic-auth-modal__pane--hidden");
        }

        private void Show()
        {
            if (_root != null) _root.style.display = DisplayStyle.Flex;
        }

        private void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
