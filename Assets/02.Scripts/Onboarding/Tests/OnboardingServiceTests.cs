using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using OpenDesk.Onboarding.Implementations;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using R3;

namespace OpenDesk.Onboarding.Tests
{
    /// <summary>
    /// OnboardingService 테스트
    /// 모든 외부 의존성은 Mock으로 대체 — Unity 엔진 없이 실행 가능
    /// </summary>
    public class OnboardingServiceTests
    {
        // ── Mock 구현체들 ────────────────────────────────────────────────────

        private class MockDetector : IOpenClawDetector
        {
            public bool   Installed    = true;
            public string InstallPath  = "/mock/path/openclaw.yaml";
            public string Version      = "1.0.0";
            public bool   GatewayListening = true;

            public UniTask<bool>   IsInstalledAsync(System.Threading.CancellationToken ct)
                => UniTask.FromResult(Installed);
            public UniTask<string> GetInstallPathAsync(System.Threading.CancellationToken ct)
                => UniTask.FromResult(InstallPath);
            public UniTask<string> GetVersionAsync(System.Threading.CancellationToken ct)
                => UniTask.FromResult(Version);
            public UniTask<bool>   IsGatewayListeningAsync(int port, System.Threading.CancellationToken ct)
                => UniTask.FromResult(GatewayListening);
        }

        private class MockInstaller : IOpenClawInstaller
        {
            public bool ShouldSucceed = true;
            public ReadOnlyReactiveProperty<float>  Progress   { get; }
                = new ReactiveProperty<float>(0f);
            public ReadOnlyReactiveProperty<string> StatusText { get; }
                = new ReactiveProperty<string>("");
            public UniTask<bool> InstallAsync(System.Threading.CancellationToken ct)
                => UniTask.FromResult(ShouldSucceed);
        }

        private class MockParser : IAgentConfigParser
        {
            public List<AgentConfig> Agents = new()
            {
                new AgentConfig { SessionId = "main", Name = "팀장", Role = "main" }
            };
            public IReadOnlyList<AgentConfig> ParseFromFile(string path) => Agents;
            public IReadOnlyList<AgentConfig> ParseFromString(string yaml) => Agents;
        }

        private class MockSettings : IOnboardingSettings
        {
            public bool   IsFirstRun       { get; set; } = true;
            public string SavedGatewayUrl  { get; set; } = "ws://localhost:18800/events";
            public string SavedLocalPath   { get; set; } = "";
            public int    AppVersion       { get; set; } = 0;
            public string CompletedGateway { get; private set; }
            public string CompletedPath    { get; private set; }

            public void MarkOnboardingComplete(string url, string path)
            {
                IsFirstRun       = false;
                CompletedGateway = url;
                CompletedPath    = path;
            }
            public void ClearAll() { IsFirstRun = true; }
        }

        private class MockBridge : IOpenClawBridgeService
        {
            public bool ShouldConnect = true;
            public bool IsConnected   { get; private set; }
            public ReadOnlyReactiveProperty<bool> ConnectionState { get; }
                = new ReactiveProperty<bool>(false);
            public Observable<AgentEvent> OnEventReceived { get; }
                = new Subject<AgentEvent>();

            public async UniTask ConnectAsync(string url, System.Threading.CancellationToken ct)
            {
                if (!ShouldConnect) throw new System.Exception("Mock 연결 실패");
                IsConnected = true;
                await UniTask.CompletedTask;
            }
            public UniTask DisconnectAsync() { IsConnected = false; return UniTask.CompletedTask; }
            public UniTask SendMessageAsync(string s, string m, System.Threading.CancellationToken ct)
                => UniTask.CompletedTask;
            public void Dispose() { }
        }

        private class MockWorkspace : IWorkspaceService
        {
            public string LocalPath     { get; private set; } = "";
            public bool   IsInitialized => !string.IsNullOrEmpty(LocalPath);
            public Observable<WorkspaceEntry> OnEntryChanged { get; }
                = new Subject<WorkspaceEntry>();

            public void SetLocalPath(string path) { LocalPath = path; }
            public UniTask<IReadOnlyList<WorkspaceEntry>> GetEntriesAsync(
                System.Threading.CancellationToken ct)
                => UniTask.FromResult<IReadOnlyList<WorkspaceEntry>>(new List<WorkspaceEntry>());
            public void OpenEntry(WorkspaceEntry entry) { }
            public void Dispose() { }
        }

