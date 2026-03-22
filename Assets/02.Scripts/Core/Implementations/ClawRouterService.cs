using System;
using System.Diagnostics;
using System.IO;
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
    /// ClawRouter 스마트 라우터 관리
    /// - Gateway ↔ AI API 사이에서 요청 난이도를 14~15개 지표로 분석
    /// - 난이도에 맞는 최저가 모델로 자동 라우팅
    /// - Free 모드: Ollama 로컬 모델만 사용 (API 키 불필요, 완전 무료)
    /// </summary>
    public class ClawRouterService : IClawRouterService, IDisposable
    {
        private readonly IApiKeyVaultService _vault;

        private readonly ReactiveProperty<float>         _progress      = new(0f);
        private readonly ReactiveProperty<string>        _statusText    = new("");
        private readonly Subject<RoutingConfig>          _configChanged = new();

        public ReadOnlyReactiveProperty<float>  Progress   => _progress;
        public ReadOnlyReactiveProperty<string> StatusText => _statusText;
        public Observable<RoutingConfig> OnConfigChanged    => _configChanged;

        private const int ProcessTimeout = 60_000;

        public ClawRouterService(IApiKeyVaultService vault)
        {
            _vault = vault;
        }

        // ── 설치 확인 ───────────────────────────────────────────────────

        public async UniTask<bool> IsInstalledAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? "clawrouter.cmd"
                        : "clawrouter";

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
                    if (process == null) return false;

                    process.WaitForExit(5_000);
                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            }, cancellationToken: ct);
        }

        // ── 설치 ────────────────────────────────────────────────────────

        public async UniTask<bool> InstallAsync(CancellationToken ct = default)
        {
            try
            {
                SetProgress(0.1f, "ClawRouter 설치 중...");

                var npmCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "npm.cmd"
                    : "npm";

                var success = await RunCommandAsync(npmCmd, "install -g clawrouter", ct);

                if (!success)
                {
                    SetProgress(0f, "ClawRouter 설치 실패");
                    return false;
                }

                SetProgress(1f, "ClawRouter 설치 완료!");
                return true;
            }
            catch (Exception ex)
            {
                SetProgress(0f, $"ClawRouter 설치 오류: {ex.Message}");
                return false;
            }
        }

        // ── 현재 설정 조회 ──────────────────────────────────────────────

        public async UniTask<RoutingConfig> GetCurrentConfigAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var configPath = GetRouterConfigPath();
                    if (!File.Exists(configPath))
                    {
                        // 설정 없음 → 기본값 (Free 모드)
                        return new RoutingConfig
                        {
                            Mode              = RoutingMode.Free,
                            PrimaryModel      = "ollama/llama3",
                            UseLocalFallback  = true,
                            EstimatedMonthlyCost = 0m,
                        };
                    }

                    var yaml = File.ReadAllText(configPath);
                    return ParseRouterConfig(yaml);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Router] 설정 읽기 실패: {ex.Message}");
                    return new RoutingConfig { Mode = RoutingMode.Free };
                }
            }, cancellationToken: ct);
        }

        // ── 라우팅 모드 설정 ────────────────────────────────────────────

        public async UniTask<bool> SetRoutingModeAsync(RoutingMode mode, CancellationToken ct = default)
        {
            // Free 모드가 아닌데 API 키가 없으면 경고
            if (mode != RoutingMode.Free)
            {
                var canRunFree = await _vault.CanRunWithoutApiKeyAsync(ct);
                var statuses   = _vault.GetAllKeyStatuses();
                var hasAnyKey  = false;

                foreach (var s in statuses)
                {
                    if (s.Status == ApiKeyStatus.Valid)
                    {
                        hasAnyKey = true;
                        break;
                    }
                }

                if (!hasAnyKey && !canRunFree)
                {
                    Debug.LogWarning("[Router] API 키가 없습니다. Free 모드(Ollama)를 설정하거나 API 키를 입력해주세요.");
                    return false;
                }
            }

            var config = BuildConfigForMode(mode);

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var configPath = GetRouterConfigPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                    var yaml = GenerateRouterYaml(config);
                    File.WriteAllText(configPath, yaml);

                    Debug.Log($"[Router] 라우팅 모드 설정: {mode}");
                    _configChanged.OnNext(config);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Router] 설정 저장 실패: {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        // ── 예상 비용 ───────────────────────────────────────────────────

        public UniTask<decimal> GetEstimatedCostAsync(RoutingMode mode, CancellationToken ct = default)
        {
            // 일반적인 개인 사용 기준 예상 월 비용 (USD)
            var cost = mode switch
            {
                RoutingMode.Free    => 0m,        // Ollama 로컬 → 완전 무료
                RoutingMode.Eco     => 8m,        // $6~$13 범위
                RoutingMode.Auto    => 35m,       // $25~$50 범위
                RoutingMode.Premium => 150m,      // $100~$200+ 범위
                _                   => 0m,
            };

            return UniTask.FromResult(cost);
        }

        // ── 내부 유틸리티 ───────────────────────────────────────────────

        private static RoutingConfig BuildConfigForMode(RoutingMode mode)
        {
            return mode switch
            {
                RoutingMode.Free => new RoutingConfig
                {
                    Mode             = RoutingMode.Free,
                    PrimaryModel     = "ollama/llama3",
                    UseLocalFallback = true,
                    EstimatedMonthlyCost = 0m,
                },
                RoutingMode.Eco => new RoutingConfig
                {
                    Mode             = RoutingMode.Eco,
                    PrimaryModel     = "deepseek/deepseek-chat",
                    UseLocalFallback = true,
                    EstimatedMonthlyCost = 8m,
                },
                RoutingMode.Auto => new RoutingConfig
                {
                    Mode             = RoutingMode.Auto,
                    PrimaryModel     = "anthropic/claude-sonnet-4-6",
                    UseLocalFallback = true,
                    EstimatedMonthlyCost = 35m,
                },
                RoutingMode.Premium => new RoutingConfig
                {
                    Mode             = RoutingMode.Premium,
                    PrimaryModel     = "anthropic/claude-opus-4-6",
                    UseLocalFallback = false,
                    EstimatedMonthlyCost = 150m,
                },
                _ => new RoutingConfig { Mode = RoutingMode.Free },
            };
        }

        private static string GenerateRouterYaml(RoutingConfig config)
        {
            return $@"# OpenDesk ClawRouter 설정
# 자동 생성됨 — 수동 수정 시 덮어쓸 수 있음

routing:
  mode: {config.Mode.ToString().ToLower()}
  primary_model: {config.PrimaryModel}
  use_local_fallback: {config.UseLocalFallback.ToString().ToLower()}

tiers:
  light:
    models:
      - ollama/llama3
      - deepseek/deepseek-chat
      - google/gemini-flash-lite
  medium:
    models:
      - anthropic/claude-sonnet-4-6
      - openai/gpt-4o-mini
  heavy:
    models:
      - anthropic/claude-opus-4-6
      - openai/gpt-5.2
";
        }

        private static RoutingConfig ParseRouterConfig(string yaml)
        {
            var config = new RoutingConfig();

            // 간단한 YAML 파싱 (외부 라이브러리 없이)
            foreach (var line in yaml.Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("mode:"))
                {
                    var value = trimmed.Substring(5).Trim();
                    config.Mode = value switch
                    {
                        "free"    => RoutingMode.Free,
                        "eco"     => RoutingMode.Eco,
                        "auto"    => RoutingMode.Auto,
                        "premium" => RoutingMode.Premium,
                        _         => RoutingMode.Free,
                    };
                }
                else if (trimmed.StartsWith("primary_model:"))
                {
                    config.PrimaryModel = trimmed.Substring(14).Trim();
                }
                else if (trimmed.StartsWith("use_local_fallback:"))
                {
                    var value = trimmed.Substring(19).Trim();
                    config.UseLocalFallback = value == "true";
                }
            }

            return config;
        }

        private static string GetRouterConfigPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "openclaw", "router.yaml");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw", "router.yaml");
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
            _configChanged.Dispose();
        }
    }
}
