using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// 로컬 폴더 + Google Drive 파일 통합 관리
    /// </summary>
    public class WorkspaceService : IWorkspaceService, IDisposable
    {
        private readonly Subject<WorkspaceEntry> _entryChanged = new();

        private string _localPath   = "";
        private string _driveFolderId = "";

        private const string PrefKey_LocalPath    = "OpenDesk_LocalPath";
        private const string PrefKey_DriveFolderId = "OpenDesk_DriveFolderId";

        public string LocalPath     => _localPath;
        public bool   IsInitialized => !string.IsNullOrEmpty(_localPath);

        public Observable<WorkspaceEntry> OnEntryChanged => _entryChanged;

        public WorkspaceService()
        {
            // 저장된 경로 복원
            _localPath     = PlayerPrefs.GetString(PrefKey_LocalPath,     "");
            _driveFolderId = PlayerPrefs.GetString(PrefKey_DriveFolderId, "");
        }

        public void SetLocalPath(string path)
        {
            if (!Directory.Exists(path))
            {
                Debug.LogWarning($"[Workspace] 유효하지 않은 경로: {path}");
                return;
            }

            _localPath = path;
            PlayerPrefs.SetString(PrefKey_LocalPath, path);
            PlayerPrefs.Save();
        }

        public async UniTask<IReadOnlyList<WorkspaceEntry>> GetEntriesAsync(CancellationToken ct = default)
        {
            var entries = new List<WorkspaceEntry>();

            // 로컬 파일 수집
            if (!string.IsNullOrEmpty(_localPath) && Directory.Exists(_localPath))
            {
                var localEntries = await GetLocalEntriesAsync(_localPath, ct);
                entries.AddRange(localEntries);
            }

            // Google Drive 파일 수집 — GOOGLE_DRIVE_ENABLED 시에만 동작
            // TODO: GoogleDrive 연동 활성화 시 복원

            return entries.OrderByDescending(e => e.LastModified).ToList();
        }

        public void OpenEntry(WorkspaceEntry entry)
        {
            if (entry.Source == WorkspaceSource.Local && File.Exists(entry.FullPath))
            {
                Application.OpenURL($"file://{entry.FullPath}");
            }
            else if (entry.Source == WorkspaceSource.GoogleDrive &&
                     !string.IsNullOrEmpty(entry.DriveFileId))
            {
                Application.OpenURL(
                    $"https://drive.google.com/file/d/{entry.DriveFileId}/view"
                );
            }
        }

        private UniTask<List<WorkspaceEntry>> GetLocalEntriesAsync(
            string dirPath, CancellationToken ct)
        {
            // 파일 I/O → 스레드 풀에서 실행
            return UniTask.RunOnThreadPool(() =>
            {
                var result = new List<WorkspaceEntry>();

                foreach (var filePath in Directory.GetFiles(dirPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var info = new FileInfo(filePath);
                    result.Add(new WorkspaceEntry
                    {
                        Name         = info.Name,
                        FullPath     = filePath,
                        Source       = WorkspaceSource.Local,
                        LastModified = info.LastWriteTimeUtc,
                        IsDirectory  = false,
                        SizeBytes    = info.Length,
                    });
                }

                foreach (var dirEntry in Directory.GetDirectories(dirPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var info = new DirectoryInfo(dirEntry);
                    result.Add(new WorkspaceEntry
                    {
                        Name         = info.Name,
                        FullPath     = dirEntry,
                        Source       = WorkspaceSource.Local,
                        LastModified = info.LastWriteTimeUtc,
                        IsDirectory  = true,
                    });
                }

                return result;
            }, cancellationToken: ct);
        }

        public void Dispose()
        {
            _entryChanged.Dispose();
        }
    }
}
