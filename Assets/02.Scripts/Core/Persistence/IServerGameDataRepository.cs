namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 원격 서버 저장소 마커 인터페이스.<br/>
    /// 라우터(<see cref="RoutingGameDataRepository"/>)가 로컬과 구별하기 위한 식별 용도이며,
    /// 추가 메서드는 향후 서버 SDK 사양에 맞춰 확장할 수 있다.
    /// </summary>
    public interface IServerGameDataRepository : IGameDataRepository
    {
    }
}
