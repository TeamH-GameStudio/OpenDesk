using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace OpenDesk.Core.Installers
{
    /// <summary>
    /// 앱 시작 시 자동 실행되는 진입점
    /// 서비스들을 연결하고 OpenClaw Gateway에 접속
    /// </summary>
    public class AppBootstrapper : IStartable
    {
        private readonly IOpenClawBridgeService _bridge;
        private readonly IEventParserService    _parser;
        private readonly IAgentStateService     _agentState;
        private readonly ISubAgentService       _subAgent;

        // OpenClaw Gateway 기본 주소 (설정에서 변경 가능)
        private const string DefaultGatewayUrl = "ws://localhost:18800/events";

        public AppBootstrapper(
            IOpenClawBridgeService bridge,
            IEventParserService    parser,
            IAgentStateService     agentState,
            ISubAgentService       subAgent)
        {
            _bridge     = bridge;
            _parser     = parser;
            _agentState = agentState;
            _subAgent   = subAgent;
        }

        public void Start()
        {
            BootAsync(CancellationToken.None).Forget();
        }

        private async UniTaskVoid BootAsync(CancellationToken ct)
        {
            Debug.Log("[Boot] OpenDesk 시작");

            // 이벤트 스트림 구독 — Bridge → State/SubAgent 연결
            _bridge.OnEventReceived.Subscribe(e => OnEventReceived(e));

            // Gateway 연결
            var gatewayUrl = PlayerPrefs.GetString("OpenDesk_GatewayUrl", DefaultGatewayUrl);

            try
            {
                await _bridge.ConnectAsync(gatewayUrl, ct);
                Debug.Log($"[Boot] Gateway 연결 완료: {gatewayUrl}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Boot] Gateway 연결 실패 (나중에 재시도): {ex.Message}");
            }
        }

        private void OnEventReceived(AgentEvent e)
        {
            // 1. 에이전트 상태 업데이트
            _agentState.ApplyEvent(e);

            // 2. 서브에이전트 처리
            switch (e.ActionType)
            {
                case AgentActionType.SubAgentSpawned:
                    _subAgent.OnSubAgentSpawned(e);
                    break;
                case AgentActionType.SubAgentCompleted:
                    _subAgent.OnSubAgentCompleted(e);
                    break;
                case AgentActionType.SubAgentFailed:
                    _subAgent.OnSubAgentFailed(e);
                    break;
            }
        }
    }
}
