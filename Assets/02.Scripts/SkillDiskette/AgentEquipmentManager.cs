using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Services;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 에이전트의 디스켓 장착/해제 관리.
    /// - 슬롯 기반 장착 (최대 MaxSlots)
    /// - BuildSystemPrompt(): 기본 프로필 + 장착 디스켓 promptContent 합성
    /// - R3 이벤트로 UI/VFX에 장착 상태 전파
    ///
    /// AgentCharacterController와 같은 GameObject에 부착.
    /// </summary>
    public class AgentEquipmentManager : MonoBehaviour
    {
        [Header("슬롯 설정")]
        [SerializeField] private int _maxSlots = 3;

        [Header("현재 장착 (디버그)")]
        [SerializeField] private List<string> _equippedNames = new();

        // ── 내부 ──
        private readonly List<SkillDiskette> _equippedDisks = new();

        // ── R3 이벤트 ──
        private readonly Subject<SkillDiskette> _onEquipped = new();
        private readonly Subject<SkillDiskette> _onUnequipped = new();
        private readonly Subject<Unit> _onEquipmentChanged = new();

        public Observable<SkillDiskette> OnEquipped => _onEquipped;
        public Observable<SkillDiskette> OnUnequipped => _onUnequipped;
        public Observable<Unit> OnEquipmentChanged => _onEquipmentChanged;

        // ── 프로퍼티 ──
        public IReadOnlyList<SkillDiskette> EquippedDisks => _equippedDisks;
        public int MaxSlots => _maxSlots;
        public int RemainingSlots => _maxSlots - _equippedDisks.Count;
        public bool HasRoom => RemainingSlots > 0;

        // ── 에이전트 프로필 ──
        private string _agentName;
        private string _agentRole;
        private string _agentTone;
        private AgentSoul _soul;

        /// <summary>에이전트 기본 프로필 설정 (위저드 데이터에서)</summary>
        public void SetAgentProfile(string name, string role, string tone)
        {
            _agentName = name;
            _agentRole = role;
            _agentTone = tone;
        }

        /// <summary>에이전트 프로필 + Soul 자동 로드</summary>
        public void SetAgentProfile(string name, AgentRole role, AgentTone tone)
        {
            _agentName = name;
            _agentRole = RoleToKorean(role);
            _agentTone = ToneToKorean(tone);
            _soul = AgentSoul.LoadFor(role, tone);

            if (_soul != null)
                Debug.Log($"[Equipment] Soul 로드: {_soul.name} ({role}/{tone})");
        }

        /// <summary>Soul을 직접 설정 (커스텀 Soul 사용 시)</summary>
        public void SetSoul(AgentSoul soul) => _soul = soul;

        /// <summary>현재 로드된 Soul</summary>
        public AgentSoul Soul => _soul;

        // ══════════════════════════════════════════════
        //  장착 / 해제
        // ══════════════════════════════════════════════

        /// <summary>디스켓 장착. 성공 시 true.</summary>
        public bool TryEquip(SkillDiskette diskette)
        {
            if (diskette == null) return false;
            if (!HasRoom)
            {
                Debug.Log($"[Equipment] 슬롯 부족 ({_equippedDisks.Count}/{_maxSlots})");
                return false;
            }
            if (_equippedDisks.Exists(d => d.SkillId == diskette.SkillId))
            {
                Debug.Log($"[Equipment] 이미 장착됨: {diskette.DisplayName}");
                return false;
            }

            _equippedDisks.Add(diskette);
            RefreshDebugNames();

            _onEquipped.OnNext(diskette);
            _onEquipmentChanged.OnNext(Unit.Default);

            Debug.Log($"[Equipment] 장착: {diskette.DisplayName} ({_equippedDisks.Count}/{_maxSlots})");
            return true;
        }

        /// <summary>디스켓 해제. 성공 시 true.</summary>
        public bool TryUnequip(string skillId)
        {
            var disk = _equippedDisks.Find(d => d.SkillId == skillId);
            if (disk == null)
            {
                Debug.Log($"[Equipment] 해제 대상 없음: {skillId}");
                return false;
            }

            _equippedDisks.Remove(disk);
            RefreshDebugNames();

            _onUnequipped.OnNext(disk);
            _onEquipmentChanged.OnNext(Unit.Default);

            Debug.Log($"[Equipment] 해제: {disk.DisplayName} ({_equippedDisks.Count}/{_maxSlots})");
            return true;
        }

        /// <summary>모든 디스켓 해제</summary>
        public void UnequipAll()
        {
            while (_equippedDisks.Count > 0)
                TryUnequip(_equippedDisks[0].SkillId);
        }

        // ══════════════════════════════════════════════
        //  System Prompt 합성
        // ══════════════════════════════════════════════

        /// <summary>
        /// 기본 프로필 + Soul + 장착 디스켓 promptContent를 합성한 system prompt 반환.
        /// </summary>
        public string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            // 1. 기본 프로필
            if (!string.IsNullOrEmpty(_agentName))
            {
                sb.AppendLine($"당신은 '{_agentName}'이라는 이름의 AI 에이전트입니다.");
                if (!string.IsNullOrEmpty(_agentRole))
                    sb.AppendLine($"전문 분야: {_agentRole}");
                if (!string.IsNullOrEmpty(_agentTone))
                    sb.AppendLine($"어조: {_agentTone}");
                sb.AppendLine("한국어로 대화합니다.");
                sb.AppendLine();
            }

            // 2. Soul — 성격, 행동 원칙, 전문성 등 깊은 인격 레이어
            if (_soul != null)
            {
                sb.AppendLine(_soul.ToSystemPromptBlock());
            }

            // 3. 장착 디스켓 주입
            if (_equippedDisks.Count > 0)
            {
                sb.AppendLine("다음 스킬이 장착되어 있습니다. 각 스킬의 지시사항을 따르세요:");
                sb.AppendLine();

                foreach (var disk in _equippedDisks)
                {
                    sb.AppendLine($"<skill name=\"{disk.DisplayName}\">");
                    sb.AppendLine(disk.PromptContent);
                    sb.AppendLine("</skill>");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        // ══════════════════════════════════════════════
        //  외부 도구 관련
        // ══════════════════════════════════════════════

        /// <summary>장착된 디스켓들이 요구하는 모든 토큰 키 목록</summary>
        public List<string> GetAllRequiredTokens()
        {
            return _equippedDisks
                .Where(d => d.RequiredTokens != null)
                .SelectMany(d => d.RequiredTokens)
                .Distinct()
                .ToList();
        }

        /// <summary>외부 도구 디스켓이 있는지</summary>
        public bool HasExternalToolDisk()
        {
            return _equippedDisks.Any(d => d.HasExternalTool);
        }

        /// <summary>MCP 서버 명령어 목록</summary>
        public List<string> GetMcpCommands()
        {
            return _equippedDisks
                .Where(d => d.HasExternalTool)
                .Select(d => d.McpServerCommand)
                .ToList();
        }

        // ── 내부 ──

        private void RefreshDebugNames()
        {
            _equippedNames = _equippedDisks.Select(d => d.DisplayName).ToList();
        }

        private static string RoleToKorean(AgentRole role) => role switch
        {
            AgentRole.Planning    => "기획",
            AgentRole.Development => "개발",
            AgentRole.Design      => "디자인",
            AgentRole.Legal       => "법률",
            AgentRole.Marketing   => "마케팅",
            AgentRole.Research    => "리서치",
            AgentRole.Support     => "고객지원",
            AgentRole.Finance     => "재무",
            _                     => "에이전트",
        };

        private static string ToneToKorean(AgentTone tone) => tone switch
        {
            AgentTone.Friendly => "친절한",
            AgentTone.Logical  => "논리적인",
            AgentTone.Humorous => "유머러스한",
            AgentTone.Formal   => "격식체",
            AgentTone.Casual   => "편안한",
            _                  => "",
        };

        private void OnDestroy()
        {
            _onEquipped.Dispose();
            _onUnequipped.Dispose();
            _onEquipmentChanged.Dispose();
        }
    }
}
