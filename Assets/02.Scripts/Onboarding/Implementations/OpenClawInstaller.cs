using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Services;
using R3;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// OpenClaw 자동 설치 (명세서 1단계)
    /// 1. 관리자 권한 확인
    /// 2. Node.js 환경 확인/설치
    /// 3. WSL2 확인/활성화 (Windows)
    /// 4. install.sh / install.ps1 스크립트로 OpenClaw 설치
    /// 5. 데몬 등록 (openclaw onboard --install-daemon)
    /// 6. 설치 완료 → 관리자 권한 강등
    /// </summary>
    public class OpenClawInstaller : IOpenClawInstaller, IDisposable
    {
        private readonly INodeEnvironmentService _nodeEnv;
        private readonly IAdminPrivilegeService  _admin;

        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;

        private const int ProcessTimeout = 120_000; // 2분

        public OpenClawInstaller(
            INodeEnvironmentService nodeEnv,
            IAdminPrivilegeService  admin)
        {
            _nodeEnv = nodeEnv;
            _admin   = admin;
        }

        public async UniTask<bool> InstallAsync(CancellationToken ct = default)
        {
            try
            {
                // ── Step 1: 관리자 권한 확인 ─────────────────────────────

                SetProgress(0.02f, "권한 확인 중...");
                var isAdmin = await _admin.IsElevatedAsync(ct);

                if (!isAdmin)
                {
                    Debug.Log("[Installer] 관리자 권한 없음 — 일부 설치 단계가 제한될 수 있습니다.");
                }

                // ── Step 2: Node.js 확인/설치 ────────────────────────────

                SetProgress(0.05f, "Node.js 환경 확인 중...");
                var hasNode = await _nodeEnv.IsInstalledAsync(ct);

                if (!hasNode)
                {
                    SetProgress(0.08f, "Node.js 설치 중...");
                    var nodeInstalled = await _nodeEnv.InstallAsync(ct);
                    if (!nodeInstalled)
                    {
                        SetProgress(0f, "Node.js 설치 실패 — nodejs.org에서 수동 설치 후 다시 시도해주세요.");
                        return false;
                    }
                }
                else
                {
                    // 버전 체크 → 부족 시 자동 업그레이드
                    var meetsMin = await _nodeEnv.MeetsMinVersionAsync("22.16.0", ct);
                    if (!meetsMin)
                    {
                        SetProgress(0.08f, "필수 도구를 최신 버전으로 업데이트하고 있어요...");
                        var upgraded = await _nodeEnv.InstallAsync(ct);
                        if (!upgraded)
                        {
                            SetProgress(0f, "필수 도구 업데이트에 실패했습니다. 인터넷 연결을 확인해주세요.");
                            return false;
                        }
                    }
                }

                var nodeVersion = await _nodeEnv.GetVersionAsync(ct);
                Debug.Log($"[Installer] Node.js {nodeVersion} 확인됨");

                // ── Step 3: OpenClaw 설치 (install.sh / install.ps1) ────

                SetProgress(0.25f, "OpenClaw 다운로드 중...");

                bool installSuccess;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    installSuccess = await InstallViaScriptWindowsAsync(ct);
                else
                    installSuccess = await InstallViaScriptUnixAsync(ct);

                if (!installSuccess)
                {
                    // 스크립트 실패 시 npm fallback
                    Debug.Log("[Installer] 스크립트 설치 실패, npm 방식으로 재시도");
                    SetProgress(0.4f, "대체 방식으로 설치 중...");
                    installSuccess = await InstallViaNpmAsync(ct);
                }

                if (!installSuccess)
                {
                    SetProgress(0f, "OpenClaw 설치에 실패했습니다. 인터넷 연결을 확인해주세요.");
                    return false;
                }

                // ── Step 3.5: 설치 검증 ─────────────────────────────────

                SetProgress(0.55f, "설치 확인 중...");
                await UniTask.Delay(1000, cancellationToken: ct);

                var verifyCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "openclaw.cmd" : "openclaw";
                var verified = await RunCommandAsync(verifyCmd, "--version", ct);
                if (!verified)
                {
                    Debug.LogWarning("[Installer] openclaw --version 실패 — PATH 갱신 대기 후 재시도");
                    await UniTask.Delay(3000, cancellationToken: ct);
                    verified = await RunCommandAsync(verifyCmd, "--version", ct);
                }

                if (!verified)
                {
                    SetProgress(0f, "OpenClaw 설치는 완료됐으나 실행 확인에 실패했습니다.");
                    return false;
                }

                Debug.Log("[Installer] OpenClaw 설치 검증 완료");

                // ── Step 4: 데몬 등록 ────────────────────────────────────

                SetProgress(0.7f, "AI 에이전트 데몬 등록 중...");
                var daemonSuccess = await RegisterDaemonAsync(ct);
                if (!daemonSuccess)
                {
                    Debug.LogWarning("[Installer] 데몬 등록 실패 — 수동 시작 필요할 수 있음");
                }

                // ── Step 5: Gateway 시작 + 포트 검증 ──────────────────────

                SetProgress(0.85f, "AI 비서 서비스 시작 중...");

                // 데몬이 등록 안 됐어도 직접 Gateway 시작 시도
                var gwCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "openclaw.cmd" : "openclaw";
                _ = RunBackgroundProcessAsync(gwCmd, "gateway start");

                await UniTask.Delay(3000, cancellationToken: ct);

                // Gateway 포트 열림 확인 (최대 3회)
                bool gatewayReady = false;
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using var tcp = new System.Net.Sockets.TcpClient();
                        var connectTask = tcp.ConnectAsync("127.0.0.1", 18789);
                        connectTask.Wait(1000);
                        if (tcp.Connected)
                        {
                            gatewayReady = true;
                            break;
                        }
                    }
                    catch { /* 재시도 */ }
                    await UniTask.Delay(2000, cancellationToken: ct);
                }

                if (!gatewayReady)
                    Debug.LogWarning("[Installer] Gateway 포트(18789) 응답 없음 — 연결 단계에서 재시도 예정");

                // ── Step 6: 관리자 권한 강등 ─────────────────────────────

                if (isAdmin)
                {
                    SetProgress(0.95f, "보안 모드 전환 중...");
                    // 주의: 이 호출은 앱을 재시작시킴
                    // 실제 프로덕션에서는 설치 완료 플래그를 저장한 후 호출
                    Debug.Log("[Installer] 관리자 권한 강등 예정 (일반 사용자 모드 재시작)");
                    // 재시작은 OnboardingService에서 처리
                }

                SetProgress(1.0f, "설치 완료!");
                return true;
            }
            catch (OperationCanceledException)
            {
                SetProgress(0f, "설치가 취소되었습니다.");
                return false;
            }
            catch (Exception ex)
            {
                SetProgress(0f, $"오류: {ex.Message}");
                Debug.LogError($"[Installer] 설치 실패: {ex}");
                return false;
            }
        }

        // ── Windows: PowerShell 스크립트 설치 ────────────────────────────

        private async UniTask<bool> InstallViaScriptWindowsAsync(CancellationToken ct)
        {
            // 관리자 권한으로 PowerShell 스크립트 실행 (UAC 팝업)
            var result = await _admin.RunElevatedAsync(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"iwr -useb https://openclaw.ai/install.ps1 | iex\"",
                ct);
            return result.ExitCode == 0;
        }

        // ── macOS / Linux: bash 스크립트 설치 ───────────────────────────

        private async UniTask<bool> InstallViaScriptUnixAsync(CancellationToken ct)
        {
            var result = await _admin.RunElevatedAsync(
                "bash",
                "-c \"curl -fsSL https://openclaw.ai/install.sh | bash\"",
                ct);
            return result.ExitCode == 0;
        }

        // ── npm fallback ────────────────────────────────────────────────

        private async UniTask<bool> InstallViaNpmAsync(CancellationToken ct)
        {
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npm.cmd"
                : "npm";

            // npm global install도 관리자 권한 필요
            var result = await _admin.RunElevatedAsync(cmd, "install -g openclaw", ct);
            return result.ExitCode == 0;
        }

        // ── 데몬 등록 (비대화형 + 관리자 권한) ────────────────────────────

        private async UniTask<bool> RegisterDaemonAsync(CancellationToken ct)
        {
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "openclaw.cmd"
                : "openclaw";

            // 1단계: 설정 파일 생성 (비대화형, 일반 권한으로 충분)
            var configOk = await RunCommandAsync(cmd,
                "onboard --non-interactive --accept-risk --skip-channels --skip-skills --skip-search --auth-choice skip --flow quickstart",
                ct);
            if (!configOk)
                Debug.LogWarning("[Installer] OpenClaw 설정 파일 생성 실패");

            // 2단계: 데몬 서비스 등록 (관리자 권한 필요 — UAC)
            var daemonResult = await _admin.RunElevatedAsync(cmd,
                "onboard --non-interactive --accept-risk --install-daemon --skip-channels --skip-skills --skip-search --auth-choice skip --flow quickstart",
                ct);

            if (daemonResult.ExitCode != 0)
                Debug.LogWarning($"[Installer] 데몬 등록 결과: exit={daemonResult.ExitCode}");

            return configOk;  // 설정 파일만 있으면 Gateway 수동 시작 가능
        }

        // ── 백그라운드 프로세스 (Gateway 등 데몬 시작용) ─────────────────

        private static void RunBackgroundProcessAsync(string cmd, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = cmd,
                    Arguments       = args,
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };
                var process = Process.Start(psi);
                Debug.Log($"[Installer] 백그라운드 시작: {cmd} {args} (PID: {process?.Id})");
                // 프로세스를 닫지 않음 — 백그라운드에서 계속 실행
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Installer] 백그라운드 시작 실패: {cmd} {args} — {ex.Message}");
            }
        }

        // ── 프로세스 실행 유틸리티 ───────────────────────────────────────

        private UniTask<bool> RunCommandAsync(string cmd, string args, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = cmd,
                        Arguments              = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();

                    process.WaitForExit(ProcessTimeout);

                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning($"[Installer] 명령 실패 ({cmd}): {stderr}");
                        return false;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Installer] 명령 실행 오류 ({cmd}): {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        private void SetProgress(float value, string text)
        {
            _progress.Value   = value;
            _statusText.Value = text;
        }

        public void Dispose()
        {
            _progress.Dispose();
            _statusText.Dispose();
        }
    }
}
