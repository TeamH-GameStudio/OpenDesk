using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// 스킬의 런타임 표현. 카탈로그 엔트리 + 매니페스트 + 설치 상태를 통합한 immutable 모델.
    /// JsonUtility 직렬화 대상이 아님 (SkillCatalogEntry / SkillManifest 가 직렬화 책임).
    /// AgentEquipmentManager 가 장착 단위로 사용.
    /// </summary>
    public sealed record SkillDescriptor(
        string Id,
        string DisplayName,
        string Description,
        SkillCategory Category,
        string Version,
        string Author,
        string IconUrl,
        string PromptContent,
        string McpServerCommand,
        IReadOnlyList<string> RequiredTokens,
        IReadOnlyList<AgentRole> RecommendedRoles,
        string DownloadUrl,
        string Checksum,
        DateTime PublishedAt,
        int Downloads,
        float Rating,
        bool IsInstalled,
        string InstallPath,
        // v4: 이 스킬이 호출하는 capability 를 제공하는 플러그인 ID. SkillMarketView 설치 모달이
        // "스킬 + 필요 플러그인 함께 설치" 흐름을 만들 때 사용. 빈 배열 = 의존성 없음.
        IReadOnlyList<string> RequiredPlugins = null,
        IReadOnlyList<string> OptionalPlugins = null
    )
    {
        public bool HasExternalTool => !string.IsNullOrEmpty(McpServerCommand);

        public static SkillDescriptor Empty(string id) => new(
            Id: id ?? string.Empty,
            DisplayName: id ?? string.Empty,
            Description: string.Empty,
            Category: SkillCategory.General,
            Version: "0.0.0",
            Author: string.Empty,
            IconUrl: string.Empty,
            PromptContent: string.Empty,
            McpServerCommand: string.Empty,
            RequiredTokens: Array.Empty<string>(),
            RecommendedRoles: Array.Empty<AgentRole>(),
            DownloadUrl: string.Empty,
            Checksum: string.Empty,
            PublishedAt: DateTime.MinValue,
            Downloads: 0,
            Rating: 0f,
            IsInstalled: false,
            InstallPath: string.Empty,
            RequiredPlugins: Array.Empty<string>(),
            OptionalPlugins: Array.Empty<string>()
        );

        public SkillDescriptor WithInstallState(bool isInstalled, string installPath)
        {
            return this with { IsInstalled = isInstalled, InstallPath = installPath ?? string.Empty };
        }

        public SkillDescriptor WithPromptContent(string promptContent)
        {
            return this with { PromptContent = promptContent ?? string.Empty };
        }
    }
}
