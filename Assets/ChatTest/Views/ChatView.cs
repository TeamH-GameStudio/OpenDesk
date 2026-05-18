using System.ComponentModel;
using ChatTest.Data;
using ChatTest.Models;
using ChatTest.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace ChatTest.Views
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChatView : MonoBehaviour
    {
        private const string MessageBubbleClass = "message__bubble";
        private const string MessageUserClass = "message--user";
        private const string MessageAssistantClass = "message--assistant";
        // Animation entry is handled via inline opacity + translate so that the
        // base .message transition fires. No --enter class needed.

        private UIDocument _document;
        private ChatViewModel _viewModel;

        private VisualElement _messagesContainer;
        private ScrollView _chatScroll;
        private TextField _chatInput;
        private VisualElement _chatInputInner;
        private VisualElement _chatInputRow;
        private Button _sendButton;
        private Button _sessionsButton;
        private Button _closeButton;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null || _document.rootVisualElement == null)
            {
                Debug.LogError("[ChatView] UIDocument or rootVisualElement is null. Assign UXML to the UIDocument.");
                return;
            }

            var root = _document.rootVisualElement;
            CacheElements(root);

            _viewModel = new ChatViewModel(ChatSeed.GetInitialConversation(), ChatSeed.GetMockReplies());

            RenderInitialMessages();
            RegisterCallbacks();
            UpdateSendButtonState();
            ScrollToBottom();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
            if (_viewModel != null)
            {
                _viewModel.Dispose();
                _viewModel = null;
            }
            if (_messagesContainer != null) _messagesContainer.Clear();
        }

        private void CacheElements(VisualElement root)
        {
            _messagesContainer = root.Q<VisualElement>("messages-container");
            _chatScroll = root.Q<ScrollView>("chat-scroll");
            _chatInput = root.Q<TextField>("chat-input");
            _sendButton = root.Q<Button>("send-button");
            _sessionsButton = root.Q<Button>("sessions-button");
            _closeButton = root.Q<Button>("close-button");
            _chatInputInner = _chatInput?.Q("unity-text-input");
            _chatInputRow = root.Q<VisualElement>(className: "chat-input-row");
        }

        private void RegisterCallbacks()
        {
            _chatInput.RegisterValueChangedCallback(OnDraftChanged);
            _sendButton.clicked += OnSendClicked;

            // Trickle-down on the inner input lets us intercept Enter before
            // the multiline TextField inserts a newline.
            if (_chatInputInner != null)
            {
                _chatInputInner.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
                _chatInputInner.RegisterCallback<FocusInEvent>(OnInputFocusIn);
                _chatInputInner.RegisterCallback<FocusOutEvent>(OnInputFocusOut);
            }

            if (_sessionsButton != null) _sessionsButton.clicked += OnSessionsClicked;
            if (_closeButton != null) _closeButton.clicked += OnCloseClicked;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.MessageAdded += OnMessageAdded;
        }

        private void UnregisterCallbacks()
        {
            if (_chatInput != null) _chatInput.UnregisterValueChangedCallback(OnDraftChanged);
            if (_sendButton != null) _sendButton.clicked -= OnSendClicked;
            if (_chatInputInner != null)
            {
                _chatInputInner.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
                _chatInputInner.UnregisterCallback<FocusInEvent>(OnInputFocusIn);
                _chatInputInner.UnregisterCallback<FocusOutEvent>(OnInputFocusOut);
            }
            if (_sessionsButton != null) _sessionsButton.clicked -= OnSessionsClicked;
            if (_closeButton != null) _closeButton.clicked -= OnCloseClicked;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.MessageAdded -= OnMessageAdded;
            }
        }

        private void RenderInitialMessages()
        {
            if (_messagesContainer == null || _viewModel == null) return;
            _messagesContainer.Clear();
            foreach (var msg in _viewModel.Messages)
            {
                _messagesContainer.Add(BuildMessage(msg, animateIn: false));
            }
        }

        private void OnDraftChanged(ChangeEvent<string> evt)
        {
            if (_viewModel == null) return;
            _viewModel.Draft = evt.newValue ?? string.Empty;
        }

        private void OnSendClicked()
        {
            TrySend();
        }

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            var isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            if (!isEnter) return;
            if (evt.shiftKey) return;

            evt.StopPropagation();
            evt.PreventDefault();
            TrySend();
        }

        private void TrySend()
        {
            if (_viewModel == null || !_viewModel.CanSend) return;
            _viewModel.Send();
            // The TextField won't auto-clear from VM since we set value via VM only;
            // also reset the inner text so multiline cursor resets cleanly.
            _chatInput.SetValueWithoutNotify(string.Empty);
        }

        private void OnSessionsClicked()
        {
            // Hook for opening session list panel (§2+ Sessions); test stub.
        }

        private void OnInputFocusIn(FocusInEvent evt)
        {
            _chatInputRow?.AddToClassList("chat-input-row--focused");
        }

        private void OnInputFocusOut(FocusOutEvent evt)
        {
            _chatInputRow?.RemoveFromClassList("chat-input-row--focused");
        }

        private void OnCloseClicked()
        {
            // Hook for closing the panel; test stub.
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CanSend))
            {
                UpdateSendButtonState();
            }
        }

        private void UpdateSendButtonState()
        {
            if (_sendButton == null || _viewModel == null) return;
            _sendButton.SetEnabled(_viewModel.CanSend);
        }

        private void OnMessageAdded(ChatMessage msg)
        {
            if (_messagesContainer == null) return;
            var element = BuildMessage(msg, animateIn: true);
            _messagesContainer.Add(element);
            ScrollToBottom();
        }

        private VisualElement BuildMessage(ChatMessage msg, bool animateIn)
        {
            var row = new VisualElement();
            row.AddToClassList("message");
            row.AddToClassList(msg.Sender == ChatSender.User ? MessageUserClass : MessageAssistantClass);

            if (msg.Sender == ChatSender.Assistant)
            {
                var senderLabel = new Label("Writer");
                senderLabel.AddToClassList("message__sender-label");
                row.Add(senderLabel);
            }

            var bubble = new Label(msg.Body);
            bubble.AddToClassList(MessageBubbleClass);
            row.Add(bubble);

            if (animateIn)
            {
                // Inline starts opaque-zero; one frame later inline flips to 1.
                // The .message base owns the transition, so the inline change
                // animates in. (USS class flip doesn't work here because inline
                // styles override class values.)
                row.style.opacity = 0f;
                row.style.translate = new StyleTranslate(new Translate(0, 6, 0));
                row.schedule.Execute(() =>
                {
                    row.style.opacity = 1f;
                    row.style.translate = new StyleTranslate(new Translate(0, 0, 0));
                }).StartingIn(16);
            }

            return row;
        }

        private void ScrollToBottom()
        {
            if (_chatScroll == null) return;
            _chatScroll.schedule.Execute(() =>
            {
                var content = _chatScroll.contentContainer;
                if (content == null) return;
                var height = content.layout.height;
                _chatScroll.scrollOffset = new Vector2(0, height);
            }).StartingIn(20);
        }
    }
}
