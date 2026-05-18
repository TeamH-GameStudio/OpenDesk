using System;
using OpenDesk.AgentCreation.Persistence;
using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// AgentDraftRecord (JSON 영속 모델) → 런타임 AgentProfileSO 변환.
    /// AgentSpawner.SpawnAgent 의 시그니처를 바꾸지 않기 위한 어댑터.
    ///
    /// - SessionId 는 record.id 를 그대로 사용 (JSON 파일명과 일치 → 클릭/세션 매핑 단순)
    /// - role 한글 라벨 / modelId 문자열 → enum 매핑 (알 수 없는 값은 None)
    /// </summary>
    public static class AgentProfileFactory
    {
        public static AgentProfileSO FromRecord(AgentDraftRecord record, GameObject mannequinPrefab)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var role = ParseRole(record.role);
            var aiModel = ParseAiModel(record.modelId);

            var so = ScriptableObject.CreateInstance<AgentProfileSO>();
            so.name = $"AgentProfile_{record.name}";

            // AgentProfileSO 의 필드는 SerializeField private — record 매핑은 reflection 으로.
            // (CreateFromData 가 private 필드에 직접 접근하는 동일한 패턴.)
            ApplyPrivateFields(so, record, mannequinPrefab, role, aiModel);
            return so;
        }

        private static void ApplyPrivateFields(
            AgentProfileSO so,
            AgentDraftRecord record,
            GameObject mannequinPrefab,
            AgentRole role,
            AgentAIModel aiModel)
        {
            var t = typeof(AgentProfileSO);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

            t.GetField("_agentName", F)?.SetValue(so, record.name ?? string.Empty);
            t.GetField("_role", F)?.SetValue(so, role);
            t.GetField("_aiModel", F)?.SetValue(so, aiModel);
            t.GetField("_tone", F)?.SetValue(so, ParseTone(record.tone));
            t.GetField("_modelPrefab", F)?.SetValue(so, mannequinPrefab);
            t.GetField("_hudColor", F)?.SetValue(so, AgentProfileSO.GetDefaultHudColor(role));
            t.GetField("_sessionId", F)?.SetValue(so, record.id ?? string.Empty);
            // Source 도 SerializeField 가 아닌 private 필드라 동일한 reflection 경로로 주입.
            t.GetField("_source", F)?.SetValue(so, record);
        }

        // ───────────────────────────────────────────────────────
        //  Enum 매핑
        // ───────────────────────────────────────────────────────

        // 새 위저드는 역할을 자유 텍스트로 받음. 자주 쓰는 한글 라벨만 enum 으로 매핑하고
        // 그 외에는 Planning 으로 폴백 — AgentSpawner.IsValid 체크에서 None 거부되어
        // 스폰 자체가 막히는 것을 방지. HUD 컬러는 fallback enum 의 기본값이 된다.
        private static AgentRole ParseRole(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return AgentRole.Planning;

            switch (raw.Trim())
            {
                case "기획":   case "Planning":    return AgentRole.Planning;
                case "개발":   case "Development": return AgentRole.Development;
                case "디자인": case "Design":      return AgentRole.Design;
                case "법률":   case "Legal":       return AgentRole.Legal;
                case "마케팅": case "Marketing":   return AgentRole.Marketing;
                case "리서치": case "Research":    return AgentRole.Research;
                case "고객지원": case "지원": case "Support": return AgentRole.Support;
                case "재무":   case "Finance":     return AgentRole.Finance;
                default:                           return AgentRole.Planning; // 자유 텍스트 폴백
            }
        }

        // record.tone 은 enum 이름("Friendly") 또는 한글 라벨("친절한") 양쪽 모두 허용.
        // 위저드가 아직 tone 을 직접 받지 않으므로 보통 null/empty → None 폴백.
        // BindAgent 시 raw 문자열은 record.tone 그대로 system prompt 에 들어가므로
        // enum 매핑은 HUD/표시 보조용으로만 쓰인다.
        private static AgentTone ParseTone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return AgentTone.None;

            switch (raw.Trim())
            {
                case "친절한":   case "Friendly":  return AgentTone.Friendly;
                case "논리적인": case "Logical":   return AgentTone.Logical;
                case "유머러스한": case "Humorous": return AgentTone.Humorous;
                case "격식체":   case "Formal":    return AgentTone.Formal;
                case "편안한":   case "Casual":    return AgentTone.Casual;
                default:                            return AgentTone.None;
            }
        }

        // ModelOptions 의 Id 형식: "claude-sonnet-4-6", "claude-opus-4-7", "claude-haiku-4-5".
        // 현재 enum 은 ClaudeSonnet 단일이라 모든 Claude 변형을 그쪽으로 매핑.
        // 미지정 / 알 수 없는 ID 는 ClaudeSonnet 으로 폴백 — IsValid 체크 통과 보장.
        private static AgentAIModel ParseAiModel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return AgentAIModel.ClaudeSonnet;

            if (raw.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                return AgentAIModel.ClaudeSonnet;
            if (raw.StartsWith("gpt", StringComparison.OrdinalIgnoreCase))
                return AgentAIModel.GPT4o;
            if (raw.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                return AgentAIModel.GeminiPro;
            return AgentAIModel.ClaudeSonnet; // 자유 텍스트 폴백
        }
    }
}
