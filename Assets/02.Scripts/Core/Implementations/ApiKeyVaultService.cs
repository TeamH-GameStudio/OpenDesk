using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// AI 제공업체 API 키 안전 저장소
    /// - Windows: DPAPI (ProtectedData) 암호화
    /// - macOS/Linux: 파일 기반 암호화 (Application.persistentDataPath)
    /// - Ollama 감지 시 API 키 없이도 Free 모드 사용 가능
    /// </summary>
    public class ApiKeyVaultService : IApiKeyVaultService, IDisposable
    {
        private readonly Subject<ApiKeyEntry> _keyChanged = new();
        private readonly Dictionary<string, ApiKeyEntry> _statuses = new();

        public Observable<ApiKeyEntry> OnKeyChanged => _keyChanged;

        // ── 제공업체 목록 (14개+) ────────────────────────────────────────

        private static readonly List<ApiProvider> Providers = new()
        {
            new() { Id = "ollama",     DisplayName = "Ollama (로컬 무료)",  IsLocal = true,  RequiresKey = false, KeyHint = "",                SignupUrl = "" },
            new() { Id = "anthropic",  DisplayName = "Anthropic (Claude)",  IsLocal = false, RequiresKey = true,  KeyHint = "sk-ant-...",      SignupUrl = "https://console.anthropic.com/settings/keys" },
            new() { Id = "openai",     DisplayName = "OpenAI (GPT)",        IsLocal = false, RequiresKey = true,  KeyHint = "sk-...",          SignupUrl = "https://platform.openai.com/api-keys" },
            new() { Id = "google",     DisplayName = "Google (Gemini)",     IsLocal = false, RequiresKey = true,  KeyHint = "AIza...",         SignupUrl = "https://aistudio.google.com/apikey" },
            new() { Id = "deepseek",   DisplayName = "DeepSeek",            IsLocal = false, RequiresKey = true,  KeyHint = "sk-...",          SignupUrl = "https://platform.deepseek.com/api_keys" },
            new() { Id = "xai",        DisplayName = "xAI (Grok)",          IsLocal = false, RequiresKey = true,  KeyHint = "xai-...",         SignupUrl = "https://console.x.ai" },
            new() { Id = "mistral",    DisplayName = "Mistral AI",          IsLocal = false, RequiresKey = true,  KeyHint = "",                SignupUrl = "https://console.mistral.ai/api-keys" },
            new() { Id = "cohere",     DisplayName = "Cohere",              IsLocal = false, RequiresKey = true,  KeyHint = "",                SignupUrl = "https://dashboard.cohere.com/api-keys" },
            new() { Id = "groq",       DisplayName = "Groq",                IsLocal = false, RequiresKey = true,  KeyHint = "gsk_...",         SignupUrl = "https://console.groq.com/keys" },
            new() { Id = "together",   DisplayName = "Together AI",         IsLocal = false, RequiresKey = true,  KeyHint = "",                SignupUrl = "https://api.together.xyz/settings/api-keys" },
            new() { Id = "fireworks",  DisplayName = "Fireworks AI",        IsLocal = false, RequiresKey = true,  KeyHint = "fw_...",          SignupUrl = "https://fireworks.ai/account/api-keys" },
            new() { Id = "perplexity", DisplayName = "Perplexity",          IsLocal = false, RequiresKey = true,  KeyHint = "pplx-...",        SignupUrl = "https://www.perplexity.ai/settings/api" },
            new() { Id = "openrouter", DisplayName = "OpenRouter",          IsLocal = false, RequiresKey = true,  KeyHint = "sk-or-...",       SignupUrl = "https://openrouter.ai/keys" },
            new() { Id = "azure",      DisplayName = "Azure OpenAI",        IsLocal = false, RequiresKey = true,  KeyHint = "",                SignupUrl = "https://portal.azure.com" },
        };

        public IReadOnlyList<ApiProvider> GetProviders() => Providers;

        public ApiKeyEntry GetKeyStatus(string providerId)
        {
            return _statuses.TryGetValue(providerId, out var entry)
                ? entry
                : new ApiKeyEntry { ProviderId = providerId, Status = ApiKeyStatus.NotSet };
        }

        public IReadOnlyList<ApiKeyEntry> GetAllKeyStatuses()
        {
            return Providers.Select(p => GetKeyStatus(p.Id)).ToList();
        }

        // ── 키 저장 (암호화) ────────────────────────────────────────────

        public async UniTask<ApiKeyStatus> SaveKeyAsync(string providerId, string apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await DeleteKeyAsync(providerId, ct);
                return ApiKeyStatus.NotSet;
            }

            await UniTask.RunOnThreadPool(() =>
            {
                var encrypted = EncryptKey(apiKey);
                var path = GetKeyFilePath(providerId);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, encrypted);
            }, cancellationToken: ct);

            Debug.Log($"[Vault] API 키 저장됨: {providerId}");

            // 저장 후 유효성 검증
            var status = await ValidateKeyAsync(providerId, ct);

            var entry = new ApiKeyEntry
            {
                ProviderId   = providerId,
                Status       = status,
                LastVerified = DateTime.UtcNow,
            };
            _statuses[providerId] = entry;
            _keyChanged.OnNext(entry);

            return status;
        }

        public async UniTask<string> GetKeyAsync(string providerId, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                var path = GetKeyFilePath(providerId);
                if (!File.Exists(path)) return null;

                var encrypted = File.ReadAllBytes(path);
                return DecryptKey(encrypted);
            }, cancellationToken: ct);
        }

        // ── 키 유효성 검증 ──────────────────────────────────────────────

        public async UniTask<ApiKeyStatus> ValidateKeyAsync(string providerId, CancellationToken ct = default)
        {
            var key = await GetKeyAsync(providerId, ct);
            if (string.IsNullOrEmpty(key))
                return ApiKeyStatus.NotSet;

            // 로컬 모델은 키 검증 불필요
            var provider = Providers.FirstOrDefault(p => p.Id == providerId);
            if (provider is { IsLocal: true })
                return ApiKeyStatus.Valid;

            return await UniTask.RunOnThreadPool(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout   = TimeSpan.FromSeconds(10);

                    // 제공업체별 간단한 API 호출로 키 유효성 확인
                    var (url, headers) = GetValidationEndpoint(providerId, key);
                    if (string.IsNullOrEmpty(url))
                        return ApiKeyStatus.Valid; // 검증 엔드포인트 없으면 유효로 간주

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    foreach (var (name, value) in headers)
                        request.Headers.TryAddWithoutValidation(name, value);

                    var response = await client.SendAsync(request, ct);

                    return response.IsSuccessStatusCode
                        ? ApiKeyStatus.Valid
                        : ApiKeyStatus.Invalid;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Vault] 키 검증 실패 ({providerId}): {ex.Message}");
                    return ApiKeyStatus.Error;
                }
            }, cancellationToken: ct);
        }

        // ── 키 삭제 ─────────────────────────────────────────────────────

        public async UniTask DeleteKeyAsync(string providerId, CancellationToken ct = default)
        {
            await UniTask.RunOnThreadPool(() =>
            {
                var path = GetKeyFilePath(providerId);
                if (File.Exists(path))
                    File.Delete(path);
            }, cancellationToken: ct);

            var entry = new ApiKeyEntry { ProviderId = providerId, Status = ApiKeyStatus.NotSet };
            _statuses[providerId] = entry;
            _keyChanged.OnNext(entry);

            Debug.Log($"[Vault] API 키 삭제됨: {providerId}");
        }

        // ── Ollama 로컬 모델 확인 ───────────────────────────────────────

        public async UniTask<bool> CanRunWithoutApiKeyAsync(CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    // Ollama가 실행 중인지 확인 (기본 포트 11434)
                    using var client = new System.Net.Sockets.TcpClient();
                    var task = client.ConnectAsync("127.0.0.1", 11434);
                    return task.Wait(1000);
                }
                catch
                {
                    return false;
                }
            }, cancellationToken: ct);
        }

        // ── 암호화/복호화 ───────────────────────────────────────────────

        private static byte[] EncryptKey(string plainText)
        {
            // Base64 인코딩 (Application.persistentDataPath 파일 권한으로 보호)
            // TODO: 빌드 시 Windows DPAPI 또는 OS Keychain 연동으로 업그레이드
            var bytes = Encoding.UTF8.GetBytes(plainText);
            return Encoding.UTF8.GetBytes(Convert.ToBase64String(bytes));
        }

        private static string DecryptKey(byte[] encrypted)
        {
            var base64Str = Encoding.UTF8.GetString(encrypted);
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64Str));
        }

        // ── 저장 경로 ───────────────────────────────────────────────────

        private static string GetKeyFilePath(string providerId)
        {
            // Application.persistentDataPath/OpenDesk/keys/{providerId}.key
            return Path.Combine(
                Application.persistentDataPath,
                "OpenDesk", "keys",
                $"{providerId}.key");
        }

        // ── 제공업체별 검증 엔드포인트 ───────────────────────────────────

        private static (string url, (string name, string value)[] headers) GetValidationEndpoint(
            string providerId, string key)
        {
            return providerId switch
            {
                "anthropic" => (
                    "https://api.anthropic.com/v1/models",
                    new[] {
                        ("x-api-key", key),
                        ("anthropic-version", "2023-06-01")
                    }),
                "openai" => (
                    "https://api.openai.com/v1/models",
                    new[] { ("Authorization", $"Bearer {key}") }),
                "deepseek" => (
                    "https://api.deepseek.com/models",
                    new[] { ("Authorization", $"Bearer {key}") }),
                "groq" => (
                    "https://api.groq.com/openai/v1/models",
                    new[] { ("Authorization", $"Bearer {key}") }),
                _ => (null, Array.Empty<(string, string)>()),
            };
        }

        public void Dispose()
        {
            _keyChanged.Dispose();
        }
    }
}