        // ── 테스트 ───────────────────────────────────────────────────────────

        private OnboardingService CreateService(
            MockDetector  detector  = null,
            MockInstaller installer = null,
            MockParser    parser    = null,
            MockSettings  settings  = null,
            MockBridge    bridge    = null,
            MockWorkspace workspace = null)
        {
            return new OnboardingService(
                detector  ?? new MockDetector(),
                installer ?? new MockInstaller(),
                parser    ?? new MockParser(),
                settings  ?? new MockSettings(),
                bridge    ?? new MockBridge(),
                workspace ?? new MockWorkspace()
            );
        }

        [Test]
        public async Task Start_최초실행_OpenClaw설치됨_Completed상태()
        {
            var svc = CreateService();
            await svc.StartAsync();

            Assert.AreEqual(OnboardingState.WorkspaceSetup, svc.CurrentState);
            Assert.AreEqual(1, svc.Context.DetectedAgents.Count);
        }

        [Test]
        public async Task Start_재방문_Gateway연결성공_Completed()
        {
            var settings = new MockSettings { IsFirstRun = false };
            var svc = CreateService(settings: settings);

            await svc.StartAsync();

            Assert.AreEqual(OnboardingState.Completed, svc.CurrentState);
        }

        [Test]
        public async Task Start_OpenClaw미설치_NotFound상태()
        {
            var detector = new MockDetector { Installed = false };
            var svc      = CreateService(detector: detector);

            await svc.StartAsync();

            Assert.AreEqual(OnboardingState.OpenClawNotFound, svc.CurrentState);
        }

        [Test]
        public async Task Retry_설치성공_WorkspaceSetup로이동()
        {
            var detector  = new MockDetector  { Installed = false };
            var installer = new MockInstaller { ShouldSucceed = true };
            var svc       = CreateService(detector: detector, installer: installer);

            await svc.StartAsync();
            Assert.AreEqual(OnboardingState.OpenClawNotFound, svc.CurrentState);

            // 설치 후 Detector가 설치된 것으로 응답하도록 변경
            detector.Installed = true;
            await svc.RetryCurrentStepAsync();

            Assert.AreEqual(OnboardingState.WorkspaceSetup, svc.CurrentState);
        }

        [Test]
        public async Task GatewayFail_오프라인모드진입_Completed()
        {
            var bridge = new MockBridge { ShouldConnect = false };
            var svc    = CreateService(bridge: bridge);

            await svc.StartAsync();
            Assert.AreEqual(OnboardingState.GatewayFailed, svc.CurrentState);

            await svc.EnterOfflineMode();
            Assert.AreEqual(OnboardingState.ReadyToEnter, svc.CurrentState);
            Assert.IsTrue(svc.Context.IsOfflineMode);
        }

        [Test]
        public async Task SkipWorkspace_ReadyToEnter상태()
        {
            var svc = CreateService();
            await svc.StartAsync();

            Assert.AreEqual(OnboardingState.WorkspaceSetup, svc.CurrentState);

            await svc.SkipWorkspaceSetupAsync();

            Assert.AreEqual(OnboardingState.ReadyToEnter, svc.CurrentState);
            Assert.IsTrue(svc.Context.WorkspaceSkipped);
        }

        [Test]
        public async Task NoAgents_기본Main에이전트추가됨()
        {
            var parser = new MockParser { Agents = new List<AgentConfig>() };
            var svc    = CreateService(parser: parser);

            await svc.StartAsync();

            Assert.AreEqual(1, svc.Context.DetectedAgents.Count);
            Assert.AreEqual("main", svc.Context.DetectedAgents[0].SessionId);
        }

        [Test]
        public async Task MarkComplete_설정저장됨()
        {
            var settings = new MockSettings();
            var svc      = CreateService(settings: settings);

            await svc.StartAsync();
            await svc.SkipWorkspaceSetupAsync();

            Assert.IsFalse(settings.IsFirstRun);
            Assert.IsNotEmpty(settings.CompletedGateway);
        }
    }
}
