using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Plugins;
using UnityEngine;

namespace OpenDesk.Core.Services.Plugins
{
    /// <summary>
    /// 장착 플러그인 → McpConfigPayload 변환.
    /// EnvTemplate 의 {{KEY}} 토큰을 자격증명 값으로 치환한다.
    /// 누락된 자격증명이 있는 플러그인은 제외하고 경고 로그를 남긴다 (UX 는 호출 측에서 처리).
    /// </summary>
    public class McpConfigComposer : IMcpConfigComposer
    {
        // {{KEY}} 또는 {{KEY:fallback}} 같은 단순 형태만 지원
        private static readonly Regex TokenRegex = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

        private readonly IAgentPluginLoadoutService _loadout;
        private readonly IPluginCatalogService _catalog;
        private readonly IPluginCredentialService _credentials;

        public McpConfigComposer(
            IAgentPluginLoadoutService loadout,
            IPluginCatalogService catalog,
            IPluginCredentialService credentials)
        {
            _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        public async UniTask<McpConfigPayload> ComposeAsync(string agentId, CancellationToken ct = default)
        {
            var payload = new McpConfigPayload { agentId = agentId ?? string.Empty };
            if (string.IsNullOrEmpty(agentId)) return payload;

            var loadout = _loadout.GetLoadout(agentId);
            if (loadout == null || loadout.IsEmpty) return payload;

            foreach (var pluginId in loadout.EquippedPluginIds)
            {
                ct.ThrowIfCancellationRequested();
                var descriptor = _catalog.GetById(pluginId);
                if (descriptor == null)
                {
                    Debug.LogWarning($"[McpConfigComposer] 카탈로그에 없는 플러그인 무시: {pluginId}");
                    continue;
                }
                if (descriptor.ServerSpec == null || !descriptor.ServerSpec.IsValid)
                {
                    Debug.LogWarning($"[McpConfigComposer] ServerSpec 누락: {pluginId}");
                    continue;
                }

                var entry = await BuildEntryAsync(descriptor, ct);
                if (entry != null) payload.servers.Add(entry);
            }

            return payload;
        }

        private async UniTask<McpConfigServerEntry> BuildEntryAsync(PluginDescriptor descriptor, CancellationToken ct)
        {
            var resolvedEnv = new List<McpEnvEntry>();
            foreach (var pair in descriptor.ServerSpec.EnvTemplate)
            {
                var resolved = await ResolveTokensAsync(descriptor.Id, pair.Value ?? string.Empty, ct);
                if (resolved == null)
                {
                    Debug.LogWarning(
                        $"[McpConfigComposer] 플러그인 {descriptor.Id} env '{pair.Key}' 의 자격증명이 비어있어 서버를 건너뜁니다.");
                    return null;
                }
                resolvedEnv.Add(new McpEnvEntry { key = pair.Key, value = resolved });
            }

            return new McpConfigServerEntry
            {
                name = descriptor.Id,
                transport = descriptor.Transport.ToSerializedKey(),
                command = descriptor.ServerSpec.Command,
                args = new List<string>(descriptor.ServerSpec.Args ?? Array.Empty<string>()),
                env = resolvedEnv,
            };
        }

        private async UniTask<string> ResolveTokensAsync(string pluginId, string template, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (!template.Contains("{{")) return template;

            var matches = TokenRegex.Matches(template);
            if (matches.Count == 0) return template;

            var sb = new System.Text.StringBuilder(template.Length);
            int cursor = 0;
            foreach (Match m in matches)
            {
                sb.Append(template, cursor, m.Index - cursor);
                var key = m.Groups[1].Value;
                var value = await _credentials.GetAsync(pluginId, key, ct);
                if (string.IsNullOrEmpty(value))
                {
                    // 누락된 자격증명 → null 반환해서 호출자가 server 자체를 스킵하게 함.
                    return null;
                }
                sb.Append(value);
                cursor = m.Index + m.Length;
            }
            sb.Append(template, cursor, template.Length - cursor);
            return sb.ToString();
        }
    }
}
