using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenDesk.Core.Models;
using OpenDesk.Core.Services;
using UnityEngine;

namespace OpenDesk.Core.Implementations
{
    /// <summary>
    /// OpenClaw Gateway에서 오는 Raw JSON을 AgentEvent로 변환
    /// Stateless — Transient으로 등록
    ///
    /// Gateway 메시지 유형:
    /// 1. {"type":"event","event":"chat","payload":{...}}   → AI 채팅 응답 (delta/final)
    /// 2. {"type":"event","event":"agent","payload":{...}}  → 에이전트 라이프사이클
    /// 3. {"type":"event","event":"connect.challenge",...}   → 핸드셰이크 (무시)
    /// 4. {"type":"res","ok":true,...}                       → RPC 응답 (무시)
    /// 5. 기존 플랫 포맷 {"type":"task_started",...}         → 레거시 호환
    /// </summary>
    public class EventParserService : IEventParserService
    {
        // JSON eventType 키 → ActionType 매핑 테이블 (레거시 플랫 포맷용)
        private readonly Dictionary<string, AgentActionType> _rules = new()
        {
            { "task_started",        AgentActionType.TaskStarted        },
            { "task_completed",      AgentActionType.TaskCompleted      },
            { "task_failed",         AgentActionType.TaskFailed         },
            { "thinking",            AgentActionType.Thinking           },

            // 에이전틱 루프 세부 단계
            { "planning",            AgentActionType.Planning           },
            { "executing",           AgentActionType.Executing          },
            { "reviewing",           AgentActionType.Reviewing          },

            // 도구 사용
            { "tool_call",           AgentActionType.ToolUsing          },
            { "tool_result",         AgentActionType.ToolResult         },

            // 서브 에이전트
            { "subagent_spawned",    AgentActionType.SubAgentSpawned    },
            { "subagent_completed",  AgentActionType.SubAgentCompleted  },
            { "subagent_failed",     AgentActionType.SubAgentFailed     },

            // 연결
            { "connected",           AgentActionType.Connected          },
            { "disconnected",        AgentActionType.Disconnected       },
        };

        // 정규식: JSON 문자열 값 추출 (이스케이프 문자 포함)
        private static readonly Regex _reEventField    = new("\"event\"\\s*:\\s*\"([^\"]+)\"",      RegexOptions.Compiled);
        private static readonly Regex _reStateField    = new("\"state\"\\s*:\\s*\"([^\"]+)\"",      RegexOptions.Compiled);
        private static readonly Regex _reRunIdField    = new("\"runId\"\\s*:\\s*\"([^\"]+)\"",      RegexOptions.Compiled);
        private static readonly Regex _reSessionKey    = new("\"sessionKey\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex _reStreamField   = new("\"stream\"\\s*:\\s*\"([^\"]+)\"",     RegexOptions.Compiled);

        public AgentEvent? Parse(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            try
            {
                // 1) Gateway 이벤트 포맷: {"type":"event","event":"chat|agent|..."}
                if (rawJson.Contains("\"type\":\"event\"") && rawJson.Contains("\"event\":"))
                    return ParseGatewayEvent(rawJson);

                // 2) RPC 응답: {"type":"res",...} → 무시 (hello-ok, health 응답 등)
                if (rawJson.Contains("\"type\":\"res\""))
                    return null;

                // 3) 레거시 플랫 포맷 폴백
                return ParseLegacyEvent(rawJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventParser] JSON 파싱 실패: {ex.Message}\n{(rawJson.Length > 300 ? rawJson[..300] + "..." : rawJson)}");
                return null;
            }
        }

        public void RegisterRule(string eventTypeKey, AgentActionType actionType)
        {
            _rules[eventTypeKey] = actionType;
        }

        // ── Gateway 이벤트 파싱 ─────────────────────────────────────────

        private AgentEvent? ParseGatewayEvent(string json)
        {
            var eventName = ExtractRegex(_reEventField, json);
            if (string.IsNullOrEmpty(eventName))
                return null;

            switch (eventName)
            {
                case "chat":
                    return ParseChatEvent(json);

                case "agent":
                    return ParseAgentLifecycleEvent(json);

                case "connect.challenge":
                    // 핸드셰이크 challenge — Bridge에서 처리, 여기선 무시
                    return null;

                default:
                    Debug.Log($"[EventParser] 알 수 없는 Gateway 이벤트: {eventName}");
                    return null;
            }
        }

        /// <summary>
        /// chat 이벤트 파싱
        /// 포맷: {"type":"event","event":"chat","payload":{
        ///   "runId":"...", "sessionKey":"default", "seq":0,
        ///   "state":"delta"|"final",
        ///   "message":{"role":"assistant","content":[{"type":"text","text":"응답 텍스트"}]}
        /// }}
        /// </summary>
        private AgentEvent? ParseChatEvent(string json)
        {
            var state      = ExtractRegex(_reStateField, json);
            var runId      = ExtractRegex(_reRunIdField, json);
            var sessionKey = ExtractRegex(_reSessionKey, json);

            // content[].text 추출 — "text":"..." 패턴에서 마지막 매치가 실제 응답
            var textContent = ExtractChatTextContent(json);

            if (string.IsNullOrEmpty(textContent) && state != "final")
            {
                // delta인데 텍스트 없으면 무시 (빈 delta 가능)
                return null;
            }

            var actionType = state == "final"
                ? AgentActionType.ChatFinal
                : AgentActionType.ChatDelta;

            return new AgentEvent(
                actionType : actionType,
                sessionId  : sessionKey ?? "default",
                message    : textContent ?? "",
                runId      : runId ?? "",
                rawPayload : json
            );
        }

