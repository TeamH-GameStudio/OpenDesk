using System.Collections.Generic;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// OpenClaw .yaml 설정 파싱
    /// 순수 파싱 전용 — 파일 I/O는 경로를 받아서 처리
    /// </summary>
    public interface IAgentConfigParser
    {
        // 파일 경로를 받아 에이전트 목록 반환
        // 파싱 실패 시 빈 목록 반환 (예외 던지지 않음)
        IReadOnlyList<AgentConfig> ParseFromFile(string yamlPath);

        // Raw YAML 문자열 직접 파싱 (테스트용)
        IReadOnlyList<AgentConfig> ParseFromString(string yamlContent);
    }
}
