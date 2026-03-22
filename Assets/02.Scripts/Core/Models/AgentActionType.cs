namespace OpenDesk.Core.Models
{
    public enum AgentActionType
    {
        Idle,
        TaskStarted,
        TaskCompleted,
        TaskFailed,
        Thinking,

        // 에이전틱 루프 세부 단계
        Planning,           // 계획 수립 중
        Executing,          // 도구/명령 실행 중
        Reviewing,          // 결과 검토 중

        // 도구 사용
        ToolUsing,          // 도구 호출 시작
        ToolResult,         // 도구 결과 수신

        // 서브 에이전트
        SubAgentSpawned,
        SubAgentCompleted,
        SubAgentFailed,

        // 연결
        Connected,
        Disconnected
    }
}
