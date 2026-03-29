using System;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 1명의 대화 세션 데이터.
    /// 에이전트 : 세션 = 1 : N (복수 대화 가능).
    /// </summary>
    [Serializable]
    public class AgentSession
    {
        public string SessionId;        // 세션 고유 ID
        public int AgentIndex;          // AgentDataStore의 인덱스 (에이전트 식별)
        public string AgentName;        // 표시용 이름
        public AgentRole Role;          // 표시용 역할
        public string Title;            // 세션 제목 ("새 대화", 또는 첫 메시지 기반)
        public string LastMessage;      // 마지막 메시지 미리보기
        public DateTime CreatedAt;
        public DateTime LastActivity;
        public bool IsActive;           // 현재 활성 세션 여부
    }
}
