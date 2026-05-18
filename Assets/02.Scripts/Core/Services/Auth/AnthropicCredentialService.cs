using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Services.Auth
{
    /// <summary>
    /// Base64 파일 기반 자격증명 저장소. {persistentDataPath}/OpenDesk/anthropic-credentials.json
    /// PluginCredentialService 와 동일 보호 수준. OS 키체인 업그레이드는 ApiKeyVaultService 와 함께 일괄 처리.
    /// OAuth 토큰은 본 서비스가 보관하지 않고, Claude CLI 가 OpenDeskPaths.ClaudeConfigDir 에 직접 저장하는 것을
    /// 파일 존재 여부로만 감지한다.
    /// </summary>
    public class AnthropicCredentialService : IAnthropicCredentialService, IDisposable
    {
        private const string ApiKeyField = "anthropic_api_key";

        private readonly Subject<AuthCredentialChange> _onChanged = new();
        private readonly object _gate = new();
        private readonly Func<string> _credentialFileProvider;
        private readonly Func<string> _claudeConfigDirProvider;
        private string _cachedApiKey;
        private bool _apiKeyLoaded;

        public Observable<AuthCredentialChange> OnChanged => _onChanged;

        // VContainer picks the constructor with the most parameters by default,
        // which would force it to resolve Func<string> (not registered). Mark
        // the parameterless ctor with [Inject] so the container uses it; the
        // Func<string> overload remains available for tests/manual wiring.
        [Inject]
        public AnthropicCredentialService() : this(DefaultCredentialFile, DefaultClaudeConfigDir) { }

        public AnthropicCredentialService(Func<string> credentialFileProvider, Func<string> claudeConfigDirProvider)
        {
            _credentialFileProvider = credentialFileProvider ?? DefaultCredentialFile;
            _claudeConfigDirProvider = claudeConfigDirProvider ?? DefaultClaudeConfigDir;
        }

        public bool HasApiKeyCached
        {
            get
            {
                EnsureLoaded();
                return !string.IsNullOrEmpty(_cachedApiKey);
            }
        }

        public bool HasOAuthTokens
        {
            get
            {
                var dir = _claudeConfigDirProvider();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                foreach (var name in OAuthSentinelFiles)
                {
                    if (File.Exists(Path.Combine(dir, name))) return true;
                }
                return false;
            }
        }

        public bool IsAuthenticated => HasApiKeyCached || HasOAuthTokens;

        public async UniTask<string> GetApiKeyAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    LoadFromDiskLocked();
                    return _cachedApiKey;
                }
            }, cancellationToken: ct);
        }

        public async UniTask SetApiKeyAsync(string apiKey, CancellationToken ct = default)
        {
            await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var snapshot = LoadSnapshotLocked();
                    snapshot.ApiKeyEncoded = string.IsNullOrEmpty(apiKey) ? string.Empty : Encode(apiKey);
                    WriteSnapshotLocked(snapshot);
                    _cachedApiKey = apiKey;
                    _apiKeyLoaded = true;
                }
            }, cancellationToken: ct);

            _onChanged.OnNext(string.IsNullOrEmpty(apiKey)
                ? AuthCredentialChange.ApiKeyDeleted
                : AuthCredentialChange.ApiKeySet);
        }

        public async UniTask DeleteApiKeyAsync(CancellationToken ct = default)
        {
            await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var path = _credentialFileProvider();
                    if (File.Exists(path)) File.Delete(path);
                    _cachedApiKey = null;
                    _apiKeyLoaded = true;
                }
            }, cancellationToken: ct);

            _onChanged.OnNext(AuthCredentialChange.ApiKeyDeleted);
        }

        public void NotifyOAuthTokensChanged() => _onChanged.OnNext(AuthCredentialChange.OAuthTokensChanged);

        public void Dispose() => _onChanged.Dispose();

        // ── 내부 ──────────────────────────────────────────────

        private static readonly string[] OAuthSentinelFiles =
        {
            ".credentials.json", "credentials.json", ".claude.json", "config.json",
        };

        private void EnsureLoaded()
        {
            if (_apiKeyLoaded) return;
            lock (_gate) LoadFromDiskLocked();
        }

        private void LoadFromDiskLocked()
        {
            if (_apiKeyLoaded) return;
            var snapshot = LoadSnapshotLocked();
            _cachedApiKey = string.IsNullOrEmpty(snapshot.ApiKeyEncoded)
                ? null
                : Decode(snapshot.ApiKeyEncoded);
            _apiKeyLoaded = true;
        }

        private CredentialSnapshot LoadSnapshotLocked()
        {
            var path = _credentialFileProvider();
            if (!File.Exists(path)) return new CredentialSnapshot();
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return new CredentialSnapshot();
                return JsonUtility.FromJson<CredentialSnapshot>(json) ?? new CredentialSnapshot();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnthropicCredentialService] 자격증명 파일 파싱 실패 ({path}): {ex.Message}");
                return new CredentialSnapshot();
            }
        }

        private void WriteSnapshotLocked(CredentialSnapshot snapshot)
        {
            var path = _credentialFileProvider();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(snapshot, prettyPrint: false));
        }

        private static string Encode(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(bytes);
        }

        private static string Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return null;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch { return null; }
        }

        private static string DefaultCredentialFile() =>
            Path.Combine(Application.persistentDataPath, "OpenDesk", "anthropic-credentials.json");

        private static string DefaultClaudeConfigDir() => OpenDeskPaths.ClaudeConfigDir;

        [Serializable]
        private class CredentialSnapshot
        {
            public string ApiKeyEncoded = string.Empty;
        }
    }
}
