using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
    /// Node.js 환경 감지 및 자동 설치
    /// - Windows: 공식 MSI 사일런트 설치
    /// - macOS: 공식 pkg 설치
    /// - Linux: NodeSource 스크립트
    /// </summary>
    public class NodeEnvironmentService : INodeEnvironmentService, IDisposable
    {
        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;

        private const string MinVersion     = "22.16.0";
        private const string TargetVersion  = "24.1.0";   // 권장 버전
        private const int    ProcessTimeout = 120_000;     // 2분

        public async UniTask<bool> IsInstalledAsync(CancellationToken ct = default)
        {
            var version = await GetVersionAsync(ct);
            return version != null;
        }

        public async UniTask<string> GetVersionAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "node.exe" : "node";
                    var psi = new ProcessStartInfo
                    {
                        FileName               = cmd,
                        Arguments              = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return null;

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5_000);

                    if (process.ExitCode != 0) return null;

                    // "v24.1.0" → "24.1.0"
                    return output.StartsWith("v") ? output.Substring(1) : output;
                }
                catch
                {
                    return null;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<bool> MeetsMinVersionAsync(string minVersion = "22.16.0", CancellationToken ct = default)
        {
            var current = await GetVersionAsync(ct);
            if (current == null) return false;

            return CompareVersions(current, minVersion) >= 0;
        }

        public async UniTask<bool> InstallAsync(CancellationToken ct = default)
        {
            try
            {
                SetProgress(0.05f, "Node.js 설치 준비 중...");

                // 이미 설치되어 있고 버전이 충분하면 스킵
                if (await MeetsMinVersionAsync(MinVersion, ct))
                {
                    var ver = await GetVersionAsync(ct);
                    SetProgress(1f, $"Node.js {ver} 이미 설치됨");
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return await InstallWindowsAsync(ct);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return await InstallMacAsync(ct);

                return await InstallLinuxAsync(ct);
            }
            catch (OperationCanceledException)
            {
                SetProgress(0f, "설치가 취소되었습니다.");
                return false;
            }
            catch (Exception ex)
            {
                SetProgress(0f, $"Node.js 설치 실패: {ex.Message}");
                Debug.LogError($"[NodeEnv] 설치 실패: {ex}");
                return false;
            }
        }

        // ── Windows: MSI 사일런트 설치 ──────────────────────────────────

        private async UniTask<bool> InstallWindowsAsync(CancellationToken ct)
        {
            SetProgress(0.1f, "Node.js 설치 파일 다운로드 중...");

            var msiUrl  = $"https://nodejs.org/dist/v{TargetVersion}/node-v{TargetVersion}-x64.msi";
            var msiPath = Path.Combine(Path.GetTempPath(), $"node-v{TargetVersion}-x64.msi");

            // 다운로드
            var downloaded = await DownloadFileAsync(msiUrl, msiPath, ct);
            if (!downloaded)
            {
                SetProgress(0f, "Node.js 다운로드 실패 — 인터넷 연결을 확인해주세요.");
                return false;
            }

            SetProgress(0.5f, "Node.js 설치 중... (잠시 기다려주세요)");

            // 사일런트 설치 (관리자 권한 필요)
            var success = await RunCommandAsync(
                "msiexec.exe",
                $"/i \"{msiPath}\" /qn /norestart",
                ct);

            // 설치 파일 정리
            try { File.Delete(msiPath); } catch { /* 무시 */ }

            if (!success)
            {
                SetProgress(0f, "Node.js 설치 실패 — 관리자 권한이 필요합니다.");
                return false;
            }

            SetProgress(0.9f, "Node.js 설치 확인 중...");

            // PATH 갱신 대기
            await UniTask.Delay(2000, cancellationToken: ct);

            // 설치 확인
            var installed = await IsInstalledAsync(ct);
            SetProgress(installed ? 1f : 0f,
                installed ? $"Node.js {TargetVersion} 설치 완료!" : "Node.js 설치 확인 실패");
            return installed;
        }

        // ── macOS: pkg 설치 ─────────────────────────────────────────────

        private async UniTask<bool> InstallMacAsync(CancellationToken ct)
        {
            SetProgress(0.1f, "Node.js 설치 파일 다운로드 중...");

            var pkgUrl  = $"https://nodejs.org/dist/v{TargetVersion}/node-v{TargetVersion}.pkg";
            var pkgPath = Path.Combine(Path.GetTempPath(), $"node-v{TargetVersion}.pkg");

            var downloaded = await DownloadFileAsync(pkgUrl, pkgPath, ct);
            if (!downloaded)
            {
                SetProgress(0f, "Node.js 다운로드 실패");
                return false;
            }

            SetProgress(0.5f, "Node.js 설치 중...");

            var success = await RunCommandAsync(
                "sudo",
                $"installer -pkg \"{pkgPath}\" -target /",
                ct);

            try { File.Delete(pkgPath); } catch { /* 무시 */ }

            if (!success)
            {
                SetProgress(0f, "Node.js 설치 실패");
                return false;
            }

            SetProgress(1f, "Node.js 설치 완료!");
            return true;
        }

        // ── Linux: NodeSource 설치 ──────────────────────────────────────

        private async UniTask<bool> InstallLinuxAsync(CancellationToken ct)
        {
            SetProgress(0.3f, "Node.js 설치 중...");

            // NodeSource 설치 스크립트 사용
            var success = await RunCommandAsync(
                "bash",
                "-c \"curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash - && sudo apt-get install -y nodejs\"",
                ct);

            if (!success)
            {
                SetProgress(0f, "Node.js 설치 실패");
                return false;
            }

            SetProgress(1f, "Node.js 설치 완료!");
            return true;
        }

        // ── 유틸리티 ────────────────────────────────────────────────────

        private async UniTask<bool> DownloadFileAsync(string url, string destPath, CancellationToken ct)
        {
            return await UniTask.RunOnThreadPool(async () =>
            {
                try
                {
                    using var client   = new HttpClient();
                    client.Timeout     = TimeSpan.FromMinutes(5);
                    using var response = await client.GetAsync(url, ct);
                    response.EnsureSuccessStatusCode();

                    using var fs = File.Create(destPath);
                    await response.Content.CopyToAsync(fs);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NodeEnv] 다운로드 실패: {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
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

                    process.WaitForExit(ProcessTimeout);
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NodeEnv] 명령 실행 실패: {cmd} {args} — {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        /// <summary>시맨틱 버전 비교: a >= b → 양수, a == b → 0, a &lt; b → 음수</summary>
        private static int CompareVersions(string a, string b)
        {
            var partsA = a.Split('.');
            var partsB = b.Split('.');
            var len = Math.Max(partsA.Length, partsB.Length);

            for (int i = 0; i < len; i++)
            {
                int va = i < partsA.Length && int.TryParse(partsA[i], out var pa) ? pa : 0;
                int vb = i < partsB.Length && int.TryParse(partsB[i], out var pb) ? pb : 0;
                if (va != vb) return va - vb;
            }
            return 0;
        }

        // ── 기존 Node.js 프로젝트 스캔 ──────────────────────────────────

        public async UniTask<IReadOnlyList<string>> ScanExistingProjectsAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                var results = new System.Collections.Generic.List<string>();
                try
                {
                    // 사용자 주요 폴더에서 package.json 검색 (깊이 2단계까지)
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string[] scanRoots =
                    {
                        Path.Combine(userProfile, "Documents"),
                        Path.Combine(userProfile, "Desktop"),
                        Path.Combine(userProfile, "Downloads"),
                        Path.Combine(userProfile, "source"),         // Visual Studio 기본
                        Path.Combine(userProfile, "projects"),
                        Path.Combine(userProfile, "dev"),
                    };

                    foreach (var root in scanRoots)
                    {
                        if (!Directory.Exists(root)) continue;
                        ScanForPackageJson(root, results, 0, maxDepth: 2);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NodeEnv] 프로젝트 스캔 중 오류: {ex.Message}");
                }
                return (IReadOnlyList<string>)results;
            }, cancellationToken: ct);
        }

        private static void ScanForPackageJson(string dir, System.Collections.Generic.List<string> results,
            int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            try
            {
                var packageJson = Path.Combine(dir, "package.json");
                if (File.Exists(packageJson))
                {
                    // node_modules가 있으면 활성 프로젝트
                    var nodeModules = Path.Combine(dir, "node_modules");
                    var marker = File.Exists(Path.Combine(dir, "package-lock.json")) ||
                                 File.Exists(Path.Combine(dir, "yarn.lock")) ||
                                 Directory.Exists(nodeModules);
                    if (marker)
                        results.Add(dir);
                    return; // 이 폴더는 프로젝트이므로 하위 탐색 불필요
                }

                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    // 숨김 폴더, node_modules, .git 등 제외
                    if (name.StartsWith(".") || name == "node_modules" || name == "__pycache__")
                        continue;
                    ScanForPackageJson(subDir, results, depth + 1, maxDepth);
                }
            }
            catch { /* 접근 권한 없는 폴더 무시 */ }
        }

        // ── nvm을 통한 안전 설치 (기존 버전과 공존) ────────────────────

        public async UniTask<bool> InstallViaNvmAsync(CancellationToken ct = default)
        {
            try
            {
                SetProgress(0.05f, "버전 관리 도구 확인 중...");

                // nvm-windows 설치 여부 확인
                var hasNvm = await RunCommandAsync(
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nvm.exe" : "nvm",
                    "version", ct);

                if (!hasNvm)
                {
                    // nvm-windows 다운로드 및 설치
                    SetProgress(0.1f, "버전 관리 도구를 설치하고 있어요...");

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var nvmUrl = "https://github.com/coreybutler/nvm-windows/releases/latest/download/nvm-setup.exe";
                        var nvmPath = Path.Combine(Path.GetTempPath(), "nvm-setup.exe");

                        var downloaded = await DownloadFileAsync(nvmUrl, nvmPath, ct);
                        if (!downloaded)
                        {
                            SetProgress(0f, "버전 관리 도구 다운로드 실패");
                            return false;
                        }

                        SetProgress(0.25f, "버전 관리 도구 설치 중... (설치 창이 뜨면 '다음'을 계속 눌러주세요)");
                        // nvm-setup.exe는 사일런트 설치 미지원 → 사용자가 Next 클릭 필요
                        var installOk = await UniTask.RunOnThreadPool(() =>
                        {
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName        = nvmPath,
                                    UseShellExecute = true,  // GUI 설치창 표시
                                };
                                using var process = Process.Start(psi);
                                process?.WaitForExit(300_000); // 5분 대기
                                return process?.ExitCode == 0;
                            }
                            catch { return false; }
                        }, cancellationToken: ct);

                        try { File.Delete(nvmPath); } catch { }

                        if (!installOk)
                        {
                            SetProgress(0f, "버전 관리 도구 설치가 취소되었어요.");
                            return false;
                        }
                    }
                    else
                    {
                        // macOS/Linux: curl로 nvm 설치
                        var ok = await RunCommandAsync("bash",
                            "-c \"curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh | bash\"",
                            ct);
                        if (!ok)
                        {
                            SetProgress(0f, "버전 관리 도구 설치 실패");
                            return false;
                        }
                    }
                }

                // nvm으로 Node.js 24 설치
                SetProgress(0.5f, $"Node.js {TargetVersion} 설치 중... (기존 버전은 유지돼요)");
                var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nvm.exe" : "nvm";

                var installNode = await RunCommandAsync(cmd, $"install {TargetVersion}", ct);
                if (!installNode)
                {
                    SetProgress(0f, "Node.js 설치에 실패했어요.");
                    return false;
                }

                // 새 버전을 기본으로 설정
                SetProgress(0.8f, "새 버전을 활성화하고 있어요...");
                await RunCommandAsync(cmd, $"use {TargetVersion}", ct);

                // 확인
                SetProgress(0.9f, "설치 확인 중...");
                await UniTask.Delay(2000, cancellationToken: ct);

                var installed = await MeetsMinVersionAsync(MinVersion, ct);
                SetProgress(installed ? 1f : 0f,
                    installed
                        ? $"Node.js {TargetVersion} 안전 설치 완료! (기존 버전도 유지됨)"
                        : "설치 확인 실패");
                return installed;
            }
            catch (OperationCanceledException)
            {
                SetProgress(0f, "설치가 취소되었습니다.");
                return false;
            }
            catch (Exception ex)
            {
                SetProgress(0f, $"설치 실패: {ex.Message}");
                Debug.LogError($"[NodeEnv] nvm 설치 실패: {ex}");
                return false;
            }
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
