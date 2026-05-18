using System;
using System.Collections.Generic;

namespace OpenDesk.Core.Models.Skills
{
    /// <summary>
    /// Unity → Python 미들웨어로 전달하는 활성 스킬 인덱스.
    /// Claude Code 의 SKILL.md 패턴 차용:
    ///   - 시스템 프롬프트에는 (name + description) 인덱스만 들어간다.
    ///   - 본문은 LLM 이 필요할 때 미들웨어 내장 도구 `read_skill_body(skill_id)` 로 가져온다.
    ///
    /// `body` 가 비어있으면 미들웨어는 `~/.opendesk/skills/{id}/SKILL.md` 디스크에서 읽고,
    /// 채워져 있으면 메모리 캐시를 우선한다.
    /// </summary>
    [Serializable]
    public class SkillLoadoutPayload
    {
        public string agentId;
        public List<SkillLoadoutEntry> skills = new();
    }

    [Serializable]
    public class SkillLoadoutEntry
    {
        public string id;
        public string name;
        public string description;
        public string body;       // 옵셔널 — 디스크 SKILL.md 가 없을 때 fallback 본문
    }
}
