using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    public interface IOpenClawBridgeService : IDisposable
    {
        // 연결 상태
        bool IsConnected { get; }
        ReadOnlyReactiveProperty<bool> ConnectionState { get; }

        // 이벤트 스트림 — 수신되는 모든 에이전트 이벤트
        Observable<AgentEvent> OnEventReceived { get; }

        // 연결 / 해제
        UniTask ConnectAsync(string gatewayUrl, CancellationToken ct = default);
        UniTask DisconnectAsync();

        // 메시지 전송 (캐릭터 클릭 → 에이전트에게 직접 입력)
        UniTask SendMessageAsync(string sessionId, string message, CancellationToken ct = default);
    }
}
