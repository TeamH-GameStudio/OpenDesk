using System;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 서버 백엔드 미구현 상태의 placeholder.<br/>
    /// 라우팅 정책이 어떤 테이블을 <see cref="StorageMode.Server"/>로 지정했는데
    /// 실제 서버 구현체가 등록되지 않은 경우, 호출 즉시 명시적으로 throw하여
    /// 조용히 로컬로 fallback되거나 데이터가 누락되는 사고를 방지한다.<br/>
    /// 실제 서버 연동이 추가되면 이 클래스를 진짜 구현체로 교체한다.
    /// </summary>
    public class NullServerGameDataRepository : IServerGameDataRepository
    {
        public UniTask<bool> GetAndSetDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
            => throw NotImplemented(table, nameof(GetAndSetDataAsync));

        public UniTask<bool> UpdateDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
            => throw NotImplemented(table, nameof(UpdateDataAsync));

        public bool UpdateDataSync<T>(PersistedDataTable table, T data) where T : IGameData
            => throw NotImplemented(table, nameof(UpdateDataSync));

        public bool HasData(PersistedDataTable table)
            => throw NotImplemented(table, nameof(HasData));

        public bool DeleteData(PersistedDataTable table)
            => throw NotImplemented(table, nameof(DeleteData));

        private static NotImplementedException NotImplemented(PersistedDataTable table, string op) =>
            new($"[NullServerGameDataRepository] '{table}' 테이블이 Server 모드로 지정되었으나 서버 저장소 구현체가 등록되지 않았습니다. ({op})");
    }
}
