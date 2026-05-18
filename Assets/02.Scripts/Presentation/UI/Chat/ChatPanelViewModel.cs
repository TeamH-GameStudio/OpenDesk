using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Claude.Models;

namespace OpenDesk.Presentation.UI.Chat
{
    /// <summary>
    /// ChatPanelView 의 상태/이벤트 단일 진입점.
    /// View 는 본 VM 의 이벤트만 구독해서 DOM 을 변경한다 (도메인 로직 직접 호출 금지).
    /// ChatTest.ChatViewModel 의 ObservableObject 패턴을 차용하되, OpenDesk 의 ChatSender (User/Agent/System) 와 호환.
    /// </summary>
    public sealed class ChatPanelViewModel : IDisposable
    {
        private readonly List<ChatMessageVM> _messages = new();
        private string _draft = string.Empty;
        private bool _isStreaming;
        private string _status = string.Empty;
        private string _agentName = "에이전트";
        private string _agentRole = string.Empty;

        public IReadOnlyList<ChatMessageVM> Messages => _messages;

        public string AgentName
        {
            get => _agentName;
            set
            {
                if (_agentName == value) return;
                _agentName = value ?? string.Empty;
                AgentInfoChanged?.Invoke();
            }
        }

        public string AgentRole
        {
            get => _agentRole;
            set
            {
                if (_agentRole == value) return;
                _agentRole = value ?? string.Empty;
                AgentInfoChanged?.Invoke();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value ?? string.Empty;
                StatusChanged?.Invoke(_status);
            }
        }

        public string Draft
        {
            get => _draft;
            set
            {
                var next = value ?? string.Empty;
                if (_draft == next) return;
                _draft = next;
                CanSendChanged?.Invoke(CanSend);
            }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (_isStreaming == value) return;
                _isStreaming = value;
                CanSendChanged?.Invoke(CanSend);
            }
        }

        /// <summary>비어있지 않은 초안 + 스트리밍 중이 아닐 때만 전송 가능</summary>
        public bool CanSend => !string.IsNullOrWhiteSpace(_draft) && !_isStreaming;

        // ── 이벤트 ──
        public event Action<ChatMessageVM> MessageAdded;
        public event Action<ChatMessageVM> MessageUpdated;
        public event Action<bool> CanSendChanged;
        public event Action AgentInfoChanged;
        public event Action<string> StatusChanged;
        public event Action HistoryReset;

        // ── 메시지 조작 ──

        public void ResetHistory(IReadOnlyList<ChatMessage> history)
        {
            _messages.Clear();
            if (history != null)
            {
                foreach (var msg in history)
                {
                    if (msg == null) continue;
                    _messages.Add(new ChatMessageVM(msg.Sender, msg.Text, msg.Time));
                }
            }
            HistoryReset?.Invoke();
        }

        public ChatMessageVM AddMessage(ChatSender sender, string text)
        {
            var vm = new ChatMessageVM(sender, text ?? string.Empty, DateTime.UtcNow);
            _messages.Add(vm);
            MessageAdded?.Invoke(vm);
            return vm;
        }

        /// <summary>인터랙티브 ask_user 카드 메시지를 추가한다. 사용자가 응답하면 ToolAskAnswered = true 로 갱신.</summary>
        public ChatMessageVM AddToolAskMessage(ToolUserAskMessage ask)
        {
            if (ask == null) return null;
            var vm = new ChatMessageVM(ChatSender.ToolAsk, ask.question ?? string.Empty, DateTime.UtcNow)
            {
                ToolUseId = ask.tool_use_id ?? string.Empty,
                AskPayload = ask,
            };
            _messages.Add(vm);
            MessageAdded?.Invoke(vm);
            return vm;
        }

        /// <summary>
        /// ToolAsk 카드의 응답 완료 상태를 잠금. summary 는 collapsed view 에서 보이는 응답 요약 텍스트.
        /// </summary>
        public void MarkToolAskAnswered(string toolUseId, string answerSummary)
        {
            if (string.IsNullOrEmpty(toolUseId)) return;
            foreach (var vm in _messages)
            {
                if (vm.Sender == ChatSender.ToolAsk && vm.ToolUseId == toolUseId)
                {
                    vm.ToolAskAnswered = true;
                    vm.ToolAskAnswerSummary = answerSummary ?? string.Empty;
                    MessageUpdated?.Invoke(vm);
                    return;
                }
            }
        }

        /// <summary>스트리밍 중인 어시스턴트 메시지의 본문을 갱신</summary>
        public void UpdateMessageText(ChatMessageVM target, string text)
        {
            if (target == null) return;
            target.Body = text ?? string.Empty;
            MessageUpdated?.Invoke(target);
        }

        public void ClearDraft() => Draft = string.Empty;

        public void Dispose()
        {
            MessageAdded = null;
            MessageUpdated = null;
            CanSendChanged = null;
            AgentInfoChanged = null;
            StatusChanged = null;
            HistoryReset = null;
        }
    }

    /// <summary>
    /// View 가 렌더링할 메시지 단위. 본문은 스트리밍 동안 변동.
    /// </summary>
    public sealed class ChatMessageVM
    {
        public ChatSender Sender { get; }
        public string Body { get; internal set; }
        public DateTime Time { get; }

        // ── ToolAsk 전용 페이로드 (Sender == ToolAsk 일 때만 유효) ──
        public string ToolUseId { get; internal set; }
        public bool ToolAskAnswered { get; internal set; }
        public string ToolAskAnswerSummary { get; internal set; }
        public ToolUserAskMessage AskPayload { get; internal set; }

        public ChatMessageVM(ChatSender sender, string body, DateTime time)
        {
            Sender = sender;
            Body = body ?? string.Empty;
            Time = time;
        }
    }
}
