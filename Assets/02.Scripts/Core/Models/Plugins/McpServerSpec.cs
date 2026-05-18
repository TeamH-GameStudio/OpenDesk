using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Plugins
{
    /// <summary>
    /// MCP 서버 실행/연결 명세. EnvTemplate 값에는 자격증명 치환 토큰을 쓴다
    /// (예: "{{NOTION_API_KEY}}"). IMcpConfigComposer 가 실제 값으로 치환한다.
    /// JsonUtility 직렬화 대상이 아님 — McpServerSpecData 가 직렬화 책임.
    /// </summary>
    public sealed record McpServerSpec(
        string Command,
        IReadOnlyList<string> Args,
        IReadOnlyDictionary<string, string> EnvTemplate
    )
    {
        public static McpServerSpec Empty => new(
            Command: string.Empty,
            Args: Array.Empty<string>(),
            EnvTemplate: new Dictionary<string, string>()
        );

        public bool IsValid => !string.IsNullOrWhiteSpace(Command);
    }

    /// <summary>
    /// JsonUtility 호환 직렬화 컨테이너. Dictionary 미지원이므로 키/값 페어 리스트로 표현.
    /// </summary>
    [Serializable]
    public class McpServerSpecData
    {
        public string command;
        public List<string> args = new();
        public List<McpEnvEntry> env = new();

        public McpServerSpec ToSpec()
        {
            var argsCopy = args != null ? new List<string>(args) : new List<string>();
            var envCopy = new Dictionary<string, string>();
            if (env != null)
            {
                foreach (var entry in env)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key)) continue;
                    envCopy[entry.key] = entry.value ?? string.Empty;
                }
            }
            return new McpServerSpec(command ?? string.Empty, argsCopy, envCopy);
        }

        public static McpServerSpecData FromSpec(McpServerSpec spec)
        {
            var data = new McpServerSpecData
            {
                command = spec.Command ?? string.Empty,
                args = new List<string>(spec.Args ?? Array.Empty<string>()),
                env = new List<McpEnvEntry>(),
            };
            if (spec.EnvTemplate != null)
            {
                foreach (var pair in spec.EnvTemplate)
                {
                    data.env.Add(new McpEnvEntry { key = pair.Key, value = pair.Value ?? string.Empty });
                }
            }
            return data;
        }
    }

    [Serializable]
    public class McpEnvEntry
    {
        public string key;
        public string value;
    }
}
