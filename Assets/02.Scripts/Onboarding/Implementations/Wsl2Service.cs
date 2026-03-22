using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// WSL2 (Windows Subsystem for Linux) 감지 및 활성화
    /// Windows 전용 — 비Windows 환경에서는 모든 메서드가 즉시 성공 반환
    /// </summary>
    public class Wsl2Service : IWsl2Service, IDisposable
    {
        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;

        private const int ProcessTimeout = 300_000; // 5분 (WSL 설치는 오래 걸림)

        public async UniTask<bool> IsEnabledAsync(CancellationToken ct = default)
        {
            // Windows가 아니면 WSL 불필요 → 항상 true
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true;

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "wsl",
                        Arguments              = "--status",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10_000);

                    // wsl --status 성공 + "WSL 2" 또는 "Default Version: 2" 포함
                    if (process.ExitCode != 0) return false;

                    return output.Contains("2") ||
                           output.Contains("WSL 2") ||
                           output.Contains("Default Version: 2");
                }
                catch
                {
                    return false;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<IReadOnlyList<string>> GetDistributionsAsync(CancellationToken ct = default)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Array.Empty<string>();

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "wsl",
                        Arguments              = "--list --quiet",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return (IReadOnlyList<string>)Array.Empty<string>();

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10_000);

                    if (process.ExitCode != 0) return (IReadOnlyList<string>)Array.Empty<string>();

                    var distros = output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => d.Trim())
                        .Where(d => !string.IsNullOrEmpty(d))
                        .ToList();

                    return (IReadOnlyList<string>)distros;
                }
                catch
                {
                    return (IReadOnlyList<string>)Array.Empty<string>();
                }
            }, cancellationToken: ct);
        }

        public async UniTask<Wsl2InstallResult> EnableAsync(CancellationToken ct = default)
        {
            // Windows가 아니면 불필요
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new Wsl2InstallResult { Success = true, Message = "Windows가 아님 — WSL 불필요" };

            // 이미 활성화되어 있으면 스킵
            if (await IsEnabledAsync(ct))
            {
                SetProgress(1f, "WSL2 이미 활성화됨");
                return new Wsl2InstallResult { Success = true, Message = "이미 활성화됨" };
            }

            try
            {
                SetProgress(0.1f, "WSL2 설치 중... (관리자 권한 필요)");

                var success = await RunWslInstallAsync(ct);

                if (!success)
                {
                    SetProgress(0f, "WSL2 설치 실패 — 관리자 권한을 확인해주세요.");
                    return new Wsl2InstallResult
                    {
                        Success = false,
                        Message = "WSL2 설치 실패"
                    };
                }

                SetProgress(0.8f, "WSL2 설치 확인 중...");

                // wsl --install은 대부분 재부팅 필요
                var isEnabled = await IsEnabledAsync(ct);

                if (isEnabled)
                {
                    SetProgress(1f, "WSL2 설치 완료!");
                    return new Wsl2InstallResult { Success = true };
                }

                // 활성화 안 됐으면 재부팅 필요
                SetProgress(1f, "WSL2 설치 완료 — 재부팅 후 적용됩니다.");
                return new Wsl2InstallResult
                {
                    Success     = true,
                    NeedsReboot = true,
                    Message     = "WSL2가 설치되었습니다. 컴퓨터를 재부팅한 후 다시 실행해주세요."
                };
            }
            catch (OperationCanceledException)
            {
                SetProgress(0f, "설치가 취소되었습니다.");
                return new Wsl2InstallResult { Success = false, Message = "취소됨" };
            }
            catch (Exception ex)
            {
                SetProgress(0f, $"WSL2 설치 오류: {ex.Message}");
                Debug.LogError($"[WSL2] 설치 실패: {ex}");
                return new Wsl2InstallResult { Success = false, Message = ex.Message };
            }
        }

        private UniTask<bool> RunWslInstallAsync(CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // wsl --install (Windows 10 2004+ / Windows 11)
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "wsl",
                        Arguments              = "--install --no-launch",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;

                    process.WaitForExit(ProcessTimeout);

                    if (process.ExitCode == 0) return true;

                    // 대체: DISM 방식 (구형 Windows 10)
                    Debug.Log("[WSL2] wsl --install 실패, DISM 방식 시도");

                    var dismPsi = new ProcessStartInfo
                    {
                        FileName               = "dism.exe",
                        Arguments              = "/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var dismProcess = Process.Start(dismPsi);
                    if (dismProcess == null) return false;

                    dismProcess.WaitForExit(ProcessTimeout);

                    // VirtualMachinePlatform도 활성화
                    var vmPsi = new ProcessStartInfo
                    {
                        FileName               = "dism.exe",
                        Arguments              = "/online /enable-feature /featurename:VirtualMachinePlatform /all /norestart",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var vmProcess = Process.Start(vmPsi);
                    vmProcess?.WaitForExit(ProcessTimeout);

                    return dismProcess.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WSL2] 설치 명령 실패: {ex.Message}");
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
