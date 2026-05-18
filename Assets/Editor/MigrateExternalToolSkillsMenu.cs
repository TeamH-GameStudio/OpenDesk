using System.Collections.Generic;
using System.IO;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Plugins;
using UnityEditor;
using UnityEngine;
using OpenDesk.SkillDiskette;
using OpenDesk.Core;

namespace OpenDesk.Editor
{
    /// <summary>
    /// `Tools/OpenDesk/Migrate ExternalTool Skills` — 기존 SkillDiskette SO 중 외부 도구가 채워진 항목을
    /// PluginDescriptor 초안(`~/.opendesk/plugins/{id}/manifest.json`)으로 변환한다.
    /// 원본 SO 는 그대로 두고 (가역 deprecation), 결과 리포트만 콘솔에 출력.
    /// </summary>
    public static class MigrateExternalToolSkillsMenu
    {
        [MenuItem("Tools/OpenDesk/Migrate ExternalTool Skills")]
        public static void RunMigration()
        {
            var guids = AssetDatabase.FindAssets("t:SkillDiskette");
            if (guids == null || guids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "ExternalTool Skill 마이그레이션",
                    "프로젝트에서 SkillDiskette SO 자산을 찾지 못했습니다.",
                    "확인");
                return;
            }

            EnsureDir(OpenDeskPaths.Plugins);

            var migrated = new List<string>();
            var skipped = new List<string>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var diskette = AssetDatabase.LoadAssetAtPath<OpenDesk.SkillDiskette.SkillDiskette>(path);
                if (diskette == null) continue;

                if (!diskette.HasExternalTool && diskette.Category != SkillCategory.ExternalTool)
                {
                    skipped.Add(diskette.SkillId);
                    continue;
                }

                try
                {
                    var manifest = BuildManifest(diskette);
                    var pluginDir = OpenDeskPaths.PluginDir(manifest.id);
                    EnsureDir(pluginDir);

                    var manifestPath = Path.Combine(pluginDir, "manifest.json");
                    File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, prettyPrint: true));

                    migrated.Add($"{manifest.id} ({path}) → {manifestPath}");
                }
                catch (System.Exception ex)
                {
                    skipped.Add($"{diskette.SkillId} (오류: {ex.Message})");
                }
            }

            var summary = $"마이그레이션 결과:\n\n- 변환 {migrated.Count}개\n- 스킵 {skipped.Count}개";
            Debug.Log("[Migrate] " + summary + "\n변환:\n  " + string.Join("\n  ", migrated)
                     + "\n스킵:\n  " + string.Join("\n  ", skipped));
            EditorUtility.DisplayDialog("ExternalTool Skill 마이그레이션", summary, "확인");
        }

        private static PluginManifest BuildManifest(OpenDesk.SkillDiskette.SkillDiskette diskette)
        {
            var args = new List<string>();
            string command = diskette.McpServerCommand ?? string.Empty;
            // `command arg1 arg2 ...` 형태를 분리 (단순 split — quoted args 는 별도 처리 필요 시 사용자가 수동 보정).
            if (!string.IsNullOrEmpty(command))
            {
                var parts = command.Split(' ');
                if (parts.Length > 0)
                {
                    command = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(parts[i])) args.Add(parts[i]);
                    }
                }
            }

            var envList = new List<McpEnvEntry>();
            var requiredCreds = new List<CredentialRequirementData>();
            foreach (var token in diskette.RequiredTokens ?? new List<string>())
            {
                if (string.IsNullOrEmpty(token)) continue;
                envList.Add(new McpEnvEntry { key = token, value = $"{{{{{token}}}}}" });
                requiredCreds.Add(new CredentialRequirementData
                {
                    key = token,
                    displayName = token,
                    kind = "api-key",
                    optional = false,
                });
            }

            return new PluginManifest
            {
                id = string.IsNullOrEmpty(diskette.SkillId) ? diskette.name : diskette.SkillId,
                displayName = string.IsNullOrEmpty(diskette.DisplayName) ? diskette.name : diskette.DisplayName,
                description = diskette.Description ?? string.Empty,
                vendor = "custom",
                transport = "stdio",
                version = "0.1.0",
                iconUrl = string.Empty,
                serverSpec = new McpServerSpecData
                {
                    command = command,
                    args = args,
                    env = envList,
                },
                requiredCredentials = requiredCreds,
            };
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
