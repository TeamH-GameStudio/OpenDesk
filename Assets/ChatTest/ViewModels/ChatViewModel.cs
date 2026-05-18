using System;
using System.Collections.Generic;
using ChatTest.Common;
using ChatTest.Data;
using ChatTest.Models;

namespace ChatTest.ViewModels
{
    public sealed class ChatViewModel : ObservableObject, IDisposable
    {
        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        private readonly IReadOnlyList<string> _mockReplies;
        private int _nextId;
        private int _replyIndex;

        public IReadOnlyList<ChatMessage> Messages => _messages;

        public event Action<ChatMessage> MessageAdded;

        private string _draft = string.Empty;
        public string Draft
        {
            get => _draft;
            set
            {
                if (SetField(ref _draft, value ?? string.Empty))
                {
                    RaisePropertyChanged(nameof(CanSend));
                }
            }
        }

        public bool CanSend => !string.IsNullOrWhiteSpace(_draft);

        public ChatViewModel(IReadOnlyList<ChatMessage> seed, IReadOnlyList<string> mockReplies)
        {
            _mockReplies = mockReplies ?? new List<string>();
            if (seed != null)
            {
                foreach (var msg in seed)
                {
                    _messages.Add(msg);
                    if (msg.Id >= _nextId) _nextId = msg.Id + 1;
                }
            }
        }

        public void Send()
        {
            if (!CanSend) return;

            var userMsg = new ChatMessage(_nextId++, ChatSender.User, _draft.Trim());
            _messages.Add(userMsg);
            MessageAdded?.Invoke(userMsg);

            Draft = string.Empty;

            if (_mockReplies.Count > 0)
            {
                var reply = _mockReplies[_replyIndex % _mockReplies.Count];
                _replyIndex++;
                var assistantMsg = new ChatMessage(_nextId++, ChatSender.Assistant, reply);
                _messages.Add(assistantMsg);
                MessageAdded?.Invoke(assistantMsg);
            }
        }

        public void Dispose()
        {
            MessageAdded = null;
        }
    }
}
