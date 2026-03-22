using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Services;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// OpenClaw 설치 감지 — 탐색만, 설치/실행 없음
    /// - 크로스플랫폼 (Windows/macOS/Linux)
    /// - WSL2 환경 내 설치도 감지
    /// </summary>
    public class OpenClawDetector : IOpenClawDetector
    {
        // 플랫폼별 기본 설치 경로
        private static string DefaultConfigPath
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "openclaw", "openclaw.json"
                    );

                // macOS / Linux
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".openclaw", "openclaw.json"
                );
            }
        }

        private static string WorkspacePath
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "openclaw", "workspace"
                    );

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".openclaw", "workspace"
                );
            }
        }

        // WSL2 내부 경로 (Windows에서 WSL 파일시스템 접근)
        private static string WslConfigPath
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return null;

                // WSL 파일은 \\wsl$\ 또는 \\wsl.localhost\ 경로로 접근 가능
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var userName    = Path.GetFileName(userProfile);

                return $@"\\wsl.localhost\Ubuntu\home\{userName}\.openclaw\openclaw.json";
            }
        }

        public UniTask<bool> IsInstalledAsync(CancellationToken ct = default)
        {
            // 기본 경로 확인
            if (File.Exists(DefaultConfigPath) ||
                Directory.Exists(Path.GetDirectoryName(DefaultConfigPath)))
                return UniTask.FromResult(true);

            // WSL2 경로 확인 (Windows)
            var wslPath = WslConfigPath;
            if (wslPath != null)
            {
                try
                {
                    if (File.Exists(wslPath) ||
                        Directory.Exists(Path.GetDirectoryName(wslPath)))
                        return UniTask.FromResult(true);
                }
                catch
                {
                    // WSL 미설치 시 경로 접근 실패 — 무시
                }
            }

            return UniTask.FromResult(false);
        }

        public UniTask<string> GetInstallPathAsync(CancellationToken ct = default)
        {
            // 우선순위: workspace 내 yaml → 기본 json → WSL 내 json
            var candidates = new[]
            {
                Path.Combine(WorkspacePath, "openclaw.yaml"),
                Path.Combine(WorkspacePath, "config.yaml"),
                DefaultConfigPath,
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return UniTask.FromResult(path);
            }

            // WSL2 경로 확인
            var wslPath = WslConfigPath;
            if (wslPath != null)
            {
                try
                {
                    if (File.Exists(wslPath))
                        return UniTask.FromResult(wslPath);
                }
                catch { /* WSL 미설치 */ }
            }

            return UniTask.FromResult<string>(null);
        }

        public async UniTask<string> GetVersionAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var configPath = DefaultConfigPath;

                    // 기본 경로에 없으면 WSL 경로 시도
                    if (!File.Exists(configPath))
                    {
                        var wslPath = WslConfigPath;
                        if (wslPath != null && File.Exists(wslPath))
                            configPath = wslPath;
                        else
                            return null;
                    }

                    var json = File.ReadAllText(configPath);

                    // 간단한 버전 파싱 (정규식 없이)
                    var key = "\"version\"";
                    var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) return "unknown";

                    var start = json.IndexOf('"', idx + key.Length + 1) + 1;
                    var end   = json.IndexOf('"', start);
                    return start > 0 && end > start
                        ? json.Substring(start, end - start)
                        : "unknown";
                }
                catch
                {
                    return null;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<bool> IsGatewayListeningAsync(int port, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync("127.0.0.1", port);
                    return connectTask.Wait(500); // 500ms 타임아웃
                }
                catch
                {
                    return false;
                }
            }, cancellationToken: ct);
        }
    }
}
