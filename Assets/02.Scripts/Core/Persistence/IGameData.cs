namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 영속화 가능한 데이터 객체 계약.<br/>
    /// ProjectH의 IGameData에서 이식하되 BackEnd.Param / LitJson.JsonData 의존을 제거하고
    /// 단순 string JSON 기반으로 일반화했다.
    /// 구현체는 직렬화/역직렬화 로직을 직접 책임진다 (JsonUtility, Newtonsoft 등 자유 선택).
    /// </summary>
    public interface IGameData
    {
        /// <summary>
        /// 마지막 저장 이후 변경 여부. true이면 SaveAllData 시 저장 대상.
        /// </summary>
        bool IsDirty { get; }

        /// <summary>
        /// 데이터 변경을 알린다 (필드 setter 등에서 호출).
        /// </summary>
        void MarkAsDirty();

        /// <summary>
        /// 저장 성공 시 IsDirty 플래그를 해제한다.
        /// </summary>
        void ResetDirty();

        /// <summary>
        /// 신규 사용자/저장 파일 부재 시 호출되는 기본값 초기화.
        /// </summary>
        void InitializeDefault();

        /// <summary>
        /// 모든 데이터를 초기 상태로 되돌린다 (회원 탈퇴/리셋 시).
        /// </summary>
        void ResetAllData();

        /// <summary>
        /// 현재 데이터를 JSON 문자열로 직렬화한다 (저장 시).
        /// 이전 ToParam().GetJson()의 역할을 단일 메서드로 통합.
        /// </summary>
        string ToJson();

        /// <summary>
        /// JSON 문자열에서 데이터를 복원한다 (로드 시).
        /// 이전 FromJson(JsonData)의 역할을 string 인자로 일반화.
        /// </summary>
        void FromJson(string json);
    }
}
