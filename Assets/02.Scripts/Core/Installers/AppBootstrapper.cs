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
    /// - CostMonitor 백그라운드 시작
    /// - DEPRECATED: OpenClaw Bridge 의존성 2026-04-27 제거됨. 이벤트 파이프라인은 IClaudeService 경로로 일원화.
    /// - 미들웨어 sub_agent_* 어댑팅은 별도 SubAgentEventBridge (Office scope) 가 담당 — 본 클래스는 Core scope.
    /// - IDisposable로 정리 보장
    /// </summary>
    public class AppBootstrapper : IStartable, IDisposable
    {
        // DEPRECATED: OpenClaw bridge fields removed 2026-04-27.
        // private readonly IOpenClawBridgeService _bridge;
        // private readonly IAgentStateService     _agentState;
        // private readonly ISubAgentService       _subAgent;
        // private readonly IConsoleLogService     _consoleLog;
        private readonly ICostMonitorService    _costMonitor;

        // private IDisposable _eventSubscription;
        private CancellationTokenSource _cts;

        // DEPRECATED: OpenClaw gateway URL no longer used.
        // private const string DefaultGatewayUrl = "ws://127.0.0.1:18789";

        public AppBootstrapper(ICostMonitorService costMonitor)
        {
            _costMonitor = costMonitor;
        }

        // DEPRECATED: Mock mode keyed off OpenClaw bridge — removed.
        // private static bool IsMockMode => PlayerPrefs.GetInt("OpenDesk_MockMode", 0) == 1;

        public void Start()
        {
            // DEPRECATED: 온보딩/Mock/Gateway 분기 제거. CostMonitor만 기동.
            // var isFirstRun = PlayerPrefs.GetInt("OpenDesk_IsFirstRun", 1) == 1;
            // if (isFirstRun) { Debug.Log("[Boot] 온보딩 미완료 — AppBootstrapper 대기"); return; }
            // if (IsMockMode) { MockBootAsync().Forget(); return; }

            Debug.Log("[Boot] OpenDesk 시작 — CostMonitor 기동");
            _cts = new CancellationTokenSource();
            _costMonitor.StartMonitoringAsync(_cts.Token).Forget();
        }

        // DEPRECATED: OpenClaw bridge 기반 Mock/Boot/OnEventReceived 흐름 비활성화 2026-04-27.
        // 채팅/에이전트 이벤트는 IClaudeService → ClaudeWebSocketClient 경로에서 처리됨.
        /*
        private async UniTaskVoid MockBootAsync()
        {
            Debug.Log("[Boot] Mock 시작 — 더미 데이터로 UI 구동");
            _eventSubscription = _bridge.OnEventReceived.Subscribe(OnEventReceived);
            _cts = new CancellationTokenSource();
            _costMonitor.StartMonitoringAsync(_cts.Token).Forget();
            await UniTask.Delay(500);
            _consoleLog.AddLog(new ConsoleLogEntry
            {
                Timestamp = DateTime.Now, Level = LogLevel.Info, Category = "System",
                RawMessage = "Mock 모드로 실행 중입니다. Gateway 연결 없이 UI를 테스트합니다.",
            });
            _consoleLog.AddLog(new ConsoleLogEntry
            {
                Timestamp = DateTime.Now, Level = LogLevel.Info, Category = "System",
                RawMessage = "실제 AI 에이전트 기능은 Mock 모드에서 사용할 수 없습니다.",
            });
            Debug.Log("[Boot] Mock 부팅 완료 — Office UI 사용 가능");
        }

        private async UniTaskVoid BootAsync(CancellationToken ct)
        {
            Debug.Log("[Boot] OpenDesk 시작");
            _eventSubscription = _bridge.OnEventReceived.Subscribe(OnEventReceived);
            _costMonitor.StartMonitoringAsync(ct).Forget();
            var savedToken = PlayerPrefs.GetString("OpenDesk_GatewayToken", "");
            if (!string.IsNullOrEmpty(savedToken))
            {
                _bridge.SetGatewayToken(savedToken);
                Debug.Log("[Boot] 저장된 Gateway 토큰 복원됨");
            }
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
            _agentState.ApplyEvent(e);
            _consoleLog.AddFromAgentEvent(e);
            switch (e.ActionType)
            {
                case AgentActionType.SubAgentSpawned:   _subAgent.OnSubAgentSpawned(e);   break;
                case AgentActionType.SubAgentCompleted: _subAgent.OnSubAgentCompleted(e); break;
                case AgentActionType.SubAgentFailed:    _subAgent.OnSubAgentFailed(e);    break;
            }
        }
        */

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            // DEPRECATED: OpenClaw bridge cleanup removed 2026-04-27.
            // _eventSubscription?.Dispose();
            // _eventSubscription = null;
            // _bridge.Dispose();
        }
    }
}
