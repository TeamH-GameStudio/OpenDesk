using Cysharp.Threading.Tasks;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 게임/앱 데이터 저장소 인터페이스.<br/>
    /// 로컬 파일 시스템 / 향후 서버 백엔드 등 모든 저장소가 이 계약을 구현한다.
    /// VContainer를 통해 원하는 구현체를 주입한다.
    /// ProjectH 원본에서 BackEnd 종속을 제거하고 IGameData 측 직렬화 일반화에 맞춰 단순화.
    /// </summary>
    public interface IGameDataRepository
    {
        /// <summary>
        /// 데이터를 조회해 <paramref name="data"/> 객체에 적용한다.
        /// 저장된 데이터가 없으면 InitializeDefault 후 새로 저장한다.
        /// </summary>
        UniTask<bool> GetAndSetDataAsync<T>(PersistedDataTable table, T data) where T : IGameData;

        /// <summary>
        /// 데이터를 비동기로 저장/업데이트한다.
        /// </summary>
        UniTask<bool> UpdateDataAsync<T>(PersistedDataTable table, T data) where T : IGameData;

        /// <summary>
        /// 데이터를 동기적으로 저장한다 (앱 종료 콜백 등에서 사용).
        /// </summary>
        bool UpdateDataSync<T>(PersistedDataTable table, T data) where T : IGameData;

        /// <summary>
        /// 특정 테이블의 데이터 존재 여부.
        /// </summary>
        bool HasData(PersistedDataTable table);

        /// <summary>
        /// 특정 테이블의 데이터를 삭제한다.
        /// </summary>
        bool DeleteData(PersistedDataTable table);
    }
}
