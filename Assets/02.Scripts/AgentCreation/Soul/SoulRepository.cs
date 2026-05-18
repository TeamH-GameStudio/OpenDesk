using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenDesk.AgentCreation.Soul
{
    /// <summary>
    /// 에이전트 Soul markdown의 디스크 영속화.
    ///
    /// 키 정책: 에이전트 이름을 슬러그로 변환한 폴더 (Application.persistentDataPath/Agents/{slug}/soul.md).
    /// PlayerPrefs 인덱스/SessionId는 AgentOfficeBootstrapper.CleanupOldData에서 재할당되므로
    /// 이름 기반이 유일한 안정 키.
    ///
    /// AgentEquipmentManager는 BuildSystemPrompt에서 TryLoadAsBlock(name)으로 조회한다.
    /// </summary>
    public static class SoulRepository
    {
        private const string FolderRoot = "Agents";
        private const string FileName   = "soul.md";

        // ── 저장 ──

        /// <summary>지정 이름의 Soul markdown을 영속화. 빈 입력은 무시.</summary>
        public static void Save(string agentName, string soulMarkdown)
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                Debug.LogWarning("[SoulRepo] 빈 이름으로 저장 시도 — 무시");
                return;
            }
            if (string.IsNullOrWhiteSpace(soulMarkdown))
            {
                Debug.LogWarning($"[SoulRepo] 빈 본문으로 저장 시도 ({agentName}) — 무시");
                return;
            }

            try
            {
                var path = ResolveFilePath(agentName, ensureDir: true);
                File.WriteAllText(path, soulMarkdown.Trim() + "\n", new UTF8Encoding(false));
                Debug.Log($"[SoulRepo] 저장: {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SoulRepo] 저장 실패 ({agentName}): {e.Message}");
            }
        }

        // ── 조회 ──

        /// <summary>저장된 Soul markdown 원문. 없으면 null.</summary>
        public static string TryLoadMarkdown(string agentName)
        {
            if (string.IsNullOrWhiteSpace(agentName)) return null;

            try
            {
                var path = ResolveFilePath(agentName, ensureDir: false);
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SoulRepo] 로드 실패 ({agentName}): {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 시스템 프롬프트 합성에 바로 쓸 수 있도록 &lt;soul&gt;...&lt;/soul&gt; 블록으로 래핑.
        /// 저장된 Soul이 없으면 null.
        /// </summary>
        public static string TryLoadAsBlock(string agentName)
        {
            var md = TryLoadMarkdown(agentName);
            if (string.IsNullOrWhiteSpace(md)) return null;
            return $"<soul>\n{md.Trim()}\n</soul>\n";
        }

        // ── 경로 유틸 ──

        public static string ResolveFilePath(string agentName, bool ensureDir)
        {
            var slug = Slugify(agentName);
            var dir  = Path.Combine(Application.persistentDataPath, FolderRoot, slug);
            if (ensureDir && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        /// <summary>
        /// 이름 슬러그화. 한글·영문·숫자는 보존, 그 외는 '_'.
        /// 같은 이름의 두 에이전트는 같은 폴더를 공유한다 (V1 한계).
        /// </summary>
        private static string Slugify(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            var slug = sb.ToString();
            return string.IsNullOrEmpty(slug) ? "_" : slug;
        }
    }
}