        /// <summary>
        /// agent 라이프사이클 이벤트 파싱
        /// 포맷: {"type":"event","event":"agent","payload":{
        ///   "stream":"lifecycle", "state":"thinking"|"executing"|...,
        ///   "runId":"...", ...
        /// }}
        /// </summary>
        private AgentEvent? ParseAgentLifecycleEvent(string json)
        {
            var state      = ExtractRegex(_reStateField, json);
            var stream     = ExtractRegex(_reStreamField, json);
            var runId      = ExtractRegex(_reRunIdField, json);
            var sessionKey = ExtractRegex(_reSessionKey, json);

            // state 값으로 기존 룰 매핑 시도
            AgentActionType actionType;
            if (!string.IsNullOrEmpty(state) && _rules.TryGetValue(state.ToLowerInvariant(), out var mapped))
                actionType = mapped;
            else
                actionType = AgentActionType.AgentLifecycle;

            return new AgentEvent(
                actionType : actionType,
                sessionId  : sessionKey ?? "default",
                taskName   : state ?? "",
                runId      : runId ?? "",
                rawPayload : json
            );
        }

        // ── 레거시 플랫 포맷 ────────────────────────────────────────────

        private AgentEvent? ParseLegacyEvent(string json)
        {
            var wrapper = JsonUtility.FromJson<GatewayLegacyWrapper>(json);

            if (wrapper == null || string.IsNullOrEmpty(wrapper.type))
                return null;

            var actionType = ResolveActionType(wrapper.type);

            return new AgentEvent(
                actionType : actionType,
                sessionId  : wrapper.session_id  ?? "",
                taskName   : wrapper.task_name   ?? "",
                subAgentId : wrapper.subagent_id ?? "",
                rawPayload : json
            );
        }

        private AgentActionType ResolveActionType(string eventType)
        {
            var key = eventType.ToLowerInvariant().Trim();
            return _rules.TryGetValue(key, out var result)
                ? result
                : AgentActionType.Idle;
        }

        // ── 텍스트 추출 유틸리티 ────────────────────────────────────────

        /// <summary>
        /// chat 이벤트의 payload.message.content[].text 추출
        /// JsonUtility는 깊은 중첩 + 배열 파싱이 어려우므로 문자열 탐색으로 처리
        ///
        /// 전략: "content":[...] 블록 내부에서 "text":"..." 값을 추출
        /// </summary>
        private static string ExtractChatTextContent(string json)
        {
            // "content":[ 이후의 영역에서 "text":"값" 추출
            var contentIdx = json.IndexOf("\"content\"", StringComparison.Ordinal);
            if (contentIdx < 0) return null;

            // content 배열 시작 이후에서 "text" 필드 찾기
            var searchArea = json.Substring(contentIdx);

            // "type":"text" 를 포함하는 content 항목에서 "text":"실제값" 추출
            // 패턴: ..."type":"text"..."text":"실제 응답 텍스트"...
            var textTypeIdx = searchArea.IndexOf("\"type\":\"text\"", StringComparison.Ordinal);
            if (textTypeIdx < 0) return null;

            // "type":"text" 이후에서 "text":"값" 찾기
            var afterTypeText = searchArea.Substring(textTypeIdx);
            var textFieldIdx = afterTypeText.IndexOf("\"text\":\"", StringComparison.Ordinal);
            if (textFieldIdx < 0) return null;

            // "text":" 이후의 값 추출 (이스케이프된 따옴표 처리)
            var valueStart = textFieldIdx + "\"text\":\"".Length;
            return ExtractJsonStringValue(afterTypeText, valueStart);
        }

        /// <summary>
        /// JSON 문자열 값 추출 (이스케이프 문자 처리: \", \\, \n, \t 등)
        /// startIdx는 여는 따옴표 바로 다음 위치
        /// </summary>
        private static string ExtractJsonStringValue(string json, int startIdx)
        {
            if (startIdx >= json.Length) return null;

            var sb = new System.Text.StringBuilder(256);
            var i = startIdx;

            while (i < json.Length)
            {
                var c = json[i];

                if (c == '\\' && i + 1 < json.Length)
                {
                    var next = json[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'r':  sb.Append('\r'); break;
                        case '/':  sb.Append('/');  break;
                        default:   sb.Append('\\'); sb.Append(next); break;
                    }
                    i += 2;
                    continue;
                }

                if (c == '"')
                    break; // 닫는 따옴표 — 값 끝

                sb.Append(c);
                i++;
            }

            var result = sb.ToString();
            return string.IsNullOrEmpty(result) ? null : result;
        }

        private static string ExtractRegex(Regex regex, string input)
        {
            var match = regex.Match(input);
            return match.Success ? match.Groups[1].Value : null;
        }

        // ── 레거시 직렬화 클래스 ────────────────────────────────────────

        [Serializable]
        private class GatewayLegacyWrapper
        {
            public string type        = "";
            public string session_id  = "";
            public string task_name   = "";
            public string subagent_id = "";
            public string message     = "";
        }
    }
}
