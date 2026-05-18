using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models.Skills;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Services.Skills
{
    /// <summary>
    /// 정적 JSON 레지스트리 기반 인스톨러.
    /// 1. catalog 에서 엔트리 조회 → 2. zip 임시 다운로드 → 3. SHA256 검증 → 4. 매니페스트 검증 → 5. atomic move.
    /// 오류 시 임시 파일 정리 + 카탈로그 상태 미변경.
    /// </summary>
    public class SkillInstallerService : ISkillInstallerService, IDisposable
    {
        private readonly IRemoteSkillRegistry _registry;
        private readonly ISkillCatalogService _catalog;
        private readonly Subject<SkillInstallEvent> _onChanged = new();

        public Observable<SkillInstallEvent> OnInstallChanged => _onChanged;

        public SkillInstallerService(IRemoteSkillRegistry registry, ISkillCatalogService catalog)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public async UniTask<bool> InstallAsync(string skillId, IProgress<float> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                Debug.LogWarning("[SkillInstaller] skillId 비어있음");
                return false;
            }

            var descriptor = _catalog.GetById(skillId);
            if (descriptor == null)
            {
                Debug.LogWarning($"[SkillInstaller] 카탈로그에 없는 스킬: {skillId}");
                return false;
            }

            var entry = FindCatalogEntry(skillId);
            if (entry == null)
            {
                Debug.LogWarning($"[SkillInstaller] catalog entry 미발견: {skillId}");
                return false;
            }

            EnsureDirectory(OpenDeskPaths.SkillsTmp);
            EnsureDirectory(OpenDeskPaths.Skills);

            var tempZip = Path.Combine(OpenDeskPaths.SkillsTmp, $"{skillId}-{Guid.NewGuid():N}.zip");
            var tempExtractDir = Path.Combine(OpenDeskPaths.SkillsTmp, $"{skillId}-{Guid.NewGuid():N}");
            var finalDir = Path.Combine(OpenDeskPaths.Skills, skillId);

            try
            {
                // 1. 다운로드
                if (string.IsNullOrEmpty(entry.downloadUrl))
                {
                    Debug.LogWarning($"[SkillInstaller] downloadUrl 없음: {skillId} — 빌트인 fallback 으로 더미 설치");
                    return InstallBuiltinFallback(descriptor, finalDir);
                }

                await _registry.DownloadSkillPackageAsync(entry, tempZip, progress, ct);
                ct.ThrowIfCancellationRequested();

                // 2. 체크섬 검증
                if (!string.IsNullOrEmpty(entry.checksum))
                {
                    var ok = await UniTask.RunOnThreadPool(
                        () => VerifyChecksum(tempZip, entry.checksum),
                        cancellationToken: ct);
                    if (!ok)
                    {
                        Debug.LogError($"[SkillInstaller] 체크섬 불일치: {skillId}");
                        return false;
                    }
                }

                // 3. 임시 디렉토리에 압축 해제
                await UniTask.RunOnThreadPool(() =>
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, recursive: true);
                    ZipFile.ExtractToDirectory(tempZip, tempExtractDir);
                }, cancellationToken: ct);
                ct.ThrowIfCancellationRequested();

                // 4. 매니페스트 검증
                var manifest = LoadManifest(tempExtractDir);
                if (manifest == null || string.IsNullOrEmpty(manifest.id) || manifest.id != skillId)
                {
                    Debug.LogError($"[SkillInstaller] 매니페스트 검증 실패: {skillId}");
                    return false;
                }

                // 5. atomic move (기존 디렉토리 백업 → 새 디렉토리로 교체 → 백업 삭제)
                await UniTask.RunOnThreadPool(() => AtomicReplaceDirectory(tempExtractDir, finalDir),
                    cancellationToken: ct);

                _catalog.NotifyInstallStateChanged(skillId, true, finalDir);
                _onChanged.OnNext(new SkillInstallEvent(skillId, true, finalDir));
                Debug.Log($"[SkillInstaller] 설치 완료: {skillId} → {finalDir}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[SkillInstaller] 설치 실패 ({skillId}): {ex.Message}");
                return false;
            }
            finally
            {
                SafeDeleteFile(tempZip);
                SafeDeleteDirectory(tempExtractDir);
            }
        }

        public UniTask<bool> UninstallAsync(string skillId, CancellationToken ct)
        {
            return UniTask.RunOnThreadPool(() =>
            {
                try
                {
                    var dir = Path.Combine(OpenDeskPaths.Skills, skillId);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);

                    _catalog.NotifyInstallStateChanged(skillId, false, string.Empty);
                    _onChanged.OnNext(new SkillInstallEvent(skillId, false, string.Empty));
                    Debug.Log($"[SkillInstaller] 삭제 완료: {skillId}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SkillInstaller] 삭제 실패 ({skillId}): {ex.Message}");
                    return false;
                }
            }, cancellationToken: ct);
        }

        public UniTask<IReadOnlyList<SkillDescriptor>> ScanInstalledAsync(CancellationToken ct)
        {
            return UniTask.RunOnThreadPool<IReadOnlyList<SkillDescriptor>>(() =>
            {
                var result = new List<SkillDescriptor>();
                var basePath = OpenDeskPaths.Skills;
                if (!Directory.Exists(basePath)) return result;

                foreach (var dir in Directory.GetDirectories(basePath))
                {
                    var skillId = Path.GetFileName(dir);
                    if (skillId.StartsWith(".")) continue;   // .tmp 등 숨김 디렉토리 스킵

                    var manifest = LoadManifest(dir);
                    if (manifest != null)
                    {
                        result.Add(manifest.ToDescriptor(dir));
                        continue;
                    }

                    // SKILL.md 만 있는 레거시 디렉토리 — 카탈로그 메타데이터로 fallback
                    var legacySkillMd = Path.Combine(dir, "SKILL.md");
                    if (File.Exists(legacySkillMd))
                    {
                        var fromCatalog = _catalog.GetById(skillId);
                        if (fromCatalog != null)
                            result.Add(fromCatalog.WithInstallState(true, dir));
                    }
                }

                return result;
            }, cancellationToken: ct);
        }

        // ── 내부 ──────────────────────────────────────────────────

        private SkillCatalogEntry FindCatalogEntry(string skillId)
        {
            // catalog service 가 raw entry 를 공개하지 않으므로 BuiltInFallback / 캐시 envelope 를 직접 조회.
            // 단순화를 위해 catalog 의 SkillDescriptor → SkillCatalogEntry 역변환 어댑터로 충분 (downloadUrl 미보유 시 빌트인 fallback 분기).
            var descriptor = _catalog.GetById(skillId);
            if (descriptor == null) return null;

            return new SkillCatalogEntry
            {
                id = descriptor.Id,
                displayName = descriptor.DisplayName,
                description = descriptor.Description,
                category = descriptor.Category.ToSerializedKey(),
                version = descriptor.Version,
                author = descriptor.Author,
                iconUrl = descriptor.IconUrl,
                downloadUrl = descriptor.DownloadUrl,
                checksum = descriptor.Checksum,
                publishedAt = 0,
                downloads = descriptor.Downloads,
                rating = descriptor.Rating,
                recommendedRoles = descriptor.RecommendedRoles?
                    .Select(r => r.ToString()).ToList() ?? new List<string>(),
            };
        }

        private bool InstallBuiltinFallback(SkillDescriptor descriptor, string finalDir)
        {
            try
            {
                Directory.CreateDirectory(finalDir);
                var manifest = new SkillManifest
                {
                    id = descriptor.Id,
                    displayName = descriptor.DisplayName,
                    description = descriptor.Description,
                    category = descriptor.Category.ToSerializedKey(),
                    version = descriptor.Version,
                    author = descriptor.Author,
                    iconUrl = descriptor.IconUrl,
                    promptContent = string.IsNullOrEmpty(descriptor.PromptContent)
                        ? $"# {descriptor.DisplayName}\n\n{descriptor.Description}\n\n이 스킬이 활성화되면 에이전트가 자동으로 적절한 상황에서 호출합니다."
                        : descriptor.PromptContent,
                    mcpServerCommand = descriptor.McpServerCommand,
                    requiredTokens = new List<string>(descriptor.RequiredTokens ?? Array.Empty<string>()),
                    recommendedRoles = descriptor.RecommendedRoles?
                        .Select(r => r.ToString()).ToList() ?? new List<string>(),
                };

                var json = JsonUtility.ToJson(manifest, prettyPrint: true);
                File.WriteAllText(Path.Combine(finalDir, "manifest.json"), json);
                File.WriteAllText(Path.Combine(finalDir, "prompt.md"), manifest.promptContent);

                _catalog.NotifyInstallStateChanged(descriptor.Id, true, finalDir);
                _onChanged.OnNext(new SkillInstallEvent(descriptor.Id, true, finalDir));
                Debug.Log($"[SkillInstaller] 빌트인 fallback 설치: {descriptor.Id} → {finalDir}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SkillInstaller] 빌트인 fallback 실패 ({descriptor.Id}): {ex.Message}");
                return false;
            }
        }

        private static SkillManifest LoadManifest(string skillDir)
        {
            try
            {
                var jsonPath = Path.Combine(skillDir, "manifest.json");
                if (!File.Exists(jsonPath)) return null;

                var manifest = JsonUtility.FromJson<SkillManifest>(File.ReadAllText(jsonPath));
                if (manifest == null) return null;

                // promptContent 가 별도 파일(prompt.md) 로 분리됐다면 본문 머지
                if (string.IsNullOrEmpty(manifest.promptContent))
                {
                    var promptFile = string.IsNullOrEmpty(manifest.promptFile) ? "prompt.md" : manifest.promptFile;
                    var promptPath = Path.Combine(skillDir, promptFile);
                    if (File.Exists(promptPath))
                        manifest.promptContent = File.ReadAllText(promptPath);
                }
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SkillInstaller] manifest 파싱 실패 ({skillDir}): {ex.Message}");
                return null;
            }
        }

        private static bool VerifyChecksum(string filePath, string expectedChecksum)
        {
            if (string.IsNullOrEmpty(expectedChecksum)) return true;

            // "sha256:xxx" / "sha256-xxx" / 원본 hex 지원
            var expected = expectedChecksum.Trim();
            var sepIndex = expected.IndexOfAny(new[] { ':', '-' });
            if (sepIndex > 0) expected = expected.Substring(sepIndex + 1);
            expected = expected.ToLowerInvariant();

            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var actual = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return actual == expected;
        }

        private static void AtomicReplaceDirectory(string sourceDir, string targetDir)
        {
            var parent = Path.GetDirectoryName(targetDir);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            var backupDir = $"{targetDir}.bak-{Guid.NewGuid():N}";
            if (Directory.Exists(targetDir))
                Directory.Move(targetDir, backupDir);

            try
            {
                Directory.Move(sourceDir, targetDir);
                if (Directory.Exists(backupDir))
                    Directory.Delete(backupDir, recursive: true);
            }
            catch
            {
                // 롤백 시도
                if (!Directory.Exists(targetDir) && Directory.Exists(backupDir))
                    Directory.Move(backupDir, targetDir);
                throw;
            }
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void SafeDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }

        private static void SafeDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* ignore */ }
        }

        public void Dispose() => _onChanged.Dispose();
    }
}
