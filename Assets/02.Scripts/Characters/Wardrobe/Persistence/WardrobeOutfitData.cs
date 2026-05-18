using System;
using System.Collections.Generic;
using OpenDesk.Core.Persistence;
using UnityEngine;

namespace OpenDesk.Characters.Wardrobe.Persistence
{
    /// <summary>
    /// 에이전트별 아웃핏 저장소.<br/>
    /// IGameDataService를 통해 로컬/서버 저장소에 영속화된다.<br/>
    /// 데이터 구조: agentId → <see cref="WardrobeOutfit"/> 매핑 + agentId가 없는 캐릭터에 적용할 fallback outfit.
    ///
    /// 사용 예:
    /// <code>
    /// var data = _gameDataService.GetData&lt;WardrobeOutfitData&gt;();
    /// data.Set("agent_abc123", outfit);                // 마지막에 dirty=true → 다음 저장 사이클에 영속화
    /// await _gameDataService.SaveData&lt;WardrobeOutfitData&gt;();
    /// </code>
    /// </summary>
    public sealed class WardrobeOutfitData : IGameData
    {
        private const int CURRENT_VERSION = 1;

        private readonly Dictionary<string, WardrobeOutfit> _outfits = new();
        private WardrobeOutfit _fallback = new();
        private bool _isDirty;

        public bool IsDirty => _isDirty;

        public void MarkAsDirty() => _isDirty = true;
        public void ResetDirty() => _isDirty = false;

        public void InitializeDefault()
        {
            _outfits.Clear();
            _fallback = new WardrobeOutfit();
            _isDirty = false;
        }

        public void ResetAllData()
        {
            _outfits.Clear();
            _fallback = new WardrobeOutfit();
            _isDirty = false;
        }

        public string ToJson()
        {
            var snap = new SerializedSnapshot
            {
                version = CURRENT_VERSION,
                fallback = _fallback ?? new WardrobeOutfit(),
                entries = new List<SerializedEntry>(_outfits.Count),
            };

            foreach (var kvp in _outfits)
            {
                snap.entries.Add(new SerializedEntry
                {
                    agentId = kvp.Key,
                    outfit = kvp.Value ?? new WardrobeOutfit(),
                });
            }

            return JsonUtility.ToJson(snap);
        }

        public void FromJson(string json)
        {
            _outfits.Clear();
            _fallback = new WardrobeOutfit();

            if (string.IsNullOrEmpty(json)) return;

            SerializedSnapshot snap;
            try
            {
                snap = JsonUtility.FromJson<SerializedSnapshot>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WardrobeOutfitData] JSON 파싱 실패: {e.Message}");
                return;
            }

            if (snap == null) return;

            _fallback = snap.fallback ?? new WardrobeOutfit();

            if (snap.entries != null)
            {
                foreach (var entry in snap.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.agentId)) continue;
                    _outfits[entry.agentId] = entry.outfit ?? new WardrobeOutfit();
                }
            }

            // 로드된 데이터는 깨끗한 상태로 시작 — IsDirty 변경하지 않음.
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>모든 슬롯이 비어있는 fallback outfit. agentId가 없는 컨텍스트에서 사용.</summary>
        public WardrobeOutfit Fallback
        {
            get => _fallback;
            set
            {
                _fallback = value ?? new WardrobeOutfit();
                _isDirty = true;
            }
        }

        /// <summary>저장된 모든 (agentId, outfit) 쌍. 읽기 전용.</summary>
        public IReadOnlyDictionary<string, WardrobeOutfit> All => _outfits;

        public bool TryGet(string agentId, out WardrobeOutfit outfit)
        {
            outfit = null;
            if (string.IsNullOrEmpty(agentId)) return false;
            return _outfits.TryGetValue(agentId, out outfit);
        }

        /// <summary>
        /// agentId에 해당하는 outfit을 반환. 없으면 <see cref="Fallback"/>의 사본을 반환한다.
        /// </summary>
        public WardrobeOutfit GetOrFallback(string agentId)
        {
            if (!string.IsNullOrEmpty(agentId) && _outfits.TryGetValue(agentId, out var found))
                return found;
            return _fallback.Clone();
        }

        public void Set(string agentId, WardrobeOutfit outfit)
        {
            if (string.IsNullOrEmpty(agentId))
                throw new ArgumentException("agentId가 비어있습니다.", nameof(agentId));
            if (outfit == null)
                throw new ArgumentNullException(nameof(outfit));

            _outfits[agentId] = outfit;
            _isDirty = true;
        }

        /// <summary>
        /// 단일 슬롯만 갱신하는 편의 메서드. 기존 outfit이 없으면 fallback 기반으로 시작한다.
        /// </summary>
        public void SetSlot(string agentId, AgentCreationTest.Models.WardrobePart part, string optionId)
        {
            if (string.IsNullOrEmpty(agentId))
                throw new ArgumentException("agentId가 비어있습니다.", nameof(agentId));

            var current = _outfits.TryGetValue(agentId, out var existing)
                ? existing
                : _fallback.Clone();

            _outfits[agentId] = current.With(part, optionId);
            _isDirty = true;
        }

        public bool Remove(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;
            var removed = _outfits.Remove(agentId);
            if (removed) _isDirty = true;
            return removed;
        }

        public void Clear()
        {
            if (_outfits.Count == 0) return;
            _outfits.Clear();
            _isDirty = true;
        }

        // ── JsonUtility-friendly DTOs ────────────────────────────

        [Serializable]
        private sealed class SerializedSnapshot
        {
            public int version;
            public WardrobeOutfit fallback;
            public List<SerializedEntry> entries;
        }

        [Serializable]
        private sealed class SerializedEntry
        {
            public string agentId;
            public WardrobeOutfit outfit;
        }
    }
}
