using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// 설치 기록 추적 + 롤백 실행
    /// 기록은 %APPDATA%/OpenDesk/installation_record.json에 저장
    /// </summary>
    public class RollbackService : IRollbackService
    {
        private InstallationRecord _record = new();
        private readonly string _recordPath;
        private const int ProcessTimeout = 60_000;

        public RollbackService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "OpenDesk");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _recordPath = Path.Combine(dir, "installation_record.json");

            LoadRecord();
        }

        // ── 기록 ──────────────────────────────────────────────────────

        public void RecordInstall(InstalledItem item)
        {
            item.InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 같은 Id가 이미 있으면 교체
            _record.Items.RemoveAll(x => x.Id == item.Id);
            _record.Items.Add(item);
            _record.LastUpdated = item.InstalledAt;

            if (string.IsNullOrEmpty(_record.CreatedAt))
                _record.CreatedAt = item.InstalledAt;

            SaveRecord();
            Debug.Log($"[Rollback] 설치 기록: {item.DisplayName} ({item.Method})");
        }

        public IReadOnlyList<InstalledItem> GetInstalledItems() => _record.Items;

        // ── 개별 롤백 ─────────────────────────────────────────────────

        public async UniTask<bool> RollbackItemAsync(string itemId, CancellationToken ct = default)
        {
            var item = _record.Items.Find(x => x.Id == itemId && !x.RolledBack);
            if (item == null)
            {
                Debug.LogWarning($"[Rollback] '{itemId}' 항목을 찾을 수 없거나 이미 롤백됨");
                return false;
            }

            if (!item.CanRollback)
            {
                Debug.LogWarning($"[Rollback] '{item.DisplayName}'은 자동 롤백이 지원되지 않습니다");
                return false;
            }

            Debug.Log($"[Rollback] 롤백 시작: {item.DisplayName}");
            bool success;

            switch (item.Id)
            {
                case "nodejs":
                    success = await RollbackNodeJsAsync(item, ct);
                    break;
                case "nodejs_nvm":
                    success = await RollbackNodeJsNvmAsync(item, ct);
                    break;
                case "nvm":
                    success = await RollbackNvmAsync(item, ct);
                    break;
                case "openclaw":
                    success = await RollbackOpenClawAsync(item, ct);
                    break;
                case "openclaw_daemon":
                    success = await RollbackOpenClawDaemonAsync(item, ct);
                    break;
                case "wsl2":
                    success = await RollbackWsl2Async(item, ct);
                    break;
                default:
                    Debug.LogWarning($"[Rollback] 알 수 없는 항목: {item.Id}");
                    success = false;
                    break;
            }

            if (success)
            {
                item.RolledBack = true;
                SaveRecord();
                Debug.Log($"[Rollback] 완료: {item.DisplayName}");
            }
            else
            {
                Debug.LogWarning($"[Rollback] 실패: {item.DisplayName}");
            }

            return success;
        }

        // ── 전체 롤백 (역순으로) ──────────────────────────────────────

        public async UniTask<bool> RollbackAllAsync(CancellationToken ct = default)
        {
            Debug.Log("[Rollback] === 전체 롤백 시작 ===");
            bool allOk = true;

            // 설치 역순으로 롤백 (데몬 → OpenClaw → Node.js → nvm → WSL2)
            var items = new List<InstalledItem>(_record.Items);
            items.Reverse();

            foreach (var item in items)
            {
                if (item.RolledBack || !item.CanRollback) continue;

                var ok = await RollbackItemAsync(item.Id, ct);
                if (!ok) allOk = false;
            }

            Debug.Log($"[Rollback] === 전체 롤백 {(allOk ? "완료" : "일부 실패")} ===");
            return allOk;
        }

        // ── 항목별 롤백 구현 ──────────────────────────────────────────

        private async UniTask<bool> RollbackNodeJsAsync(InstalledItem item, CancellationToken ct)
        {
            // MSI로 설치된 Node.js 제거
            if (item.Method == "msi")
            {
                // 이전 버전이 있었으면 → 이전 버전 재설치 안내
                if (!string.IsNullOrEmpty(item.PreviousState) && item.PreviousState != "미설치")
                {
                    Debug.Log($"[Rollback] Node.js 이전 버전: {item.PreviousState} — 수동 재설치 필요");
                }

                return await RunCommandAsync("msiexec.exe", "/x {NodeJS} /qn /norestart", ct)
                    || await RunCommandAsync("powershell.exe",
                        "-NoProfile -Command \"Get-WmiObject -Class Win32_Product | Where-Object { $_.Name -match 'Node.js' } | ForEach-Object { $_.Uninstall() }\"",
                        ct);
            }
            return false;
        }

        private async UniTask<bool> RollbackNodeJsNvmAsync(InstalledItem item, CancellationToken ct)
        {
            // nvm으로 설치한 특정 버전 제거
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "nvm.exe" : "nvm";
            var version = item.InstalledState; // e.g. "24.1.0"

            // 이전 버전으로 전환
            if (!string.IsNullOrEmpty(item.PreviousState) && item.PreviousState != "미설치")
            {
                await RunCommandAsync(cmd, $"use {item.PreviousState}", ct);
                Debug.Log($"[Rollback] Node.js → {item.PreviousState} 으로 복원");
            }

            return await RunCommandAsync(cmd, $"uninstall {version}", ct);
        }

        private async UniTask<bool> RollbackNvmAsync(InstalledItem item, CancellationToken ct)
        {
            // nvm-windows는 프로그램 추가/제거로만 삭제 가능
            Debug.Log("[Rollback] nvm-windows는 [설정 > 앱]에서 수동 제거가 필요합니다.");
            // 자동 제거 시도 (제어판 경유)
            return await RunCommandAsync("powershell.exe",
                "-NoProfile -Command \"Get-WmiObject -Class Win32_Product | Where-Object { $_.Name -match 'NVM' } | ForEach-Object { $_.Uninstall() }\"",
                ct);
        }

        private async UniTask<bool> RollbackOpenClawAsync(InstalledItem item, CancellationToken ct)
        {
            var npmCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "npm.cmd" : "npm";
            var ok = await RunCommandAsync(npmCmd, "uninstall -g openclaw", ct);

            // 설정 폴더도 제거 (~/.openclaw/)
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw");
            if (Directory.Exists(configDir))
            {
                try
                {
                    Directory.Delete(configDir, true);
                    Debug.Log($"[Rollback] OpenClaw 설정 폴더 삭제: {configDir}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Rollback] 설정 폴더 삭제 실패: {ex.Message}");
                }
            }

            return ok;
        }

        private async UniTask<bool> RollbackOpenClawDaemonAsync(InstalledItem item, CancellationToken ct)
        {
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "openclaw.cmd" : "openclaw";

            // Gateway 중지 (데몬 + 포그라운드 프로세스 모두)
            await RunCommandAsync(cmd, "gateway stop", ct);
            await RunCommandAsync(cmd, "daemon stop", ct);

            // 포그라운드 gateway run 프로세스가 남아있으면 킬
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await RunCommandAsync("taskkill", "/F /IM node.exe /FI \"WINDOWTITLE eq openclaw*\"", ct);

            return await RunCommandAsync(cmd, "daemon uninstall", ct);
        }

        private async UniTask<bool> RollbackWsl2Async(InstalledItem item, CancellationToken ct)
        {
            if (item.PreviousState == "활성화됨")
            {
                Debug.Log("[Rollback] WSL2는 이미 설치되어 있었으므로 롤백하지 않습니다.");
                return true;
            }

            Debug.Log("[Rollback] WSL2 비활성화 — 재부팅이 필요할 수 있습니다.");
            // Ubuntu 배포판 제거
            await RunCommandAsync("wsl", "--unregister Ubuntu", ct);

            // WSL 기능 비활성화 (관리자 권한 필요)
            return await RunCommandAsync("dism.exe",
                "/online /disable-feature /featurename:Microsoft-Windows-Subsystem-Linux /norestart",
                ct);
        }

        // ── 디스크 저장/로드 ──────────────────────────────────────────

        public void SaveRecord()
        {
            try
            {
                var json = JsonUtility.ToJson(_record, true);
                File.WriteAllText(_recordPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rollback] 기록 저장 실패: {ex.Message}");
            }
        }

        public void LoadRecord()
        {
            try
            {
                if (File.Exists(_recordPath))
                {
                    var json = File.ReadAllText(_recordPath);
                    _record = JsonUtility.FromJson<InstallationRecord>(json) ?? new InstallationRecord();
                    Debug.Log($"[Rollback] 설치 기록 로드: {_record.Items.Count}개 항목");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rollback] 기록 로드 실패: {ex.Message}");
                _record = new InstallationRecord();
            }
        }

        // ── 유틸리티 ──────────────────────────────────────────────────

        private static UniTask<bool> RunCommandAsync(string cmd, string args, CancellationToken ct)
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
                    Debug.LogWarning($"[Rollback] 명령 실행 실패 ({cmd}): {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }
    }
}
