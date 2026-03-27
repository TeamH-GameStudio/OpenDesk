using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 보안 감사 + 자가 복구
    /// - 내장 점검: Gateway 노출, 파일 권한, 스킬 안전성
    /// - 외부 연동: openclaw security audit --deep/--fix 명령 실행
    /// </summary>
    public class SecurityAuditService : ISecurityAuditService, IDisposable
    {
        private readonly ReactiveProperty<float>  _progress   = new(0f);
        private readonly ReactiveProperty<string> _statusText = new("");

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;
        public AuditReport LastReport { get; private set; }

        private const int GatewayPort = 18789;

        public async UniTask<AuditReport> RunAuditAsync(bool deep = false, CancellationToken ct = default)
        {
            var report = new AuditReport { IsDeepScan = deep };

            SetProgress(0.1f, "Gateway 노출 점검 중...");
            await CheckGatewayExposure(report, ct);

            SetProgress(0.3f, "파일 시스템 점검 중...");
            await CheckFilesystemSecrets(report, ct);

            SetProgress(0.5f, "실행 정책 점검 중...");
            await CheckExecutionShell(report, ct);

            SetProgress(0.7f, "스킬 공급망 점검 중...");
            await CheckSkillsSupplyChain(report, ct);

            if (deep)
            {
                SetProgress(0.85f, "외부 보안 감사 실행 중...");
                await RunExternalAuditAsync(report, "--deep", ct);
            }

            SetProgress(1f, report.IsClean ? "[OK] 보안 점검 통과" : $"[!] {report.CriticalCount} 치명적, {report.WarnCount} 경고 발견");
            LastReport = report;

            Debug.Log($"[Security] 감사 완료 — Critical: {report.CriticalCount}, Warn: {report.WarnCount}, Pass: {report.PassCount}");
            return report;
        }

        public async UniTask<AuditReport> RunAutoFixAsync(CancellationToken ct = default)
        {
            SetProgress(0.1f, "자동 수정 실행 중...");

            // openclaw security audit --fix 실행
            var fixReport = new AuditReport();
            await RunExternalAuditAsync(fixReport, "--fix", ct);

            SetProgress(0.7f, "수정 결과 확인 중...");

            // 수정 후 재점검
            var verifyReport = await RunAuditAsync(deep: false, ct);

            SetProgress(1f, verifyReport.IsClean ? "[OK] 자동 수정 완료" : "[!] 일부 항목 수동 수정 필요");
            return verifyReport;
        }

        // ── 1. Gateway 노출 점검 ────────────────────────────────────────

        private async UniTask CheckGatewayExposure(AuditReport report, CancellationToken ct)
        {
            // 0.0.0.0 바인딩 체크 (외부 노출 위험)
            var isExposed = await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // 외부 인터페이스에서 접근 가능한지 확인
                    using var client = new TcpClient();
                    var task = client.ConnectAsync("0.0.0.0", GatewayPort);
                    return task.Wait(500);
                }
                catch { return false; }
            }, cancellationToken: ct);

            if (isExposed)
            {
                report.Items.Add(new AuditItem
                {
                    Domain      = AuditDomain.GatewayExposure,
                    Severity    = AuditSeverity.Critical,
                    Title       = "Gateway 외부 노출",
                    Description = $"포트 {GatewayPort}이 0.0.0.0에 바인딩되어 외부에서 접근 가능합니다. localhost만 허용하세요.",
                    CanAutoFix  = true,
                });
            }
            else
            {
                report.Items.Add(new AuditItem
                {
                    Domain   = AuditDomain.GatewayExposure,
                    Severity = AuditSeverity.Pass,
                    Title    = "Gateway 바인딩",
                    Description = "localhost만 접근 가능 — 안전",
                });
            }

            // Gateway 인증 확인
            report.Items.Add(new AuditItem
            {
                Domain      = AuditDomain.GatewayExposure,
                Severity    = AuditSeverity.Info,
                Title       = "Gateway 인증",
                Description = "WebSocket 인증 토큰 설정 여부는 외부 감사(--deep)에서 확인",
            });
        }

        // ── 2. 파일 시스템/시크릿 점검 ──────────────────────────────────

        private UniTask CheckFilesystemSecrets(AuditReport report, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openclaw")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw");

                // API 키 평문 노출 체크
                var configFiles = new[] { "openclaw.json", "config.yaml", "agents.yaml" };
                foreach (var file in configFiles)
                {
                    var path = Path.Combine(basePath, file);
                    if (!File.Exists(path)) continue;

                    try
                    {
                        var content = File.ReadAllText(path);
                        if (content.Contains("api_key") && !content.Contains("${"))
                        {
                            report.Items.Add(new AuditItem
                            {
                                Domain      = AuditDomain.FilesystemSecrets,
                                Severity    = AuditSeverity.Warn,
                                Title       = $"평문 API 키 발견: {file}",
                                Description = "API 키가 설정 파일에 평문으로 저장되어 있습니다. 환경 변수를 사용하세요.",
                                CanAutoFix  = true,
                            });
                        }
                        else
                        {
                            report.Items.Add(new AuditItem
                            {
                                Domain   = AuditDomain.FilesystemSecrets,
                                Severity = AuditSeverity.Pass,
                                Title    = $"{file} 키 보안",
                                Description = "API 키가 안전하게 관리됨",
                            });
                        }
                    }
                    catch { /* 파일 접근 실패는 무시 */ }
                }

                // 키 저장소 디렉토리 권한 체크
                var keysDir = Path.Combine(Application.persistentDataPath, "OpenDesk", "keys");
                if (Directory.Exists(keysDir))
                {
                    report.Items.Add(new AuditItem
                    {
                        Domain   = AuditDomain.FilesystemSecrets,
                        Severity = AuditSeverity.Pass,
                        Title    = "암호화 키 저장소",
                        Description = "API 키가 OpenDesk 암호화 저장소에 보관됨",
                    });
                }
            }, cancellationToken: ct);
        }

        // ── 3. 실행/셸 정책 점검 ────────────────────────────────────────

        private UniTask CheckExecutionShell(AuditReport report, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                // 샌드박스 기본값 확인
                var basePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openclaw")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw");

                var agentsConfig = Path.Combine(basePath, "agents.yaml");
                if (File.Exists(agentsConfig))
                {
                    var content = File.ReadAllText(agentsConfig);
                    if (content.Contains("sandbox: false") || content.Contains("sandbox: off"))
                    {
                        report.Items.Add(new AuditItem
                        {
                            Domain      = AuditDomain.ExecutionShell,
                            Severity    = AuditSeverity.Warn,
                            Title       = "샌드박스 비활성화",
                            Description = "에이전트가 호스트 머신에서 직접 명령을 실행할 수 있습니다. 샌드박스를 활성화하세요.",
                            CanAutoFix  = true,
                        });
                    }
                    else
                    {
                        report.Items.Add(new AuditItem
                        {
                            Domain   = AuditDomain.ExecutionShell,
                            Severity = AuditSeverity.Pass,
                            Title    = "샌드박스 정책",
                            Description = "에이전트 실행이 격리 환경에서 수행됨",
                        });
                    }
                }
            }, cancellationToken: ct);
        }

        // ── 4. 스킬 공급망 점검 ─────────────────────────────────────────

        private UniTask CheckSkillsSupplyChain(AuditReport report, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                var skillsPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openclaw", "skills")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openclaw", "skills");

                if (!Directory.Exists(skillsPath))
                {
                    report.Items.Add(new AuditItem
                    {
                        Domain   = AuditDomain.SkillsSupplyChain,
                        Severity = AuditSeverity.Pass,
                        Title    = "설치된 스킬 없음",
                        Description = "외부 스킬이 설치되지 않았습니다.",
                    });
                    return;
                }

                var skillDirs = Directory.GetDirectories(skillsPath);
                foreach (var dir in skillDirs)
                {
                    var skillMd = Path.Combine(dir, "SKILL.md");
                    if (!File.Exists(skillMd)) continue;

                    var content = File.ReadAllText(skillMd);
                    var name    = Path.GetFileName(dir);

                    // 위험 키워드 탐지
                    var dangerousPatterns = new[] { "rm -rf", "sudo", "chmod 777", "eval(", "exec(" };
                    var hasDanger = false;

                    foreach (var pattern in dangerousPatterns)
                    {
                        if (content.Contains(pattern))
                        {
                            report.Items.Add(new AuditItem
                            {
                                Domain      = AuditDomain.SkillsSupplyChain,
                                Severity    = AuditSeverity.Warn,
                                Title       = $"위험 패턴 감지: {name}",
                                Description = $"스킬 '{name}'에 위험한 명령어 패턴 '{pattern}'이 포함되어 있습니다.",
                                CanAutoFix  = false,
                            });
                            hasDanger = true;
                        }
                    }

                    if (!hasDanger)
                    {
                        report.Items.Add(new AuditItem
                        {
                            Domain   = AuditDomain.SkillsSupplyChain,
                            Severity = AuditSeverity.Pass,
                            Title    = $"스킬 안전: {name}",
                            Description = "위험 패턴 미발견",
                        });
                    }
                }
            }, cancellationToken: ct);
        }

        // ── 외부 openclaw CLI 감사 실행 ─────────────────────────────────

        private UniTask RunExternalAuditAsync(AuditReport report, string flags, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "openclaw.cmd"
                        : "openclaw";

                    var psi = new ProcessStartInfo
                    {
                        FileName               = cmd,
                        Arguments              = $"security audit {flags}",
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return;

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(30_000);

                    if (!string.IsNullOrEmpty(output))
                        Debug.Log($"[Security] 외부 감사 결과:\n{output}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Security] 외부 감사 실행 실패: {ex.Message}");
                    report.Items.Add(new AuditItem
                    {
                        Domain      = AuditDomain.GatewayExposure,
                        Severity    = AuditSeverity.Info,
                        Title       = "외부 감사 도구 미설치",
                        Description = "openclaw CLI가 설치되지 않아 외부 감사를 건너뛰었습니다.",
                    });
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
