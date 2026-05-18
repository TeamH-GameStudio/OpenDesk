using System;
using OpenDesk.Core.Persistence;
using UnityEngine;

namespace OpenDesk.Onboarding.Models
{
    /// <summary>
    /// 사용자 기본 프로필 영속 컨테이너.<br/>
    /// IGameDataService를 통해 로컬/서버 저장소에 영속화된다.<br/>
    /// 외부에서는 immutable <see cref="UserProfile"/>로 주고받고, 내부에 dirty 플래그를 관리한다.
    /// </summary>
    [TableName(PersistedDataTable.UserProfile)]
    public sealed class UserProfileData : IGameData
    {
        private const int CURRENT_VERSION = 1;

        private string _name = string.Empty;
        private Gender _gender = Gender.Undisclosed;
        private AgeBucket _age = AgeBucket.Undisclosed;
        private bool _hasValue;
        private bool _isDirty;

        public bool IsDirty => _isDirty;
        public bool HasValue => _hasValue;

        public void MarkAsDirty() => _isDirty = true;
        public void ResetDirty() => _isDirty = false;

        public void InitializeDefault()
        {
            _name = string.Empty;
            _gender = Gender.Undisclosed;
            _age = AgeBucket.Undisclosed;
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
                name = _name ?? string.Empty,
                gender = (int)_gender,
                age = (int)_age,
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
                Debug.LogError($"[UserProfileData] JSON 파싱 실패: {e.Message}");
                return;
            }

            if (snap == null) return;

            _hasValue = snap.hasValue;
            _name = snap.name ?? string.Empty;
            _gender = (Gender)snap.gender;
            _age = (AgeBucket)snap.age;
            // 로드된 데이터는 깨끗한 상태로 시작 — IsDirty 변경하지 않음.
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// 현재 영속 상태를 immutable record로 스냅샷 반환. 미설정 상태면 null.
        /// </summary>
        public UserProfile Snapshot()
        {
            return _hasValue ? new UserProfile(_name, _gender, _age) : null;
        }

        /// <summary>
        /// immutable record를 받아 내부 상태를 갱신하고 dirty 플래그를 세운다.
        /// </summary>
        public void Apply(UserProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            _name = profile.Name ?? string.Empty;
            _gender = profile.Gender;
            _age = profile.Age;
            _hasValue = true;
            _isDirty = true;
        }

        // ── JsonUtility-friendly DTO ──────────────────────────────

        [Serializable]
        private sealed class SerializedSnapshot
        {
            public int version;
            public bool hasValue;
            public string name;
            public int gender;
            public int age;
        }
    }
}
