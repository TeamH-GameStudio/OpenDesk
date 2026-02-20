using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// OpenClaw 자동 설치 — npm/brew를 통해 설치
    /// 진행률을 ReactiveProperty로 스트리밍
    /// </summary>
    public class OpenClawInstaller : IOpenClawInstaller, IDisposable
    {
        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;

        public async UniTask<bool> InstallAsync(CancellationToken ct = default)
        {
            try
            {
                // 1. 환경 확인
                SetProgress(0.05f, "환경 확인 중...");
                var hasNode = await CheckCommandExistsAsync("node", "--version", ct);
                if (!hasNode)
                {
                    SetProgress(0f, "Node.js가 필요합니다. nodejs.org에서 설치해주세요.");
                    return false;
                }

                // 2. npm으로 openclaw 설치
                SetProgress(0.2f, "OpenClaw 다운로드 중...");
                var installCmd = GetInstallCommand();
                var success    = await RunCommandAsync(installCmd.cmd, installCmd.args, ct);

                if (!success)
                {
                    SetProgress(0f, "설치에 실패했습니다. 인터넷 연결을 확인해주세요.");
                    return false;
                }

                // 3. Gateway 초기화
                SetProgress(0.8f, "AI 에이전트 초기화 중...");
                await UniTask.Delay(500, cancellationToken: ct);

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
                UnityEngine.Debug.LogError($"[Installer] 설치 실패: {ex}");
                return false;
            }
        }

        private (string cmd, string args) GetInstallCommand()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return ("npm", "install -g openclaw");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ("npm.cmd", "install -g openclaw");

            return ("npm", "install -g openclaw");
        }

        private async UniTask<bool> CheckCommandExistsAsync(
            string cmd, string args, CancellationToken ct)
        {
            return await RunCommandAsync(cmd, args, ct);
        }

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

                    process.WaitForExit(60_000); // 최대 60초
                    return process.ExitCode == 0;
                }
                catch
                {
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
