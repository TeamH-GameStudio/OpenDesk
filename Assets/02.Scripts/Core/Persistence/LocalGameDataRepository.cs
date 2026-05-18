using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 로컬 파일 시스템 기반 IGameDataRepository 구현.<br/>
    /// 저장 위치: <see cref="OpenDeskPaths.GameData"/>/{table}.json<br/>
    /// 백업: 같은 디렉토리의 {table}_backup.json<br/>
    /// ProjectH 원본의 백업/복원 로직을 그대로 이식, BackEnd / LitJson 의존 제거.
    /// </summary>
    public class LocalGameDataRepository : IGameDataRepository
    {
        private const string SAVE_EXTENSION = ".json";
        private const string BACKUP_SUFFIX = "_backup";

        private static string GetSaveDirectory()
        {
            var directory = OpenDeskPaths.GameData;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return directory;
        }

        private static string GetPath(PersistedDataTable table) =>
            Path.Combine(GetSaveDirectory(), $"{table}{SAVE_EXTENSION}");

        private static string GetBackupPath(PersistedDataTable table) =>
            Path.Combine(GetSaveDirectory(), $"{table}{BACKUP_SUFFIX}{SAVE_EXTENSION}");

        public async UniTask<bool> GetAndSetDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
        {
            try
            {
                var path = GetPath(table);

                if (File.Exists(path))
                {
                    var jsonString = await File.ReadAllTextAsync(path);
                    data.FromJson(jsonString);

                    Debug.Log($"[LocalGameDataRepository] {table} 데이터 로드 성공");
                    return true;
                }

                Debug.Log($"[LocalGameDataRepository] {table} 파일이 없습니다. 기본값으로 초기화합니다.");
                data.InitializeDefault();
                return await UpdateDataAsync(table, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 데이터 로드 실패: {e.Message}");
                data.InitializeDefault();
                return false;
            }
        }

        public async UniTask<bool> UpdateDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
        {
            try
            {
                var jsonString = data.ToJson();

                var path = GetPath(table);
                var backupPath = GetBackupPath(table);

                if (File.Exists(path))
                {
                    File.Copy(path, backupPath, true);
                }

                await File.WriteAllTextAsync(path, jsonString);

                Debug.Log($"[LocalGameDataRepository] {table} 데이터 저장 성공");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 데이터 저장 실패: {e.Message}");
                TryRestoreBackup(table);
                return false;
            }
        }

        public bool UpdateDataSync<T>(PersistedDataTable table, T data) where T : IGameData
        {
            try
            {
                var jsonString = data.ToJson();

                var path = GetPath(table);
                var backupPath = GetBackupPath(table);

                if (File.Exists(path))
                {
                    File.Copy(path, backupPath, true);
                }

                File.WriteAllText(path, jsonString);

                Debug.Log($"[LocalGameDataRepository] {table} 데이터 동기 저장 성공");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 데이터 동기 저장 실패: {e.Message}");
                TryRestoreBackup(table);
                return false;
            }
        }

        public bool HasData(PersistedDataTable table) => File.Exists(GetPath(table));

        public bool DeleteData(PersistedDataTable table)
        {
            try
            {
                var path = GetPath(table);
                var backupPath = GetBackupPath(table);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                Debug.Log($"[LocalGameDataRepository] {table} 데이터 삭제 성공");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 데이터 삭제 실패: {e.Message}");
                return false;
            }
        }

        private static void TryRestoreBackup(PersistedDataTable table)
        {
            var backupPath = GetBackupPath(table);
            var path = GetPath(table);

            if (!File.Exists(backupPath)) return;

            try
            {
                File.Copy(backupPath, path, true);
                Debug.Log($"[LocalGameDataRepository] {table} 백업 복원 성공");
            }
            catch (Exception restoreEx)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 백업 복원 실패: {restoreEx.Message}");
            }
        }

        /// <summary>
        /// 저장 디렉토리 자체를 삭제 (전체 초기화).
        /// </summary>
        public bool DeleteAllData()
        {
            try
            {
                var directory = GetSaveDirectory();

                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                    Debug.Log("[LocalGameDataRepository] 모든 로컬 데이터 삭제 성공");
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] 모든 로컬 데이터 삭제 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 백업 파일에서 메인 파일 복원.
        /// </summary>
        public bool RestoreFromBackup(PersistedDataTable table)
        {
            try
            {
                var path = GetPath(table);
                var backupPath = GetBackupPath(table);

                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, path, true);
                    Debug.Log($"[LocalGameDataRepository] {table} 백업에서 복원 성공");
                    return true;
                }

                Debug.LogWarning($"[LocalGameDataRepository] {table} 백업 파일이 없습니다.");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LocalGameDataRepository] {table} 백업 복원 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 디스크에 존재하는 모든 테이블 식별자 목록.
        /// </summary>
        public List<PersistedDataTable> GetSavedTables()
        {
            var savedTables = new List<PersistedDataTable>();
            var directory = GetSaveDirectory();

            if (!Directory.Exists(directory))
                return savedTables;

            var files = Directory.GetFiles(directory, $"*{SAVE_EXTENSION}")
                .Where(f => !f.Contains(BACKUP_SUFFIX));

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (Enum.TryParse<PersistedDataTable>(fileName, out var table))
                {
                    savedTables.Add(table);
                }
            }

            return savedTables;
        }
    }
}
