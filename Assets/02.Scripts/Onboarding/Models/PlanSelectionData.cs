using System;
using OpenDesk.Core.Persistence;
using UnityEngine;

namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 사용자 플랜 선택 영속 컨테이너.
    /// </summary>
    [TableName(PersistedDataTable.PlanSelection)]
    public sealed class PlanSelectionData : IGameData
    {
        private const int CURRENT_VERSION = 1;

        private PlanTier _tier = PlanTier.Free;
        private bool _hasValue;
        private bool _isDirty;

        public bool IsDirty => _isDirty;
        public bool HasValue => _hasValue;

        public void MarkAsDirty() => _isDirty = true;
        public void ResetDirty() => _isDirty = false;

        public void InitializeDefault()
        {
            _tier = PlanTier.Free;
            _hasValue = false;
            _isDirty = false;
        }

        public void ResetAllData() => InitializeDefault();

        public string ToJson()
        {
            var snap = new SerializedSnapshot
            {
                version = CURRENT_VERSION,
                hasValue = _hasValue,
                tier = (int)_tier,
            };
            return JsonUtility.ToJson(snap);
        }

        public void FromJson(string json)
        {
            InitializeDefault();
            if (string.IsNullOrEmpty(json)) return;

            SerializedSnapshot snap;
            try
            {
                snap = JsonUtility.FromJson<SerializedSnapshot>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlanSelectionData] JSON 파싱 실패: {e.Message}");
                return;
            }

            if (snap == null) return;

            _hasValue = snap.hasValue;
            _tier = (PlanTier)snap.tier;
        }

        // ── Public API ────────────────────────────────────────────

        public PlanSelection Snapshot()
        {
            return _hasValue ? new PlanSelection(_tier) : null;
        }

        public void Apply(PlanSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            _tier = selection.Tier;
            _hasValue = true;
            _isDirty = true;
        }

        // ── JsonUtility-friendly DTO ──────────────────────────────

        [Serializable]
        private sealed class SerializedSnapshot
        {
            public int version;
            public bool hasValue;
            public int tier;
        }
    }
}
