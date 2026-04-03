using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 디스켓 3D 오브젝트 생성 팩토리.
    /// - 프리셋 디스켓: Resources/SkillDisks/ 에서 로드
    /// - 커스텀 디스켓: CraftResult → 런타임 SO 생성 → 3D 오브젝트
    /// </summary>
    public class SkillDisketteFactory
    {
        private GameObject _diskettePrefab;
        private Dictionary<string, SkillDiskette> _presetCache;

        public void Initialize(GameObject diskettePrefab)
        {
            _diskettePrefab = diskettePrefab;
            LoadPresets();
        }

        private void LoadPresets()
        {
            _presetCache = new Dictionary<string, SkillDiskette>();
            var presets = Resources.LoadAll<SkillDiskette>("SkillDisks");
            foreach (var p in presets)
            {
                if (string.IsNullOrEmpty(p.SkillId)) continue;
                _presetCache[p.SkillId] = p;
            }
            Debug.Log($"[DisketteFactory] 프리셋 {_presetCache.Count}개 로드 완료");
        }

        public List<SkillDiskette> GetAllPresets()
            => _presetCache?.Values.ToList() ?? new List<SkillDiskette>();

        /// <summary>크래프팅 결과로 3D 디스켓 생성</summary>
        public GameObject CreateFromCraft(Models.CraftResult result, Vector3 spawnPosition)
        {
            var so = SkillDiskette.CreateRuntime(
                skillId: $"custom-{System.Guid.NewGuid().ToString("N")[..12]}",
                displayName: result.skillName,
                description: result.description,
                category: result.ParseCategory(),
                promptContent: result.promptContent,
                isCustomCrafted: true,
                craftPrompt: result.skillName
            );
            return SpawnObject(so, spawnPosition);
        }

        /// <summary>프리셋 디스켓 3D 오브젝트 생성</summary>
        public GameObject CreateFromPreset(string skillId, Vector3 spawnPosition)
        {
            if (_presetCache == null || !_presetCache.TryGetValue(skillId, out var so))
            {
                Debug.LogWarning($"[DisketteFactory] 프리셋 없음: {skillId}");
                return null;
            }
            return SpawnObject(so, spawnPosition);
        }

        private GameObject SpawnObject(SkillDiskette so, Vector3 position)
        {
            if (_diskettePrefab == null)
            {
                Debug.LogError("[DisketteFactory] 디스켓 프리팹 미설정");
                return null;
            }

            var go = Object.Instantiate(_diskettePrefab, position, Quaternion.identity);
            go.name = $"Diskette_{so.DisplayName}";

            var view = go.GetComponent<SkillDisketteView>();
            if (view == null)
                view = go.AddComponent<SkillDisketteView>();

            view.Initialize(so);
            return go;
        }
    }
}
