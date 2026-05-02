namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 영속 데이터의 저장 위치.
    /// </summary>
    public enum StorageMode
    {
        /// <summary>로컬 파일 시스템(<see cref="LocalGameDataRepository"/>).</summary>
        Local,

        /// <summary>원격 서버(<see cref="IServerGameDataRepository"/>). 미구현 시 호출하면 throw.</summary>
        Server,
    }
}
