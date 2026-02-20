using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OpenDesk.Onboarding.Models;
using OpenDesk.Onboarding.Services;
using UnityEngine;

namespace OpenDesk.Onboarding.Implementations
{
    /// <summary>
    /// OpenClaw .yaml 파싱
    /// 외부 YAML 라이브러리 없이 핵심 필드만 직접 파싱
    /// (YamlDotNet 추가 시 ParseFromString 교체 가능)
    /// </summary>
    public class AgentConfigParser : IAgentConfigParser
    {
        public IReadOnlyList<AgentConfig> ParseFromFile(string yamlPath)
        {
            if (string.IsNullOrEmpty(yamlPath) || !File.Exists(yamlPath))
            {
                Debug.LogWarning($"[Parser] 파일 없음: {yamlPath}");
                return new List<AgentConfig>();
            }

            try
            {
                var content = File.ReadAllText(yamlPath);
                return ParseFromString(content);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Parser] 파일 읽기 실패: {ex.Message}");
                return new List<AgentConfig>();
            }
        }

        public IReadOnlyList<AgentConfig> ParseFromString(string yamlContent)
        {
            var result = new List<AgentConfig>();

            if (string.IsNullOrWhiteSpace(yamlContent))
                return result;

            try
            {
                // OpenClaw agents 섹션 파싱
                // 예시 구조:
                // agents:
                //   main:
                //     name: "팀장"
                //     model: claude-sonnet-4-6
                //   dev:
                //     name: "개발자"
                //     model: claude-sonnet-4-6

                var lines   = yamlContent.Split('\n');
                var inAgentsSection = false;
                var currentIndent   = 0;
                AgentConfig current = null;

                foreach (var rawLine in lines)
                {
                    var line   = rawLine.TrimEnd();
                    var indent = CountLeadingSpaces(line);
                    var trimmed = line.TrimStart();

                    // agents: 섹션 진입
                    if (trimmed.StartsWith("agents:"))
                    {
                        inAgentsSection = true;
                        currentIndent   = indent;
                        continue;
                    }

                    if (!inAgentsSection) continue;

                    // 섹션 종료 감지 (들여쓰기 레벨 복귀)
                    if (!string.IsNullOrWhiteSpace(trimmed) &&
                        indent <= currentIndent &&
                        !trimmed.StartsWith("#"))
                    {
                        // agents 섹션 밖으로 나감
                        if (indent < currentIndent + 2)
                        {
                            SaveCurrentAgent(current, result);
                            current = null;
                            inAgentsSection = false;
                            continue;
                        }
                    }

                    // 새 에이전트 블록 (들여쓰기 2칸)
                    if (indent == currentIndent + 2 && trimmed.EndsWith(":"))
                    {
                        SaveCurrentAgent(current, result);
                        var sessionId = trimmed.TrimEnd(':').Trim();
                        current = new AgentConfig
                        {
                            SessionId = sessionId,
                            Name      = sessionId,
                            Role      = ResolveRole(sessionId),
                        };
                        continue;
                    }

                    if (current == null) continue;

                    // 필드 파싱
                    var kv = ParseKeyValue(trimmed);
                    if (kv == null) continue;

                    switch (kv.Value.key.ToLowerInvariant())
                    {
                        case "name":    current.Name    = kv.Value.value; break;
                        case "model":   current.Model   = kv.Value.value; break;
                        case "api_key":
                        case "apikey":
                            current.HasApiKey = !string.IsNullOrEmpty(kv.Value.value) &&
                                                kv.Value.value != "null" &&
                                                !kv.Value.value.StartsWith("${");
                            break;
                    }
                }

                // 마지막 에이전트 저장
                SaveCurrentAgent(current, result);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Parser] YAML 파싱 실패: {ex.Message}");
            }

            return result;
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        private static void SaveCurrentAgent(AgentConfig agent, List<AgentConfig> list)
        {
            if (agent != null && agent.IsValid)
                list.Add(agent);
        }

        private static string ResolveRole(string sessionId)
        {
            return sessionId.ToLowerInvariant() switch
            {
                "main"     => "main",
                "dev"      => "dev",
                "planner"  => "planner",
                "planning" => "planner",
                "life"     => "life",
                "director" => "main",
                _          => "main",
            };
        }

        private static int CountLeadingSpaces(string line)
        {
            var count = 0;
            foreach (var c in line)
            {
                if (c == ' ') count++;
                else break;
            }
            return count;
        }

        private static (string key, string value)? ParseKeyValue(string line)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) return null;

            var key   = line.Substring(0, colonIdx).Trim();
            var value = line.Substring(colonIdx + 1).Trim().Trim('"').Trim('\'');

            return string.IsNullOrEmpty(key) ? null : (key, value);
        }
    }
}
