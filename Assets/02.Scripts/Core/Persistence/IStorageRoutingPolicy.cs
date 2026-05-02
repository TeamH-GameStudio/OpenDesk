namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 테이블별 저장 위치(로컬/서버)를 결정하는 정책.<br/>
    /// <see cref="RoutingGameDataRepository"/>가 이 정책을 참조해 매 요청마다 위임 대상 저장소를 고른다.<br/>
    /// 런타임에 변경 가능하며, 실행 중 모드가 바뀌면 그 시점 이후의 저장/로드부터 적용된다(이미 캐시된 데이터는 자동 마이그레이션되지 않음).
    /// </summary>
    public interface IStorageRoutingPolicy
    {
        /// <summary>
        /// 명시적 override가 없는 테이블에 적용되는 기본 모드.
        /// </summary>
        StorageMode DefaultMode { get; set; }

        /// <summary>
        /// 특정 테이블에 적용할 모드를 조회한다 (override 우선, 없으면 DefaultMode).
        /// </summary>
        StorageMode GetMode(PersistedDataTable table);

        /// <summary>
        /// 특정 테이블의 저장 위치를 지정/변경한다.
        /// </summary>
        void SetMode(PersistedDataTable table, StorageMode mode);

        /// <summary>
        /// 특정 테이블의 override를 제거하고 DefaultMode를 따르도록 되돌린다.
        /// </summary>
        void ClearOverride(PersistedDataTable table);
    }
}
