namespace ChatTest.Models
{
    public enum ChatSender
    {
        User,
        Assistant
    }

    public sealed class ChatMessage
    {
        public int Id { get; }
        public ChatSender Sender { get; }
        public string Body { get; }

        public ChatMessage(int id, ChatSender sender, string body)
        {
            Id = id;
            Sender = sender;
            Body = body ?? string.Empty;
        }
    }
}
