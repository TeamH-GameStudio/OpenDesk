using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Services;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// 관리자 권한 획득/강등/체크
    /// 보안 핵심: 설치 시에만 관리자 권한 사용, 완료 즉시 강등
    /// </summary>
    public class AdminPrivilegeService : IAdminPrivilegeService
    {
        private const int ProcessTimeout = 120_000; // 2분

        public UniTask<bool> IsElevatedAsync(CancellationToken ct = default)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // net session 명령은 관리자 권한일 때만 성공 (exit code 0)
                        var psiAdmin = new ProcessStartInfo
                        {
                            FileName               = "net",
                            Arguments              = "session",
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true,
                            UseShellExecute        = false,
                            CreateNoWindow         = true,
                        };
                        using var proc = Process.Start(psiAdmin);
                        if (proc == null) return false;
                        proc.WaitForExit(5_000);
                        return proc.ExitCode == 0;
                    }

                    // macOS/Linux: uid 0 = root
                    var psi = new ProcessStartInfo
                    {
                        FileName               = "id",
                        Arguments              = "-u",
                        RedirectStandardOutput = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };
                    using var process = Process.Start(psi);
                    if (process == null) return false;

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5_000);
                    return output == "0";
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Admin] 권한 확인 실패: {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<ProcessOutput> RunElevatedAsync(
            string command, string arguments, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    ProcessStartInfo psi;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        // Windows: runas verb로 UAC 프롬프트 표시
                        psi = new ProcessStartInfo
                        {
                            FileName        = command,
                            Arguments       = arguments,
                            Verb            = "runas",
                            UseShellExecute = true,  // runas는 UseShellExecute 필요
                            CreateNoWindow  = false,
                        };
                    }
                    else
                    {
                        // macOS/Linux: sudo 사용
                        psi = new ProcessStartInfo
                        {
                            FileName               = "sudo",
                            Arguments              = $"{command} {arguments}",
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true,
                            UseShellExecute        = false,
                            CreateNoWindow         = true,
                        };
                    }

                    using var process = Process.Start(psi);
                    if (process == null)
                        return new ProcessOutput { ExitCode = -1, StdErr = "프로세스 시작 실패" };

                    string stdout = "";
                    string stderr = "";

                    if (psi.RedirectStandardOutput)
                    {
                        stdout = process.StandardOutput.ReadToEnd();
                        stderr = process.StandardError.ReadToEnd();
                    }

                    process.WaitForExit(ProcessTimeout);

                    return new ProcessOutput
                    {
                        ExitCode = process.ExitCode,
                        StdOut   = stdout,
                        StdErr   = stderr,
                    };
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // ERROR_CANCELLED (1223) = 사용자가 UAC에서 "아니오" 선택
                    Debug.LogWarning("[Admin] 사용자가 권한 요청을 거부했습니다.");
                    return new ProcessOutput
                    {
                        ExitCode = -1223,
                        StdErr   = "UAC_DENIED",
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Admin] 관리자 실행 실패: {ex.Message}");
                    return new ProcessOutput
                    {
                        ExitCode = -1,
                        StdErr   = ex.Message,
                    };
                }
            }, cancellationToken: ct);
        }

        public async UniTask DropPrivilegesAndRestartAsync(CancellationToken ct = default)
        {
            // 현재 프로세스가 관리자가 아니면 아무것도 안 함
            var isElevated = await IsElevatedAsync(ct);
            if (!isElevated)
            {
                Debug.Log("[Admin] 이미 일반 사용자 모드 — 강등 불필요");
                return;
            }

            Debug.Log("[Admin] 관리자 권한 강등 → 일반 사용자 모드로 재시작");

            await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // 현재 실행 파일을 일반 권한으로 재시작
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) return;

                    var psi = new ProcessStartInfo
                    {
                        FileName        = exePath,
                        UseShellExecute = true,
                        // Verb를 지정하지 않음 → 일반 사용자 권한
                    };

                    Process.Start(psi);

                    // 현재 관리자 프로세스 종료
                    Application.Quit();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Admin] 재시작 실패: {ex.Message}");
                }
            }, cancellationToken: ct);
        }
    }
}
