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
                    // 버전 체크
                    var meetsMin = await _nodeEnv.MeetsMinVersionAsync("22.16.0", ct);
                    if (!meetsMin)
                    {
                        var ver = await _nodeEnv.GetVersionAsync(ct);
                        SetProgress(0f, $"Node.js {ver} → 22.16 이상 필요합니다. 업데이트 후 다시 시도해주세요.");
                        return false;
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

                // ── Step 4: 데몬 등록 ────────────────────────────────────

                SetProgress(0.7f, "AI 에이전트 데몬 등록 중...");
                var daemonSuccess = await RegisterDaemonAsync(ct);
                if (!daemonSuccess)
                {
                    Debug.LogWarning("[Installer] 데몬 등록 실패 — 수동 시작 필요할 수 있음");
                    // 데몬 등록 실패는 치명적이지 않음 — 계속 진행
                }

                // ── Step 5: Gateway 시작 대기 ────────────────────────────

                SetProgress(0.85f, "Gateway 시작 대기 중...");
                await UniTask.Delay(2000, cancellationToken: ct);

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
            return await RunCommandAsync(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"iwr -useb https://openclaw.ai/install.ps1 | iex\"",
                ct);
        }

        // ── macOS / Linux: bash 스크립트 설치 ───────────────────────────

        private async UniTask<bool> InstallViaScriptUnixAsync(CancellationToken ct)
        {
            return await RunCommandAsync(
                "bash",
                "-c \"curl -fsSL https://openclaw.ai/install.sh | bash\"",
                ct);
        }

        // ── npm fallback ────────────────────────────────────────────────

        private async UniTask<bool> InstallViaNpmAsync(CancellationToken ct)
        {
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "npm.cmd"
                : "npm";

            return await RunCommandAsync(cmd, "install -g openclaw", ct);
        }

        // ── 데몬 등록 ───────────────────────────────────────────────────

        private async UniTask<bool> RegisterDaemonAsync(CancellationToken ct)
        {
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "openclaw.cmd"
                : "openclaw";

            return await RunCommandAsync(cmd, "onboard --install-daemon", ct);
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
