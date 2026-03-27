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
                string tempScriptPath = null;
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
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        // macOS: osascript로 네이티브 인증 다이얼로그 표시
                        // ProcessStartInfo.Arguments 토크나이저가 따옴표 이스케이프를
                        // 깨뜨리므로, 임시 AppleScript 파일을 생성하여 우회
                        var shellCmd = $"{command} {arguments}";
                        // elevated shell은 Unity CWD에 접근 불가 + PATH가 최소한이므로
                        // cd /tmp 및 PATH 확장을 삽입
                        var wrappedCmd = "export PATH=/usr/local/bin:/opt/homebrew/bin:$PATH && cd /tmp && " + shellCmd;
                        var escapedCmd = wrappedCmd
                            .Replace("\\", "\\\\")
                            .Replace("\"", "\\\"");

                        tempScriptPath = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            $"opendesk_admin_{Guid.NewGuid():N}.applescript");
                        System.IO.File.WriteAllText(tempScriptPath,
                            $"do shell script \"{escapedCmd}\" with administrator privileges");

                        Debug.Log($"[Admin] osascript 실행: {shellCmd}");

                        psi = new ProcessStartInfo
                        {
                            FileName               = "osascript",
                            Arguments              = tempScriptPath,
                            RedirectStandardOutput = true,
                            RedirectStandardError  = true,
                            UseShellExecute        = false,
                            CreateNoWindow         = true,
                        };
                    }
                    else
                    {
                        // Linux: pkexec (PolicyKit GUI 인증) → 없으면 sudo 폴백
                        var pkexecPath = "/usr/bin/pkexec";
                        if (System.IO.File.Exists(pkexecPath))
                        {
                            psi = new ProcessStartInfo
                            {
                                FileName               = pkexecPath,
                                Arguments              = $"{command} {arguments}",
                                RedirectStandardOutput = true,
                                RedirectStandardError  = true,
                                UseShellExecute        = false,
                                CreateNoWindow         = true,
                            };
                        }
                        else
                        {
                            // pkexec 없으면 sudo 폴백 (터미널 환경에서만 동작)
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

                    if (tempScriptPath != null)
                    {
                        try { System.IO.File.Delete(tempScriptPath); } catch { /* ignore */ }
                    }

                    var output = new ProcessOutput
                    {
                        ExitCode = process.ExitCode,
                        StdOut   = stdout,
                        StdErr   = stderr,
                    };

                    if (output.ExitCode != 0)
                        Debug.LogWarning($"[Admin] 관리자 명령 실패 (exit {output.ExitCode}): {output.StdErr}");

                    return output;
                }
                catch (Exception ex)
                {
                    if (tempScriptPath != null)
                    {
                        try { System.IO.File.Delete(tempScriptPath); } catch { /* ignore */ }
                    }
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
