using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.SkillDiskette;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// 레거시 SkillDiskette SO → 신규 SkillDescriptor 변환.
    /// Diskette 크래프팅 / 프리셋 자산을 새 장착 흐름에 합류시키기 위한 어댑터.
    /// </summary>
    public static class SkillDisketteAdapter
    {
        public static SkillDescriptor ToDescriptor(SkillDiskette.SkillDiskette diskette, string installPath = null)
        {
            if (diskette == null) return SkillDescriptor.Empty("unknown");

            var tokens = diskette.RequiredTokens != null
                ? new List<string>(diskette.RequiredTokens)
                : new List<string>();

            return new SkillDescriptor(
                Id: diskette.SkillId ?? diskette.name,
                DisplayName: string.IsNullOrEmpty(diskette.DisplayName) ? diskette.SkillId : diskette.DisplayName,
                Description: diskette.Description ?? string.Empty,
                Category: diskette.Category,
                Version: "1.0.0",
                Author: diskette.IsCustomCrafted ? "user" : "opendesk",
                IconUrl: string.Empty,
                PromptContent: diskette.PromptContent ?? string.Empty,
                McpServerCommand: diskette.McpServerCommand ?? string.Empty,
                RequiredTokens: tokens,
                RecommendedRoles: Array.Empty<AgentRole>(),
                DownloadUrl: string.Empty,
                Checksum: string.Empty,
                PublishedAt: DateTime.MinValue,
                Downloads: 0,
                Rating: 0f,
                IsInstalled: true,
                InstallPath: installPath ?? string.Empty
            );
        }
    }
}
