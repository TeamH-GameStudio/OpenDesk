using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// 메신저 채널 연동 관리
    /// - Telegram, Discord, Slack, WhatsApp, Signal
    /// - 봇 토큰 입력 → 설정 파일 자동 수정 → 통신 개시
    /// </summary>
    public interface IChannelService
    {
        /// <summary>지원 채널 목록 + 현재 상태</summary>
        IReadOnlyList<ChannelConfig> GetChannels();

        /// <summary>채널 연결 설정 (토큰 입력 → 설정 파일 수정)</summary>
        UniTask<bool> ConfigureChannelAsync(ChannelType type, string token, CancellationToken ct = default);

        /// <summary>채널 연결 해제</summary>
        UniTask<bool> DisconnectChannelAsync(ChannelType type, CancellationToken ct = default);

        /// <summary>채널 연결 테스트</summary>
        UniTask<ChannelStatus> TestConnectionAsync(ChannelType type, CancellationToken ct = default);

        /// <summary>채널 상태 변경 이벤트</summary>
        Observable<ChannelConfig> OnChannelStatusChanged { get; }
    }
}
