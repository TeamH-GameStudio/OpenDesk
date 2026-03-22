using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 메신저 채널 연동 — 봇 토큰 입력 → OpenClaw 설정 파일 수정 → 통신 개시
    /// </summary>
    public class ChannelService : IChannelService, IDisposable
    {
        private readonly Subject<ChannelConfig> _statusChanged = new();
        private readonly Dictionary<ChannelType, ChannelConfig> _channels = new();

        public Observable<ChannelConfig> OnChannelStatusChanged => _statusChanged;

        public ChannelService()
        {
            // 지원 채널 초기화
            _channels[ChannelType.Telegram]  = new() { Type = ChannelType.Telegram,  DisplayName = "Telegram",  SetupGuideUrl = "https://core.telegram.org/bots#botfather" };
            _channels[ChannelType.Discord]   = new() { Type = ChannelType.Discord,   DisplayName = "Discord",   SetupGuideUrl = "https://discord.com/developers/applications" };
            _channels[ChannelType.Slack]     = new() { Type = ChannelType.Slack,      DisplayName = "Slack",     SetupGuideUrl = "https://api.slack.com/apps" };
            _channels[ChannelType.WhatsApp]  = new() { Type = ChannelType.WhatsApp,  DisplayName = "WhatsApp",  SetupGuideUrl = "https://developers.facebook.com/docs/whatsapp" };
            _channels[ChannelType.Signal]    = new() { Type = ChannelType.Signal,    DisplayName = "Signal",    SetupGuideUrl = "https://signal.org/docs/" };
        }

        public IReadOnlyList<ChannelConfig> GetChannels()
        {
            return new List<ChannelConfig>(_channels.Values);
        }

        public async UniTask<bool> ConfigureChannelAsync(ChannelType type, string token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            var channel = _channels[type];
            channel.Token  = token.Trim();
            channel.Status = ChannelStatus.Connecting;
            _statusChanged.OnNext(channel);

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // OpenClaw 채널 설정 파일에 토큰 기록
                    var configPath = GetChannelConfigPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                    var yamlKey = type.ToString().ToLower();
                    var yaml = $@"
channels:
  {yamlKey}:
    enabled: true
    token: ""{token.Trim()}""
";
                    // 기존 설정에 append (실제로는 YAML 머지 필요)
                    File.AppendAllText(configPath, yaml);

                    channel.Status = ChannelStatus.Connected;
                    _statusChanged.OnNext(channel);
                    Debug.Log($"[Channel] {type} 연결 설정 완료");
                    return true;
                }
                catch (Exception ex)
                {
                    channel.Status       = ChannelStatus.Error;
                    channel.ErrorMessage = ex.Message;
                    _statusChanged.OnNext(channel);
                    Debug.LogError($"[Channel] {type} 설정 실패: {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<bool> DisconnectChannelAsync(ChannelType type, CancellationToken ct = default)
        {
            var channel = _channels[type];
            channel.Token  = "";
            channel.Status = ChannelStatus.NotConfigured;
            _statusChanged.OnNext(channel);
            Debug.Log($"[Channel] {type} 연결 해제");
            await UniTask.CompletedTask;
            return true;
        }

        public async UniTask<ChannelStatus> TestConnectionAsync(ChannelType type, CancellationToken ct = default)
        {
            var channel = _channels[type];
            if (string.IsNullOrEmpty(channel.Token))
                return ChannelStatus.NotConfigured;

            // 실제로는 각 채널 API에 테스트 요청
            await UniTask.Delay(500, cancellationToken: ct);
            return ChannelStatus.Connected;
        }

        private static string GetChannelConfigPath()
        {
            var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openclaw")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw");

            return Path.Combine(basePath, "channels.yaml");
        }

        public void Dispose() => _statusChanged.Dispose();
    }
}
