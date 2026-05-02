using System;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 정책(<see cref="IStorageRoutingPolicy"/>)을 보고 테이블 단위로
    /// <see cref="LocalGameDataRepository"/> 혹은 <see cref="IServerGameDataRepository"/>에 위임하는 IGameDataRepository.<br/>
    /// GameDataService는 이 라우팅 저장소만 알고 있고, 실제 저장 위치 분기는 여기에서만 일어난다.
    /// </summary>
    public class RoutingGameDataRepository : IGameDataRepository
    {
        private readonly LocalGameDataRepository _local;
        private readonly IServerGameDataRepository _server;
        private readonly IStorageRoutingPolicy _policy;

        public RoutingGameDataRepository(
            LocalGameDataRepository local,
            IServerGameDataRepository server,
            IStorageRoutingPolicy policy)
        {
            _local = local ?? throw new ArgumentNullException(nameof(local));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        private IGameDataRepository Pick(PersistedDataTable table) =>
            _policy.GetMode(table) switch
            {
                StorageMode.Server => _server,
                _ => _local,
            };

        public UniTask<bool> GetAndSetDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
            => Pick(table).GetAndSetDataAsync(table, data);

        public UniTask<bool> UpdateDataAsync<T>(PersistedDataTable table, T data) where T : IGameData
            => Pick(table).UpdateDataAsync(table, data);

        public bool UpdateDataSync<T>(PersistedDataTable table, T data) where T : IGameData
            => Pick(table).UpdateDataSync(table, data);

        public bool HasData(PersistedDataTable table) => Pick(table).HasData(table);

        public bool DeleteData(PersistedDataTable table) => Pick(table).DeleteData(table);
    }
}
