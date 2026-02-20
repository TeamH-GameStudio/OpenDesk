#if GOOGLE_DRIVE_ENABLED
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// Google Drive OAuth 인증 + 파일 목록 조회
    /// refresh_token은 로컬 파일로 암호화 저장 (FileDataStore)
    /// </summary>
    public class GoogleDriveService : IGoogleDriveService, IDisposable
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveReadonly };
        private const string AppName  = "OpenDesk";
        private const string TokenDir = "GoogleDriveToken";  // Application.persistentDataPath 하위

        private DriveService _driveService;
        private readonly ReactiveProperty<bool> _authState = new(false);

        public bool IsAuthenticated => _authState.Value;
        public ReadOnlyReactiveProperty<bool> AuthState => _authState;

        public async UniTask<bool> AuthenticateAsync(CancellationToken ct = default)
        {
            try
            {
                // client_secret.json 경로 (StreamingAssets에 넣어야 함)
                var secretPath = Path.Combine(
                    Application.streamingAssetsPath, "client_secret.json"
                );

                if (!File.Exists(secretPath))
                {
                    Debug.LogError("[Drive] client_secret.json 없음. StreamingAssets에 넣어주세요.");
                    return false;
                }

                var tokenPath = Path.Combine(
                    Application.persistentDataPath, TokenDir
                );

                UserCredential credential;
                using var stream = new FileStream(secretPath, FileMode.Open, FileAccess.Read);

                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    ct,
                    new FileDataStore(tokenPath, fullPath: true)
                );

                _driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName       = AppName,
                });

                _authState.Value = true;
                Debug.Log("[Drive] Google Drive 인증 완료");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Drive] 인증 실패: {ex.Message}");
                _authState.Value = false;
                return false;
            }
        }

        public void RevokeAuth()
        {
            _driveService = null;
            _authState.Value = false;

            // 저장된 토큰 삭제
            var tokenPath = Path.Combine(Application.persistentDataPath, TokenDir);
            if (Directory.Exists(tokenPath))
                Directory.Delete(tokenPath, recursive: true);
        }

        public async UniTask<IReadOnlyList<WorkspaceEntry>> ListFilesAsync(
            string folderId,
            CancellationToken ct = default)
        {
            if (_driveService == null)
                return new List<WorkspaceEntry>();

            return await UniTask.RunOnThreadPool(async () =>
            {
                var result  = new List<WorkspaceEntry>();
                var request = _driveService.Files.List();

                request.Q          = $"'{folderId}' in parents and trashed = false";
                request.Fields     = "files(id, name, mimeType, modifiedTime, size)";
                request.PageSize   = 100;

                var response = await request.ExecuteAsync(ct);

                foreach (var file in response.Files)
                {
                    result.Add(new WorkspaceEntry
                    {
                        Name         = file.Name,
                        DriveFileId  = file.Id,
                        MimeType     = file.MimeType,
                        Source       = WorkspaceSource.GoogleDrive,
                        LastModified = file.ModifiedTimeDateTimeOffset?.UtcDateTime
                                       ?? DateTime.MinValue,
                        SizeBytes    = file.Size ?? 0,
                        IsDirectory  = file.MimeType ==
                                       "application/vnd.google-apps.folder",
                    });
                }

                return (IReadOnlyList<WorkspaceEntry>)result;
            }, cancellationToken: ct);
        }

        public void Dispose()
        {
            _driveService?.Dispose();
            _authState.Dispose();
        }
    }
}
#endif
