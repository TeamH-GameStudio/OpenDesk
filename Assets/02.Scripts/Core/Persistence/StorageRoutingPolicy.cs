using System.Collections.Generic;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 기본값 + 테이블별 override 형태의 단순한 라우팅 정책 구현.<br/>
    /// Unity 메인 스레드 사용 가정 — 다중 스레드에서 변경할 일이 있다면 외부에서 동기화할 것.
    /// </summary>
    public class StorageRoutingPolicy : IStorageRoutingPolicy
    {
        private readonly Dictionary<PersistedDataTable, StorageMode> _overrides = new();

        public StorageMode DefaultMode { get; set; } = StorageMode.Local;

        public StorageMode GetMode(PersistedDataTable table) =>
            _overrides.TryGetValue(table, out var mode) ? mode : DefaultMode;

        public void SetMode(PersistedDataTable table, StorageMode mode) =>
            _overrides[table] = mode;

        public void ClearOverride(PersistedDataTable table) =>
            _overrides.Remove(table);
    }
}
