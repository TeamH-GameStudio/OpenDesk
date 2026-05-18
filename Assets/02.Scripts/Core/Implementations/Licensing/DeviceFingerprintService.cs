using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services.Licensing;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Implementations.Licensing
{
    /// <summary>
    /// macOS: ioreg -rd1 -c IOPlatformExpertDevice → IOPlatformUUID 파싱.
    /// Windows: wmic csproduct get uuid (또는 PowerShell Get-CimInstance Win32_ComputerSystemProduct).
    /// 외부 명령 실패 시 PlayerPrefs 기반 GUID fallback 으로 같은 머신에서 안정성 유지.
    /// </summary>
    public sealed class DeviceFingerprintService : IDeviceFingerprintService
    {
        private const string PlayerPrefsKey = "OpenDesk_License_Fingerprint";
        private const string FallbackGuidKey = "OpenDesk_License_FallbackGuid";
        private const int ProcessTimeoutMs = 3000;

        private string _cached;

        [Inject]
        public DeviceFingerprintService() { }

        public async UniTask<string> GetFingerprintAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(_cached))
            {
                return _cached;
            }

            var cached = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(cached))
            {
                _cached = cached;
                return cached;
            }

            var rawId = await UniTask.RunOnThreadPool(() => ReadPlatformUuid(), cancellationToken: ct);
            if (string.IsNullOrEmpty(rawId))
            {
                rawId = GetOrCreateFallbackGuid();
            }

            var hash = Sha256Hex(rawId);
            PlayerPrefs.SetString(PlayerPrefsKey, hash);
            PlayerPrefs.Save();
            _cached = hash;
            return hash;
        }

        public string GetSuggestedDeviceName()
        {
            var host = Environment.MachineName;
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "OpenDesk";
            }
            if (string.IsNullOrWhiteSpace(user))
            {
                return host;
            }
            return $"{host} ({user})";
        }

        private static string ReadPlatformUuid()
        {
            try
            {
                if (Application.platform == RuntimePlatform.OSXEditor
                    || Application.platform == RuntimePlatform.OSXPlayer)
                {
                    return ReadMacOsPlatformUuid();
                }
                if (Application.platform == RuntimePlatform.WindowsEditor
                    || Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    return ReadWindowsPlatformUuid();
                }
                if (Application.platform == RuntimePlatform.LinuxEditor
                    || Application.platform == RuntimePlatform.LinuxPlayer)
                {
                    return ReadLinuxMachineId();
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DeviceFingerprint] platform read failed: {ex.Message}");
            }
            return string.Empty;
        }

        private static string ReadMacOsPlatformUuid()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/sbin/ioreg",
                Arguments = "-rd1 -c IOPlatformExpertDevice",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var output = RunProcess(psi);
            if (string.IsNullOrEmpty(output))
            {
                return string.Empty;
            }

            // 줄: "IOPlatformUUID" = "ABCD-1234-..."
            const string token = "\"IOPlatformUUID\"";
            var tokenIdx = output.IndexOf(token, StringComparison.Ordinal);
            if (tokenIdx < 0) return string.Empty;
            var start = output.IndexOf('"', tokenIdx + token.Length);
            if (start < 0) return string.Empty;
            start++;
            var end = output.IndexOf('"', start);
            if (end < 0) return string.Empty;
            return output.Substring(start, end - start).Trim();
        }

        private static string ReadWindowsPlatformUuid()
        {
            // PowerShell 이 더 안정적. WMIC 는 deprecated 지만 fallback 으로 시도.
            var ps = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-CimInstance -ClassName Win32_ComputerSystemProduct).UUID\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var output = RunProcess(ps);
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }

            var wmic = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "csproduct get uuid",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var wmicOutput = RunProcess(wmic);
            if (string.IsNullOrWhiteSpace(wmicOutput)) return string.Empty;

            // wmic 출력: "UUID\r\n{actual-uuid}\r\n"
            foreach (var line in wmicOutput.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("UUID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return trimmed;
            }
            return string.Empty;
        }

        private static string ReadLinuxMachineId()
        {
            const string path = "/etc/machine-id";
            try
            {
                if (System.IO.File.Exists(path))
                {
                    return System.IO.File.ReadAllText(path).Trim();
                }
            }
            catch (Exception)
            {
                // ignore
            }
            return string.Empty;
        }

        private static string RunProcess(ProcessStartInfo psi)
        {
            try
            {
                using var process = Process.Start(psi);
                if (process == null) return string.Empty;
                if (!process.WaitForExit(ProcessTimeoutMs))
                {
                    try { process.Kill(); } catch { /* swallow */ }
                    return string.Empty;
                }
                return process.StandardOutput.ReadToEnd();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[DeviceFingerprint] process failed ({psi.FileName}): {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetOrCreateFallbackGuid()
        {
            var existing = PlayerPrefs.GetString(FallbackGuidKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }
            var guid = Guid.NewGuid().ToString();
            PlayerPrefs.SetString(FallbackGuidKey, guid);
            PlayerPrefs.Save();
            return guid;
        }

        private static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
