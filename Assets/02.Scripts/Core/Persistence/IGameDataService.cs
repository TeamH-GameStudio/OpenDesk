using System;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 게임 데이터 서비스 인터페이스.<br/>
    /// IGameDataRepository를 한 단계 위에서 감싸 캐싱, 변경 감지(IsDirty), 배치 처리, 이벤트를 제공한다.
    /// ProjectH의 IGameDataService에서 이식 — BackEnd 의존 제거.
    /// </summary>
    public interface IGameDataService : IDisposable
    {
        /// <summary>
        /// 단일 테이블 저장/로드 시 발화.
        /// </summary>
        event Action<PersistedDataTable, IGameData> OnDataUpdated;

        /// <summary>
        /// SaveAllData 성공 시 발화.
        /// </summary>
        event Action OnAllDataSaved;

        /// <summary>
        /// 내부 오류가 발생했을 때 발화.
        /// </summary>
        event Action<Exception> OnError;

        /// <summary>
        /// 캐시된 데이터를 제네릭으로 조회한다.
        /// </summary>
        T GetData<T>() where T : class, IGameData;

        /// <summary>
        /// 데이터가 캐시에 존재하는지 확인한다.
        /// </summary>
        bool HasData<T>() where T : class, IGameData;

        /// <summary>
        /// 등록된 모든 데이터를 기본값으로 초기화한다 (앱 시작 시 1회).
        /// </summary>
        UniTask InitializeAllData();

        /// <summary>
        /// 저장소에서 모든 데이터를 가져와 캐시를 채운다.
        /// </summary>
        UniTask<bool> FetchDataFromServer();

        /// <summary>
        /// 단일 데이터만 저장소에서 가져온다.
        /// </summary>
        UniTask<bool> FetchData<T>() where T : class, IGameData;

        /// <summary>
        /// 단일 데이터를 저장한다 (변경 사항이 있을 때만).
        /// </summary>
        UniTask<bool> SaveData<T>() where T : class, IGameData;

        /// <summary>
        /// 외부에서 받은 데이터 객체를 캐시에 적용 후 저장한다.
        /// </summary>
        UniTask<bool> SaveData<T>(T data) where T : class, IGameData;

        /// <summary>
        /// 변경된 모든 데이터를 일괄 저장한다.
        /// <paramref name="specificTable"/>이 지정되면 해당 테이블만 저장.
        /// </summary>
        UniTask<bool> SaveAllData(PersistedDataTable? specificTable = null);

        /// <summary>
        /// 동기 저장 (OnApplicationQuit 등에서 사용).
        /// </summary>
        void SaveAllDataSync(PersistedDataTable? specificTable = null);

        /// <summary>
        /// 캐시를 모두 비운다.
        /// </summary>
        void ClearCache();

        /// <summary>
        /// 등록된 모든 데이터를 초기 상태로 되돌린다.
        /// </summary>
        void Reset();
    }
}
