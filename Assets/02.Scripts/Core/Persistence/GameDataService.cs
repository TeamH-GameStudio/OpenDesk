using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using OpenDesk.Characters.Wardrobe.Persistence;
using UnityEngine;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 데이터 타입 ↔ 테이블 매핑을 위한 어트리뷰트.<br/>
    /// IGameData 구현 클래스에 부착하면 GameDataService가 자동 인식한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TableNameAttribute : Attribute
    {
        public PersistedDataTable Table { get; }
        public TableNameAttribute(PersistedDataTable table) => Table = table;
    }

    /// <summary>
    /// 데이터 타입 등록 메타데이터.<br/>
    /// <see cref="Mode"/>가 지정되면 서비스 시작 시 라우팅 정책에 push된다 (런타임에 정책 변경 가능).
    /// </summary>
    public class DataRegistration
    {
        public Type DataType { get; set; }
        public PersistedDataTable Table { get; set; }
        public StorageMode? Mode { get; set; }
    }

    /// <summary>
    /// IGameDataRepository 위에 캐시 / 변경 감지(IsDirty) / 배치 저장 / 이벤트를 얹은 서비스.
    /// ProjectH의 GameDataService에서 이식 — BackEnd 의존(_isServerMode, Backend.IsLogin) 제거.
    /// 저장 위치(로컬/서버) 라우팅은 주입된 IGameDataRepository(보통 RoutingGameDataRepository)와 IStorageRoutingPolicy가 담당한다.
    /// 새 도메인 추가 시 InitializeDataRegistrations에 항목을 등록한다.
    /// </summary>
    public class GameDataService : IGameDataService
    {
        private readonly Dictionary<PersistedDataTable, IGameData> _dataCache = new();
        private readonly Dictionary<Type, PersistedDataTable> _typeToTableMap = new();
        private readonly List<DataRegistration> _dataRegistrations = new();
        private readonly Dictionary<Type, IGameData> _instanceCache = new();
        private bool _isInitialized;

        public event Action<PersistedDataTable, IGameData> OnDataUpdated;
        public event Action OnAllDataSaved;
        public event Action<Exception> OnError;

        private readonly IGameDataRepository _repository;
        private readonly IStorageRoutingPolicy _routingPolicy;

        public GameDataService(IGameDataRepository repository, IStorageRoutingPolicy routingPolicy)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _routingPolicy = routingPolicy ?? throw new ArgumentNullException(nameof(routingPolicy));
            InitializeDataRegistrations();
            BuildTypeToTableMap();
            ApplyPreferredModes();
        }

        #region Data Registration

        /// <summary>
        /// 모든 IGameData 도메인 타입을 등록한다.
        /// 새 데이터 추가 시 이 메서드만 수정하면 된다.
        /// 또는 [TableName(...)] 어트리뷰트로 자동 인식 (GetTableName 참조).
        /// Mode를 지정하지 않으면 IStorageRoutingPolicy.DefaultMode가 적용된다.
        /// </summary>
        private void InitializeDataRegistrations()
        {
            _dataRegistrations.AddRange(new[]
            {
                // 캐릭터 아웃핏 — 로컬 저장. 사용자 개인 취향이라 서버 동기화는 후순위.
                new DataRegistration
                {
                    DataType = typeof(WardrobeOutfitData),
                    Table = PersistedDataTable.WardrobeOutfits,
                    Mode = StorageMode.Local,
                },

                // 향후 도메인 추가 위치 (예시)
                // new DataRegistration { DataType = typeof(AgentProfilesData), Table = PersistedDataTable.AgentProfiles, Mode = StorageMode.Server },
                // new DataRegistration { DataType = typeof(SessionsData),      Table = PersistedDataTable.Sessions /* Mode 생략 → DefaultMode 사용 */ },
            });
        }

        private void BuildTypeToTableMap()
        {
            foreach (var registration in _dataRegistrations)
            {
                _typeToTableMap[registration.DataType] = registration.Table;
            }
        }

        /// <summary>
        /// DataRegistration.Mode가 지정된 항목을 라우팅 정책에 반영한다.<br/>
        /// 호출 이후 외부에서 IStorageRoutingPolicy.SetMode로 다시 override할 수 있다.
        /// </summary>
        private void ApplyPreferredModes()
        {
            foreach (var registration in _dataRegistrations)
            {
                if (registration.Mode.HasValue)
                {
                    _routingPolicy.SetMode(registration.Table, registration.Mode.Value);
                }
            }
        }

        #endregion

        #region Generic Data Access

        public T GetData<T>() where T : class, IGameData
        {
            try
            {
                var table = GetTableName<T>();
                return _dataCache.TryGetValue(table, out var data) ? data as T : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] GetData<{typeof(T).Name}> 오류: {ex.Message}");
                return null;
            }
        }

        public bool HasData<T>() where T : class, IGameData
        {
            try
            {
                var table = GetTableName<T>();
                return _dataCache.ContainsKey(table) && _dataCache[table] != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] HasData<{typeof(T).Name}> 오류: {ex.Message}");
                return false;
            }
        }

        private PersistedDataTable GetTableName<T>() where T : IGameData
        {
            var type = typeof(T);

            if (_typeToTableMap.TryGetValue(type, out var table))
                return table;

            var attribute = type.GetCustomAttribute<TableNameAttribute>();
            if (attribute != null)
                return attribute.Table;

            throw new InvalidOperationException($"PersistedDataTable not found for type {type.Name}");
        }

        #endregion

        #region Data Loading

        public async UniTask InitializeAllData()
        {
            if (_isInitialized) return;

            try
            {
                var initTasks = new List<UniTask>();

                foreach (var registration in _dataRegistrations)
                {
                    initTasks.Add(InitializeSingleData(registration));
                }

                const int batchSize = 3;
                for (var i = 0; i < initTasks.Count; i += batchSize)
                {
                    var batch = initTasks.Skip(i).Take(batchSize);
                    await UniTask.WhenAll(batch);

                    if (i + batchSize < initTasks.Count)
                    {
                        await UniTask.DelayFrame(1);
                    }
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] 데이터 초기화 중 전체 오류: {ex.Message}");
                OnError?.Invoke(ex);
                throw;
            }
        }

        private UniTask InitializeSingleData(DataRegistration registration)
        {
            try
            {
                if (!_instanceCache.TryGetValue(registration.DataType, out var instance))
                {
                    instance = CreateInstance(registration.DataType);
                    _instanceCache[registration.DataType] = instance;
                }

                instance.InitializeDefault();
                _dataCache[registration.Table] = instance;

                return UniTask.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {registration.DataType.Name} 초기화 중 오류: {ex.Message}");
                throw;
            }
        }

        private static IGameData CreateInstance(Type dataType)
        {
            try
            {
                return Activator.CreateInstance(dataType) as IGameData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {dataType.Name} 인스턴스 생성 실패: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Repository Operations

        public async UniTask<bool> FetchDataFromServer()
        {
            try
            {
                var fetchTasks = new List<UniTask<bool>>();
                var tableDataPairs = _dataCache.ToList();

                const int batchSize = 5;

                for (var i = 0; i < tableDataPairs.Count; i += batchSize)
                {
                    var batch = tableDataPairs.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async kvp =>
                    {
                        var (table, data) = (kvp.Key, kvp.Value);
                        return await FetchSingleData(table, data);
                    });

                    var batchResults = await UniTask.WhenAll(batchTasks);
                    fetchTasks.AddRange(batchResults.Select(UniTask.FromResult));

                    if (i + batchSize < tableDataPairs.Count)
                    {
                        await UniTask.DelayFrame(1);
                    }
                }

                var results = await UniTask.WhenAll(fetchTasks);
                var allSuccess = results.All(r => r);

                if (!allSuccess)
                {
                    Debug.LogError("[GameDataService] 일부 데이터 가져오기 실패");
                }

                return allSuccess;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameDataService] 데이터 가져오기 중 예외 발생: {e.Message}");
                OnError?.Invoke(e);
                return false;
            }
        }

        private async UniTask<bool> FetchSingleData(PersistedDataTable table, IGameData data)
        {
            try
            {
                var result = await _repository.GetAndSetDataAsync(table, data);

                if (!result)
                {
                    Debug.LogError($"[GameDataService] {table} 데이터 조회 실패");
                    return false;
                }

                OnDataUpdated?.Invoke(table, data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {table} 데이터 조회 중 오류: {ex.Message}");
                return false;
            }
        }

        public async UniTask<bool> FetchData<T>() where T : class, IGameData
        {
            try
            {
                var table = GetTableName<T>();
                var data = GetData<T>();

                if (data == null)
                {
                    Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터가 캐시에 없습니다.");
                    return false;
                }

                var result = await _repository.GetAndSetDataAsync(table, data);
                if (result)
                {
                    OnDataUpdated?.Invoke(table, data);
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터 가져오기 중 오류: {ex.Message}");
                OnError?.Invoke(ex);
                return false;
            }
        }

        public async UniTask<bool> SaveData<T>() where T : class, IGameData
        {
            if (!IsRepositoryReady())
                return false;

            try
            {
                var table = GetTableName<T>();
                var data = GetData<T>();

                if (data == null)
                {
                    Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터가 캐시에 없습니다.");
                    return false;
                }

                if (!data.IsDirty)
                {
                    return true;
                }

                return await SaveSingleData(table, data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터 저장 중 오류: {ex.Message}");
                OnError?.Invoke(ex);
                return false;
            }
        }

        public async UniTask<bool> SaveData<T>(T data) where T : class, IGameData
        {
            if (!IsRepositoryReady())
                return false;

            if (data == null)
            {
                Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터가 null입니다.");
                return false;
            }

            try
            {
                var table = GetTableName<T>();

                _dataCache[table] = data;

                if (!data.IsDirty)
                {
                    return true;
                }

                return await SaveSingleData(table, data);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {typeof(T).Name} 데이터 저장 중 오류: {ex.Message}");
                OnError?.Invoke(ex);
                return false;
            }
        }

        public async UniTask<bool> SaveAllData(PersistedDataTable? specificTable = null)
        {
            if (!IsRepositoryReady())
                return false;

            try
            {
                var anyUpdated = false;
                var saveTasks = new List<UniTask<bool>>();

                foreach (var kvp in _dataCache)
                {
                    var (table, data) = (kvp.Key, kvp.Value);

                    if (specificTable.HasValue && table != specificTable.Value)
                        continue;

                    if (data?.IsDirty == true)
                    {
                        saveTasks.Add(SaveSingleData(table, data));
                    }
                }

                if (saveTasks.Count > 0)
                {
                    const int batchSize = 3;
                    for (var i = 0; i < saveTasks.Count; i += batchSize)
                    {
                        var batch = saveTasks.Skip(i).Take(batchSize);
                        var results = await UniTask.WhenAll(batch);
                        anyUpdated = anyUpdated || results.Any(r => r);

                        if (i + batchSize < saveTasks.Count)
                        {
                            await UniTask.DelayFrame(1);
                        }
                    }

                    if (anyUpdated)
                    {
                        OnAllDataSaved?.Invoke();
                    }
                }

                return anyUpdated;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] 데이터 저장 중 오류: {ex.Message}");
                OnError?.Invoke(ex);
                return false;
            }
        }

        private async UniTask<bool> SaveSingleData(PersistedDataTable table, IGameData data)
        {
            try
            {
                var success = await _repository.UpdateDataAsync(table, data);

                if (success)
                {
                    data.ResetDirty();
                    OnDataUpdated?.Invoke(table, data);
                }
                else
                {
                    Debug.LogError($"[GameDataService] {table} 데이터 저장 실패");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] {table} 데이터 저장 중 예외: {ex.Message}");
                return false;
            }
        }

        public void SaveAllDataSync(PersistedDataTable? specificTable = null)
        {
            if (!IsRepositoryReady())
                return;

            try
            {
                foreach (var kvp in _dataCache)
                {
                    var (table, data) = (kvp.Key, kvp.Value);

                    if (specificTable.HasValue && table != specificTable.Value)
                        continue;

                    if (data?.IsDirty != true) continue;

                    try
                    {
                        var success = _repository.UpdateDataSync(table, data);

                        if (success)
                        {
                            data.ResetDirty();
                        }
                        else
                        {
                            Debug.LogError($"[GameDataService] {table} 데이터 저장 실패 (동기)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GameDataService] {table} 동기 저장 중 오류: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameDataService] 동기 데이터 저장 중 전체 오류: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        private bool IsRepositoryReady()
        {
            if (_repository == null)
            {
                Debug.LogError("[GameDataService] Repository가 설정되지 않았습니다.");
                return false;
            }

            // 로컬 모드 전용 — ProjectH의 Backend.IsInitialized / Backend.IsLogin 체크 제거.
            return true;
        }

        public void ClearCache()
        {
            _dataCache.Clear();
            _instanceCache.Clear();
            _isInitialized = false;
        }

        public void Dispose()
        {
            ClearCache();
            OnDataUpdated = null;
            OnAllDataSaved = null;
            OnError = null;
        }

        public void Reset()
        {
            foreach (var registration in _dataRegistrations)
            {
                if (_dataCache.TryGetValue(registration.Table, out var data))
                {
                    data.ResetAllData();
                }
            }
        }

        #endregion
    }
}
