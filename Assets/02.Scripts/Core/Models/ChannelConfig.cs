namespace OpenDesk.Core.Models
{
    public enum ChannelType
    {
        Telegram,
        Discord,
        Slack,
        WhatsApp,
        Signal,
    }

    public enum ChannelStatus
    {
        NotConfigured,
        Connecting,
        Connected,
        Disconnected,
        Error,
    }

    /// <summary>메신저 채널 설정</summary>
    public class ChannelConfig
    {
        public ChannelType   Type         { get; set; }
        public string        DisplayName  { get; set; } = "";
        public string        Token        { get; set; } = "";   // 봇 토큰
        public ChannelStatus Status       { get; set; } = ChannelStatus.NotConfigured;
        public string        ErrorMessage { get; set; } = "";
        public string        SetupGuideUrl { get; set; } = "";  // 설정 방법 안내 URL
    }
}
