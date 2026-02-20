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
    /// </summary>
    public class OpenClawDetector : IOpenClawDetector
    {
        // 플랫폼별 기본 설치 경로
        private static string DefaultConfigPath
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".openclaw", "openclaw.json"
                    );

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "openclaw", "openclaw.json"
                    );

                // Linux
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".openclaw", "workspace"
                    );

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "openclaw", "workspace"
                );
            }
        }

        public UniTask<bool> IsInstalledAsync(CancellationToken ct = default)
        {
            var result = File.Exists(DefaultConfigPath) ||
                         Directory.Exists(Path.GetDirectoryName(DefaultConfigPath));
            return UniTask.FromResult(result);
        }

        public UniTask<string> GetInstallPathAsync(CancellationToken ct = default)
        {
            // yaml config는 workspace 하위에 있음
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

            return UniTask.FromResult<string>(null);
        }

        public async UniTask<string> GetVersionAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // openclaw.json에서 버전 읽기
                    if (!File.Exists(DefaultConfigPath)) return null;

                    var json = File.ReadAllText(DefaultConfigPath);
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
