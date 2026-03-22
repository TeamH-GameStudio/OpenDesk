using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// ClawHub 스킬 마켓플레이스
    /// - 스킬 목록은 내장 카탈로그 + 로컬 디렉토리 스캔
    /// - 설치: ~/.openclaw/skills/{name}/SKILL.md 생성
    /// - 샌드박스: agents.defaults.sandbox 설정 제어
    /// </summary>
    public class SkillMarketService : ISkillMarketService, IDisposable
    {
        private readonly Subject<SkillEntry> _skillChanged = new();
        public Observable<SkillEntry> OnSkillChanged => _skillChanged;

        // ── 내장 스킬 카탈로그 (추후 ClawHub API로 대체) ────────────────

        private static readonly List<SkillEntry> BuiltInCatalog = new()
        {
            new() { Id = "google-calendar",   Name = "Google Calendar",   Description = "일정 조회/생성/수정",           Category = "생산성", Author = "openclaw", Rating = 4.5f, Downloads = 12000 },
            new() { Id = "web-search",        Name = "Web Search",        Description = "웹 검색 및 요약",              Category = "정보",   Author = "openclaw", Rating = 4.7f, Downloads = 25000 },
            new() { Id = "browser-control",   Name = "Browser Control",   Description = "브라우저 자동화 (열기/클릭/입력)", Category = "브라우저", Author = "openclaw", Rating = 4.3f, Downloads = 8000 },
            new() { Id = "file-manager",      Name = "File Manager",      Description = "파일 읽기/쓰기/검색",           Category = "시스템", Author = "openclaw", Rating = 4.6f, Downloads = 15000 },
            new() { Id = "code-executor",     Name = "Code Executor",     Description = "Python/Node.js 코드 실행",     Category = "개발",   Author = "openclaw", Rating = 4.4f, Downloads = 10000 },
            new() { Id = "email-assistant",   Name = "Email Assistant",   Description = "이메일 읽기/작성/발송",         Category = "커뮤니케이션", Author = "community", Rating = 4.1f, Downloads = 5000 },
            new() { Id = "notion-sync",       Name = "Notion Sync",       Description = "Notion 페이지 읽기/편집",       Category = "생산성", Author = "community", Rating = 4.0f, Downloads = 3000 },
            new() { Id = "zapier-mcp",        Name = "Zapier MCP",        Description = "Zapier 5000+ 앱 연동",         Category = "통합",   Author = "zapier",   Rating = 4.2f, Downloads = 7000 },
            new() { Id = "image-gen",         Name = "Image Generator",   Description = "DALL-E/Stable Diffusion 이미지 생성", Category = "창작", Author = "community", Rating = 3.9f, Downloads = 6000 },
            new() { Id = "db-query",          Name = "Database Query",    Description = "SQL 데이터베이스 쿼리/수정",     Category = "개발",   Author = "community", Rating = 4.3f, Downloads = 4000 },
        };

        public async UniTask<IReadOnlyList<SkillEntry>> SearchSkillsAsync(string query = "", CancellationToken ct = default)
        {
            var installed = await ScanInstalledSkillsAsync(ct);

            var results = BuiltInCatalog.Select(s =>
            {
                var copy = CloneEntry(s);
                copy.IsInstalled = installed.Any(i => i.Id == s.Id);
                return copy;
            });

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.ToLower();
                results = results.Where(s =>
                    s.Name.ToLower().Contains(q) ||
                    s.Description.ToLower().Contains(q) ||
                    s.Category.ToLower().Contains(q));
            }

            return results.ToList();
        }

        public async UniTask<IReadOnlyList<SkillEntry>> GetFeaturedSkillsAsync(CancellationToken ct = default)
        {
            var all = await SearchSkillsAsync("", ct);
            return all.OrderByDescending(s => s.Downloads).Take(6).ToList();
        }

        public async UniTask<IReadOnlyList<SkillEntry>> GetInstalledSkillsAsync(CancellationToken ct = default)
        {
            return await ScanInstalledSkillsAsync(ct);
        }

        public async UniTask<bool> InstallSkillAsync(string skillId, CancellationToken ct = default)
        {
            var skill = BuiltInCatalog.FirstOrDefault(s => s.Id == skillId);
            if (skill == null)
            {
                Debug.LogWarning($"[SkillMarket] 스킬 미발견: {skillId}");
                return false;
            }

            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var skillDir = Path.Combine(GetSkillsBasePath(), skillId);
                    Directory.CreateDirectory(skillDir);

                    // SKILL.md 생성
                    var skillMd = $@"---
name: {skill.Name}
description: {skill.Description}
author: {skill.Author}
version: 1.0.0
model_preference: auto
sandbox: true
---

# {skill.Name}

{skill.Description}

## 사용 방법
이 스킬이 활성화되면 에이전트가 자동으로 적절한 상황에서 호출합니다.
";
                    File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), skillMd);

                    var installed = CloneEntry(skill);
                    installed.IsInstalled = true;
                    installed.InstallPath = skillDir;
                    _skillChanged.OnNext(installed);

                    Debug.Log($"[SkillMarket] 설치 완료: {skill.Name} → {skillDir}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SkillMarket] 설치 실패 ({skillId}): {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<bool> UninstallSkillAsync(string skillId, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var skillDir = Path.Combine(GetSkillsBasePath(), skillId);
                    if (Directory.Exists(skillDir))
                    {
                        Directory.Delete(skillDir, recursive: true);
                        Debug.Log($"[SkillMarket] 삭제 완료: {skillId}");
                    }

                    var entry = new SkillEntry { Id = skillId, IsInstalled = false };
                    _skillChanged.OnNext(entry);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SkillMarket] 삭제 실패 ({skillId}): {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        public async UniTask<bool> SetSandboxModeAsync(string skillId, bool enabled, CancellationToken ct = default)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var skillMdPath = Path.Combine(GetSkillsBasePath(), skillId, "SKILL.md");
                    if (!File.Exists(skillMdPath)) return false;

                    var content = File.ReadAllText(skillMdPath);
                    content = content.Replace(
                        $"sandbox: {(!enabled).ToString().ToLower()}",
                        $"sandbox: {enabled.ToString().ToLower()}");
                    File.WriteAllText(skillMdPath, content);

                    Debug.Log($"[SkillMarket] 샌드박스 {(enabled ? "ON" : "OFF")}: {skillId}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SkillMarket] 샌드박스 설정 실패: {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        // ── 내부 유틸 ───────────────────────────────────────────────────

        private UniTask<List<SkillEntry>> ScanInstalledSkillsAsync(CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                var result = new List<SkillEntry>();
                var basePath = GetSkillsBasePath();
                if (!Directory.Exists(basePath)) return result;

                foreach (var dir in Directory.GetDirectories(basePath))
                {
                    var skillMd = Path.Combine(dir, "SKILL.md");
                    if (!File.Exists(skillMd)) continue;

                    var id = Path.GetFileName(dir);
                    var catalog = BuiltInCatalog.FirstOrDefault(s => s.Id == id);

                    result.Add(new SkillEntry
                    {
                        Id          = id,
                        Name        = catalog?.Name ?? id,
                        Description = catalog?.Description ?? "",
                        Category    = catalog?.Category ?? "기타",
                        IsInstalled = true,
                        InstallPath = dir,
                    });
                }

                return result;
            }, cancellationToken: ct);
        }

        private static string GetSkillsBasePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "openclaw", "skills");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw", "skills");
        }

        private static SkillEntry CloneEntry(SkillEntry s) => new()
        {
            Id = s.Id, Name = s.Name, Description = s.Description,
            Author = s.Author, Category = s.Category, Version = s.Version,
            IconUrl = s.IconUrl, Rating = s.Rating, Downloads = s.Downloads,
            IsInstalled = s.IsInstalled, IsSandboxed = s.IsSandboxed,
        };

        public void Dispose() => _skillChanged.Dispose();
    }
}
