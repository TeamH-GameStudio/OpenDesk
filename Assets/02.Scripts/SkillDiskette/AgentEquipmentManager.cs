using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Models;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.AgentCreation.Soul;
using OpenDesk.Core.Models;
using OpenDesk.Core.Models.Skills;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Skills;
using R3;
using UnityEngine;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 에이전트의 스킬 장착/해제 관리.
    /// - 슬롯 무제한 (장착된 스킬만 동적 리스트로 표시).
    /// - BuildSystemPrompt(): 기본 프로필 + Soul + 장착 스킬 promptContent 합성.
    /// - R3 이벤트로 UI/VFX에 장착 상태 전파.
    /// - SkillDescriptor 기반으로 통일. 레거시 SkillDiskette SO 입력은 어댑터로 변환되어 같은 흐름을 탄다.
    /// - IAgentSkillLoadoutService 바인딩 시 영속 상태와 양방향 동기화.
    ///
    /// AgentCharacterController 와 같은 GameObject 에 부착.
    /// </summary>
    public class AgentEquipmentManager : MonoBehaviour
    {
        [Header("현재 장착 (디버그)")]
        [SerializeField] private List<string> _equippedNames = new();

        // ── 내부 ──
        private readonly List<SkillDescriptor> _equipped = new();
        private readonly HashSet<string> _equippedIds = new();

        // ── R3 이벤트 ──
        private readonly Subject<SkillDescriptor> _onEquipped = new();
        private readonly Subject<SkillDescriptor> _onUnequipped = new();
        private readonly Subject<Unit> _onEquipmentChanged = new();

        public Observable<SkillDescriptor> OnEquipped => _onEquipped;
        public Observable<SkillDescriptor> OnUnequipped => _onUnequipped;
        public Observable<Unit> OnEquipmentChanged => _onEquipmentChanged;

        // ── 호환 레이어 (기존 SkillDiskette 구독자) ──
        private readonly Subject<SkillDiskette> _onEquippedDisk = new();
        private readonly Subject<SkillDiskette> _onUnequippedDisk = new();
        public Observable<SkillDiskette> OnEquippedDisk => _onEquippedDisk;
        public Observable<SkillDiskette> OnUnequippedDisk => _onUnequippedDisk;

        // ── 프로퍼티 ──
        public IReadOnlyList<SkillDescriptor> EquippedDescriptors => _equipped;
        /// <summary>레거시 호환: SkillDiskette 어댑터 캐시로 노출 (변경 시 새 리스트 생성)</summary>
        public IReadOnlyList<SkillDiskette> EquippedDisks => _legacyDiskCache;
        public int EquippedCount => _equipped.Count;
        public bool HasRoom => true;   // 슬롯 무제한

        // ── 에이전트 프로필 ──
        private string _agentId;
        private string _agentName;
        private string _agentRole;
        private string _agentTone;
        private AgentSoul _soul;
        private string _generatedSoulBlock;
        // 2026-05-14 — JSON-SSOT 도입. BindAgent 호출 시 record 보관 → traits 등 모든 raw 필드 접근.
        private AgentDraftRecord _record;

        // ── Loadout 서비스 바인딩 ──
        private IAgentSkillLoadoutService _loadoutService;
        private ISkillCatalogService _catalogService;
        private IDisposable _loadoutSubscription;

        // ── 레거시 호환 캐시 ──
        private readonly Dictionary<string, SkillDiskette> _disketteByIdCache = new();
        private List<SkillDiskette> _legacyDiskCache = new();

        // ══════════════════════════════════════════════
        //  프로필 설정
        // ══════════════════════════════════════════════

        /// <summary>
        /// JSON-SSOT 신규 진입점. AgentDraftRecord 를 직접 받아 traits 를 포함한
        /// 모든 raw 필드를 BuildSystemPrompt 에서 사용할 수 있게 한다.
        /// Soul 로드 + Loadout 재적용까지 일괄 수행.
        /// </summary>
        public void BindAgent(AgentDraftRecord record)
        {
            if (record == null) return;

            _record = record;
            _agentId = record.id ?? record.name;
            _agentName = record.name;
            _agentRole = record.role;
            _agentTone = record.tone;

            _generatedSoulBlock = SoulRepository.TryLoadAsBlock(_agentName);
            if (!string.IsNullOrEmpty(_generatedSoulBlock))
            {
                _soul = null;
            }
            // record 에 soulBlock 캐시가 있으면 우선 사용 (Haiku 호출 절약).
            else if (!string.IsNullOrEmpty(record.soulBlock))
            {
                _generatedSoulBlock = record.soulBlock;
                _soul = null;
            }
            else
            {
                _soul = null; // raw text role/tone 에는 enum 기반 Static Soul 매칭이 부정확하므로 비움.
            }

            ReloadFromLoadoutIfBound();
        }

        [Obsolete("Use BindAgent(AgentDraftRecord) instead. Kept for backwards compatibility — does not propagate traits.")]
        public void SetAgentProfile(string name, string role, string tone, string agentId = null)
        {
            _agentId = agentId ?? name;
            _agentName = name;
            _agentRole = role;
            _agentTone = tone;
            _generatedSoulBlock = SoulRepository.TryLoadAsBlock(name);
            ReloadFromLoadoutIfBound();
        }

        [Obsolete("Use BindAgent(AgentDraftRecord) instead. Kept for backwards compatibility — does not propagate traits.")]
        public void SetAgentProfile(string name, AgentRole role, AgentTone tone, string agentId = null)
        {
            _agentId = agentId ?? name;
            _agentName = name;
            _agentRole = RoleToKorean(role);
            _agentTone = ToneToKorean(tone);

            _generatedSoulBlock = SoulRepository.TryLoadAsBlock(name);
            if (!string.IsNullOrEmpty(_generatedSoulBlock))
            {
                _soul = null;
                Debug.Log($"[Equipment] Generated Soul 로드: {name} ({_generatedSoulBlock.Length}자)");
            }
            else
            {
                _soul = AgentSoul.LoadFor(role, tone);
                if (_soul != null)
                    Debug.Log($"[Equipment] Static Soul 로드: {_soul.name} ({role}/{tone})");
            }

            ReloadFromLoadoutIfBound();
        }

        public void SetSoul(AgentSoul soul)
        {
            _soul = soul;
            _generatedSoulBlock = null;
        }

        public AgentSoul Soul => _soul;
        public string AgentId => _agentId;

        // ══════════════════════════════════════════════
        //  Loadout 서비스 바인딩 (영속 상태 동기화)
        // ══════════════════════════════════════════════

        /// <summary>
        /// 영속 Loadout 서비스 + 카탈로그를 바인딩. 이후 장착/해제가 자동으로 영속화되고
        /// 외부 변경(다른 화면, 마이그레이션) 이 즉시 반영된다.
        /// </summary>
        public void BindLoadoutService(IAgentSkillLoadoutService loadout, ISkillCatalogService catalog, string agentId)
        {
            _loadoutSubscription?.Dispose();

            _loadoutService = loadout;
            _catalogService = catalog;
            _agentId = string.IsNullOrEmpty(agentId) ? _agentName : agentId;

            if (_loadoutService == null) return;

            _loadoutSubscription = _loadoutService.OnLoadoutChanged
                .Where(loadout =>
                    loadout != null &&
                    string.Equals(loadout.AgentId, _agentId, StringComparison.Ordinal))
                .Subscribe(loadout => ApplyLoadout(loadout));

            ReloadFromLoadoutIfBound();
        }

        public void UnbindLoadoutService()
        {
            _loadoutSubscription?.Dispose();
            _loadoutSubscription = null;
            _loadoutService = null;
            _catalogService = null;
        }

        // ══════════════════════════════════════════════
        //  장착 / 해제 (Descriptor 기준)
        // ══════════════════════════════════════════════

        public bool TryEquipDescriptor(SkillDescriptor descriptor, bool persist = true)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id)) return false;
            if (_equippedIds.Contains(descriptor.Id))
            {
                Debug.Log($"[Equipment] 이미 장착됨: {descriptor.DisplayName}");
                return false;
            }

            AddDescriptor(descriptor);

            if (persist && _loadoutService != null && !string.IsNullOrEmpty(_agentId))
                _loadoutService.EquipAsync(_agentId, descriptor.Id).Forget();

            return true;
        }

        public bool TryUnequip(string skillId, bool persist = true)
        {
            if (string.IsNullOrEmpty(skillId)) return false;
            var descriptor = _equipped.Find(d => d.Id == skillId);
            if (descriptor == null) return false;

            RemoveDescriptor(descriptor);

            if (persist && _loadoutService != null && !string.IsNullOrEmpty(_agentId))
                _loadoutService.UnequipAsync(_agentId, skillId).Forget();

            return true;
        }

        public void UnequipAll(bool persist = true)
        {
            var ids = _equipped.Select(d => d.Id).ToList();
            foreach (var id in ids) TryUnequip(id, persist);
        }

        // ══════════════════════════════════════════════
        //  레거시 SkillDiskette API (어댑터 위임)
        // ══════════════════════════════════════════════

        /// <summary>레거시 호환: SkillDiskette 입력을 SkillDescriptor 로 변환하여 장착.</summary>
        public bool TryEquip(SkillDiskette diskette)
        {
            if (diskette == null) return false;
            var descriptor = SkillDisketteAdapter.ToDescriptor(diskette);
            if (descriptor == null) return false;

            // 레거시 인스턴스 캐시 (선반 복귀용)
            if (!string.IsNullOrEmpty(descriptor.Id))
                _disketteByIdCache[descriptor.Id] = diskette;

            return TryEquipDescriptor(descriptor);
        }

        // ══════════════════════════════════════════════
        //  System Prompt 합성
        // ══════════════════════════════════════════════

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

            // 2. Personality (traits) — XML 태그로 격리해 모델이 무시할 수 없게.
            //    BindAgent 로 들어왔을 때만 채워짐. 옛 SetAgentProfile 경로는 _record 가 null 이라 자동 스킵.
            if (_record != null && _record.traits != null && _record.traits.Count > 0)
            {
                sb.AppendLine("<personality>");
                foreach (var trait in _record.traits)
                {
                    if (string.IsNullOrWhiteSpace(trait)) continue;
                    sb.AppendLine($"- {trait}");
                }
                sb.AppendLine("</personality>");
                sb.AppendLine();
            }

            // 3. Soul
            if (!string.IsNullOrEmpty(_generatedSoulBlock))
                sb.AppendLine(_generatedSoulBlock);
            else if (_soul != null)
                sb.AppendLine(_soul.ToSystemPromptBlock());

            // 4. 장착 스킬 인덱스 (Claude Code 의 SKILL.md 패턴)
            //    본문(promptContent) 은 시스템 프롬프트에 합성하지 않는다.
            //    LLM 이 description 으로 필요성을 판단하고 내장 도구 `read_skill_body`
            //    를 호출하여 디스크/메모리에서 본문을 지연 로드한다.
            if (_equipped.Count > 0)
            {
                sb.AppendLine("<available-skills>");
                sb.AppendLine("다음 스킬이 활성화되어 있습니다. 작업에 필요한 스킬의 상세 행동 지침은");
                sb.AppendLine("내장 도구 `read_skill_body(skill_id)` 를 호출하여 가져오세요.");
                foreach (var descriptor in _equipped)
                {
                    var desc = string.IsNullOrEmpty(descriptor.Description)
                        ? "(설명 없음)"
                        : descriptor.Description;
                    sb.AppendLine($"  - id: {descriptor.Id}");
                    sb.AppendLine($"    name: {descriptor.DisplayName}");
                    sb.AppendLine($"    description: {desc}");
                }
                sb.AppendLine("</available-skills>");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 미들웨어에 전달할 활성 스킬 페이로드. 본문은 클라이언트 메모리의 PromptContent
        /// 를 fallback 으로 함께 보낸다. 디스크 SKILL.md 가 있으면 미들웨어가 그쪽을 우선한다.
        /// </summary>
        public SkillLoadoutPayload BuildSkillLoadoutPayload()
        {
            var payload = new SkillLoadoutPayload { agentId = _agentId ?? string.Empty };
            foreach (var d in _equipped)
            {
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                payload.skills.Add(new SkillLoadoutEntry
                {
                    id = d.Id,
                    name = d.DisplayName ?? d.Id,
                    description = d.Description ?? string.Empty,
                    body = d.PromptContent ?? string.Empty,
                });
            }
            return payload;
        }

        // ══════════════════════════════════════════════
        //  외부 도구
        // ══════════════════════════════════════════════

        public List<string> GetAllRequiredTokens()
        {
            return _equipped
                .Where(d => d.RequiredTokens != null)
                .SelectMany(d => d.RequiredTokens)
                .Distinct()
                .ToList();
        }

        public bool HasExternalToolDisk()
        {
            return _equipped.Any(d => d.HasExternalTool);
        }

        public List<string> GetMcpCommands()
        {
            return _equipped
                .Where(d => d.HasExternalTool)
                .Select(d => d.McpServerCommand)
                .ToList();
        }

        // ══════════════════════════════════════════════
        //  내부
        // ══════════════════════════════════════════════

        private void AddDescriptor(SkillDescriptor descriptor)
        {
            _equipped.Add(descriptor);
            _equippedIds.Add(descriptor.Id);
            RefreshDebugAndLegacy();

            _onEquipped.OnNext(descriptor);
            if (_disketteByIdCache.TryGetValue(descriptor.Id, out var diskette))
                _onEquippedDisk.OnNext(diskette);
            _onEquipmentChanged.OnNext(Unit.Default);

            Debug.Log($"[Equipment] 장착: {descriptor.DisplayName} (총 {_equipped.Count})");
        }

        private void RemoveDescriptor(SkillDescriptor descriptor)
        {
            _equipped.Remove(descriptor);
            _equippedIds.Remove(descriptor.Id);
            RefreshDebugAndLegacy();

            _onUnequipped.OnNext(descriptor);
            if (_disketteByIdCache.TryGetValue(descriptor.Id, out var diskette))
                _onUnequippedDisk.OnNext(diskette);
            _onEquipmentChanged.OnNext(Unit.Default);

            Debug.Log($"[Equipment] 해제: {descriptor.DisplayName} (총 {_equipped.Count})");
        }

        private void ApplyLoadout(AgentSkillLoadout loadout)
        {
            if (loadout == null) return;
            if (_catalogService == null) return;

            var nextIds = new HashSet<string>(loadout.EquippedSkillIds ?? new List<string>());

            // 제거: 현재 장착 중인데 새 loadout 에 없는 것
            var toRemove = _equipped.Where(d => !nextIds.Contains(d.Id)).ToList();
            foreach (var descriptor in toRemove)
                RemoveDescriptor(descriptor);

            // 추가: loadout 에 있는데 현재 안 장착된 것
            foreach (var id in loadout.EquippedSkillIds ?? new List<string>())
            {
                if (_equippedIds.Contains(id)) continue;
                var descriptor = _catalogService.GetById(id);
                if (descriptor == null) continue;
                AddDescriptor(descriptor);
            }
        }

        private void ReloadFromLoadoutIfBound()
        {
            if (_loadoutService == null || _catalogService == null || string.IsNullOrEmpty(_agentId))
                return;
            var loadout = _loadoutService.GetLoadout(_agentId);
            ApplyLoadout(loadout);
        }

        private void RefreshDebugAndLegacy()
        {
            _equippedNames = _equipped.Select(d => d.DisplayName).ToList();
            _legacyDiskCache = _equipped
                .Select(d => _disketteByIdCache.TryGetValue(d.Id, out var disk) ? disk : null)
                .Where(d => d != null)
                .ToList();
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
            _loadoutSubscription?.Dispose();
            _onEquipped.Dispose();
            _onUnequipped.Dispose();
            _onEquipmentChanged.Dispose();
            _onEquippedDisk.Dispose();
            _onUnequippedDisk.Dispose();
        }
    }
}
