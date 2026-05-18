using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 파일 기반 자격증명 저장소. {persistentDataPath}/OpenDesk/plugin-credentials/{pluginId}.json
    /// Base64 인코딩 (ApiKeyVaultService 와 동일 수준 보호).
    /// 추후 OS 키체인 연동은 ApiKeyVaultService 와 함께 일괄 업그레이드 예정.
    /// </summary>
    public class PluginCredentialService : IPluginCredentialService, IDisposable
    {
        private readonly Subject<string> _onChanged = new();
        private readonly object _gate = new();
        private readonly Func<string> _rootDirProvider;

        public Observable<string> OnCredentialChanged => _onChanged;

        // VContainer 는 [Inject] 없으면 most-parameters ctor 를 default 로 선택한다.
        // Func<string> ctor 가 그 대상이 되면서 컨테이너에 미등록인 Func<string> resolve 실패가 발생한다
        // (테스트 격리 전용 ctor 인데 prod 에서 잘못 선택됨). 명시적 [Inject] 로 parameterless ctor 를 강제.
        [Inject]
        public PluginCredentialService() : this(DefaultRootDir) { }

        // 테스트 주입용 — 디스크 격리. VContainer 가 이 ctor 를 고르지 않도록 위쪽에 [Inject] 명시.
        public PluginCredentialService(Func<string> rootDirProvider)
        {
            _rootDirProvider = rootDirProvider ?? DefaultRootDir;
        }

        public async UniTask SetAsync(string pluginId, string key, string value, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(key)) return;

            await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var path = GetCredentialFilePath(pluginId);
                    EnsureDirectory(Path.GetDirectoryName(path));
                    var snapshot = LoadSnapshot(path);
                    snapshot.Set(key, Encode(value));
                    WriteSnapshot(path, snapshot);
                }
            }, cancellationToken: ct);

            _onChanged.OnNext(pluginId);
        }

        public UniTask<string> GetAsync(string pluginId, string key, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(key))
                return UniTask.FromResult<string>(null);

            return UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var path = GetCredentialFilePath(pluginId);
                    if (!File.Exists(path)) return (string)null;
                    var snapshot = LoadSnapshot(path);
                    var raw = snapshot.Get(key);
                    return raw == null ? null : Decode(raw);
                }
            }, cancellationToken: ct);
        }

        public async UniTask DeleteAsync(string pluginId, string key, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(key)) return;

            await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var path = GetCredentialFilePath(pluginId);
                    if (!File.Exists(path)) return;
                    var snapshot = LoadSnapshot(path);
                    if (!snapshot.Remove(key)) return;
                    if (snapshot.IsEmpty)
                    {
                        File.Delete(path);
                    }
                    else
                    {
                        WriteSnapshot(path, snapshot);
                    }
                }
            }, cancellationToken: ct);

            _onChanged.OnNext(pluginId);
        }

        public async UniTask DeleteAllAsync(string pluginId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            await UniTask.RunOnThreadPool(() =>
            {
                lock (_gate)
                {
                    var path = GetCredentialFilePath(pluginId);
                    if (File.Exists(path)) File.Delete(path);
                }
            }, cancellationToken: ct);

            _onChanged.OnNext(pluginId);
        }

        public async UniTask<bool> HasAllRequiredAsync(PluginDescriptor descriptor, CancellationToken ct = default)
        {
            if (descriptor == null || descriptor.RequiredCredentials == null) return true;

            foreach (var req in descriptor.RequiredCredentials)
            {
                if (req == null || req.Optional || string.IsNullOrEmpty(req.Key)) continue;
                var value = await GetAsync(descriptor.Id, req.Key, ct);
                if (string.IsNullOrEmpty(value)) return false;
            }
            return true;
        }

        public void Dispose() => _onChanged.Dispose();

        // ── 내부 ──────────────────────────────────────────────

        private string GetCredentialFilePath(string pluginId) =>
            Path.Combine(_rootDirProvider(), $"{pluginId}.json");

        private static string DefaultRootDir() =>
            Path.Combine(Application.persistentDataPath, "OpenDesk", "plugin-credentials");

        private static void EnsureDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static CredentialSnapshot LoadSnapshot(string path)
        {
            if (!File.Exists(path)) return new CredentialSnapshot();
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return new CredentialSnapshot();
                var data = JsonUtility.FromJson<CredentialSnapshotData>(json);
                return CredentialSnapshot.FromData(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PluginCredentialService] 자격증명 파일 파싱 실패 ({path}): {ex.Message}");
                return new CredentialSnapshot();
            }
        }

        private static void WriteSnapshot(string path, CredentialSnapshot snapshot)
        {
            var data = snapshot.ToData();
            var json = JsonUtility.ToJson(data, prettyPrint: false);
            File.WriteAllText(path, json);
        }

        private static string Encode(string plain)
        {
            if (plain == null) return null;
            var bytes = Encoding.UTF8.GetBytes(plain);
            return Convert.ToBase64String(bytes);
        }

        private static string Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return string.Empty;
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        // ── DTO ──────────────────────────────────────────────

        private class CredentialSnapshot
        {
            private readonly Dictionary<string, string> _values = new();

            public bool IsEmpty => _values.Count == 0;
            public string Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
            public void Set(string key, string value) => _values[key] = value ?? string.Empty;
            public bool Remove(string key) => _values.Remove(key);

            public CredentialSnapshotData ToData()
            {
                var data = new CredentialSnapshotData();
                foreach (var pair in _values)
                {
                    data.entries.Add(new CredentialEntry { key = pair.Key, value = pair.Value });
                }
                return data;
            }

            public static CredentialSnapshot FromData(CredentialSnapshotData data)
            {
                var snap = new CredentialSnapshot();
                if (data?.entries == null) return snap;
                foreach (var entry in data.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    snap._values[entry.key] = entry.value ?? string.Empty;
                }
                return snap;
            }
        }

        [Serializable]
        private class CredentialSnapshotData
        {
            public List<CredentialEntry> entries = new();
        }

        [Serializable]
        private class CredentialEntry
        {
            public string key;
            public string value;
        }
    }
}
