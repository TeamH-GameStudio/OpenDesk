using System;
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
    /// - 온보딩 완료 여부 확인 후 Gateway 접속
    /// - 이벤트 파이프라인: Bridge → State/SubAgent/ConsoleLog/CostMonitor
    /// - IDisposable로 정리 보장
    /// </summary>
    public class AppBootstrapper : IStartable, IDisposable
    {
        private readonly IOpenClawBridgeService _bridge;
        private readonly IAgentStateService     _agentState;
        private readonly ISubAgentService       _subAgent;
        private readonly IConsoleLogService     _consoleLog;
        private readonly ICostMonitorService    _costMonitor;

        private IDisposable _eventSubscription;
        private CancellationTokenSource _cts;

        private const string DefaultGatewayUrl = "ws://localhost:18789/events";

        public AppBootstrapper(
            IOpenClawBridgeService bridge,
            IAgentStateService     agentState,
            ISubAgentService       subAgent,
            IConsoleLogService     consoleLog,
            ICostMonitorService    costMonitor)
        {
            _bridge      = bridge;
            _agentState  = agentState;
            _subAgent    = subAgent;
            _consoleLog  = consoleLog;
            _costMonitor = costMonitor;
        }

        public void Start()
        {
            var isFirstRun = PlayerPrefs.GetInt("OpenDesk_IsFirstRun", 1) == 1;
            if (isFirstRun)
            {
                Debug.Log("[Boot] 온보딩 미완료 — AppBootstrapper 대기");
                return;
            }

            _cts = new CancellationTokenSource();
            BootAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid BootAsync(CancellationToken ct)
        {
            Debug.Log("[Boot] OpenDesk 시작");

            // 이벤트 스트림 구독 — Bridge → 모든 하위 서비스 연결
            _eventSubscription = _bridge.OnEventReceived.Subscribe(OnEventReceived);

            // 리소스 모니터링 시작 (백그라운드)
            _costMonitor.StartMonitoringAsync(ct).Forget();

            // Gateway 연결
            var gatewayUrl = PlayerPrefs.GetString("OpenDesk_GatewayUrl", DefaultGatewayUrl);

            try
            {
                await _bridge.ConnectAsync(gatewayUrl, ct);
                Debug.Log($"[Boot] Gateway 연결 완료: {gatewayUrl}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Boot] Gateway 초기 연결 실패 (자동 재연결 대기): {ex.Message}");
            }
        }

        private void OnEventReceived(AgentEvent e)
        {
            // 1. 에이전트 상태 업데이트
            _agentState.ApplyEvent(e);

            // 2. 콘솔 로그 기록
            _consoleLog.AddFromAgentEvent(e);

            // 3. 서브에이전트 처리
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

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _eventSubscription?.Dispose();
            _eventSubscription = null;
        }
    }
}
