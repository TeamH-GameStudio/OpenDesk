using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using OpenDesk.Core.Services.Plugins;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace OpenDesk.Presentation.UI.Plugins
{
    /// <summary>
    /// 플러그인 자격증명 입력 모달 — CredentialRequirement 기반 동적 폼.
    /// 호출자가 AskAsync(descriptor) 로 호출하면, 저장 시 true 를 반환한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PluginCredentialModal : MonoBehaviour
    {
        private IPluginCredentialService _credentials;

        [Inject]
        public void Construct(IPluginCredentialService credentials)
        {
            _credentials = credentials;
        }

        private UIDocument _document;
        private VisualElement _root;
        private Label _title;
        private Label _subtitle;
        private Label _error;
        private ScrollView _fields;
        private Button _cancelButton;
        private Button _saveButton;

        private readonly Dictionary<string, TextField> _inputs = new();
        private PluginDescriptor _pendingDescriptor;
        private UniTaskCompletionSource<bool> _pendingTcs;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement?.Q<VisualElement>("plugin-cred-modal");
            if (_root == null)
            {
                Debug.LogError("[PluginCredentialModal] UXML 루트 'plugin-cred-modal' 를 찾지 못함");
                return;
            }

            _title = _root.Q<Label>("plugin-cred-title");
            _subtitle = _root.Q<Label>("plugin-cred-subtitle");
            _error = _root.Q<Label>("plugin-cred-error");
            _fields = _root.Q<ScrollView>("plugin-cred-fields");
            _cancelButton = _root.Q<Button>("plugin-cred-cancel");
            _saveButton = _root.Q<Button>("plugin-cred-save");

            if (_cancelButton != null) _cancelButton.clicked += HandleCancel;
            if (_saveButton != null) _saveButton.clicked += HandleSave;

            Hide();
        }

        private void OnDisable()
        {
            if (_cancelButton != null) _cancelButton.clicked -= HandleCancel;
            if (_saveButton != null) _saveButton.clicked -= HandleSave;
        }

        /// <summary>저장 시 true, 취소 시 false 반환.</summary>
        public UniTask<bool> AskAsync(PluginDescriptor descriptor)
        {
            if (descriptor == null) return UniTask.FromResult(false);
            _pendingTcs?.TrySetResult(false);

            _pendingDescriptor = descriptor;
            _pendingTcs = new UniTaskCompletionSource<bool>();

            BuildFields(descriptor);
            if (_title != null) _title.text = $"{descriptor.DisplayName} 자격증명";
            if (_subtitle != null) _subtitle.text = descriptor.Description ?? string.Empty;
            if (_error != null) _error.text = string.Empty;

            Show();
            return _pendingTcs.Task;
        }

        private void BuildFields(PluginDescriptor descriptor)
        {
            _inputs.Clear();
            if (_fields == null) return;
            _fields.Clear();

            if (descriptor.RequiredCredentials == null) return;
            foreach (var req in descriptor.RequiredCredentials)
            {
                if (req == null || string.IsNullOrEmpty(req.Key)) continue;

                var wrapper = new VisualElement();
                wrapper.AddToClassList("plugin-cred-field");

                var label = new Label($"{req.DisplayName}{(req.Optional ? " (선택)" : "")}");
                label.AddToClassList("plugin-cred-field__label");
                label.AddToClassList("od-body-sm");
                wrapper.Add(label);

                var input = new TextField { isPasswordField = req.Kind != CredentialKind.Custom };
                input.AddToClassList("plugin-cred-field__input");
                wrapper.Add(input);

                var hint = new Label($"{req.Key} · {req.Kind}");
                hint.AddToClassList("plugin-cred-field__hint");
                hint.AddToClassList("od-caption");
                wrapper.Add(hint);

                _fields.Add(wrapper);
                _inputs[req.Key] = input;
            }
        }

        private void HandleCancel()
        {
            Hide();
            _pendingTcs?.TrySetResult(false);
            _pendingTcs = null;
        }

        private void HandleSave()
        {
            if (_pendingDescriptor == null || _credentials == null)
            {
                HandleCancel();
                return;
            }

            // 1) 필수 필드 검증
            foreach (var req in _pendingDescriptor.RequiredCredentials ?? Array.Empty<CredentialRequirement>())
            {
                if (req.Optional) continue;
                if (!_inputs.TryGetValue(req.Key, out var input)) continue;
                if (string.IsNullOrEmpty(input.value))
                {
                    if (_error != null) _error.text = $"{req.DisplayName} 을(를) 입력해주세요.";
                    return;
                }
            }

            SaveAsync().Forget();
        }

        private async UniTaskVoid SaveAsync()
        {
            try
            {
                foreach (var pair in _inputs)
                {
                    if (string.IsNullOrEmpty(pair.Value.value)) continue;
                    await _credentials.SetAsync(_pendingDescriptor.Id, pair.Key, pair.Value.value);
                }
                Hide();
                _pendingTcs?.TrySetResult(true);
                _pendingTcs = null;
            }
            catch (Exception ex)
            {
                if (_error != null) _error.text = $"저장 실패: {ex.Message}";
            }
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
