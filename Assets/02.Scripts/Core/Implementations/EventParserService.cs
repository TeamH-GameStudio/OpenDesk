using System;
using System.Collections.Generic;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// OpenClaw Gateway에서 오는 Raw JSON을 AgentEvent로 변환
    /// Stateless — Transient으로 등록
    /// </summary>
    public class EventParserService : IEventParserService
    {
        // JSON eventType 키 → ActionType 매핑 테이블
        private readonly Dictionary<string, AgentActionType> _rules = new()
        {
            { "task_started",        AgentActionType.TaskStarted     },
            { "task_completed",      AgentActionType.TaskCompleted   },
            { "task_failed",         AgentActionType.TaskFailed      },
            { "thinking",            AgentActionType.Thinking        },
            { "subagent_spawned",    AgentActionType.SubAgentSpawned },
            { "subagent_completed",  AgentActionType.SubAgentCompleted },
            { "subagent_failed",     AgentActionType.SubAgentFailed  },
            { "connected",           AgentActionType.Connected       },
            { "disconnected",        AgentActionType.Disconnected    },
        };

        public AgentEvent? Parse(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            try
            {
                var wrapper = JsonUtility.FromJson<GatewayEventWrapper>(rawJson);

                if (wrapper == null || string.IsNullOrEmpty(wrapper.type))
                    return null;

                var actionType = ResolveActionType(wrapper.type);

                return new AgentEvent(
                    actionType : actionType,
                    sessionId  : wrapper.session_id  ?? "",
                    taskName   : wrapper.task_name   ?? "",
                    subAgentId : wrapper.subagent_id ?? "",
                    rawPayload : rawJson
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventParser] JSON 파싱 실패: {ex.Message}\n{rawJson}");
                return null;
            }
        }

        public void RegisterRule(string eventTypeKey, AgentActionType actionType)
        {
            _rules[eventTypeKey] = actionType;
        }

        private AgentActionType ResolveActionType(string eventType)
        {
            var key = eventType.ToLowerInvariant().Trim();
            return _rules.TryGetValue(key, out var result)
                ? result
                : AgentActionType.Idle;
        }

        // OpenClaw Gateway 이벤트 구조 (JsonUtility용 직렬화 클래스)
        [Serializable]
        private class GatewayEventWrapper
        {
            public string type        = "";
            public string session_id  = "";
            public string task_name   = "";
            public string subagent_id = "";
            public string message     = "";
        }
    }
}
