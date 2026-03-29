using OpenDesk.AgentCreation.Models;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬 진입 시 PlayerPrefs에서 저장된 에이전트 데이터를 로드하여
    /// AgentSpawner로 자동 소환하는 부트스트래퍼.
    /// CreationScene → (PlayerPrefs) → OfficeScene 흐름.
    /// </summary>
    public class AgentOfficeBootstrapper : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;

        [Header("Model Prefabs (Resources 또는 직접 참조)")]
        [Tooltip("프리팹 이름 → GameObject 매핑. Inspector에서 등록.")]
        [SerializeField] private ModelPrefabEntry[] _modelPrefabs;

        [System.Serializable]
        public struct ModelPrefabEntry
        {
            public string PrefabName;
            public GameObject Prefab;
        }

        private void Start()
        {
            SpawnAllSavedAgents();
        }

        private void SpawnAllSavedAgents()
        {
            int count = AgentDataStore.Count;
            if (count == 0)
            {
                Debug.Log("[OfficeBootstrapper] 저장된 에이전트 없음");
                return;
            }

            Debug.Log($"[OfficeBootstrapper] 저장된 에이전트 {count}명 소환 시작");

            var allData = AgentDataStore.LoadAll();
            foreach (var saved in allData)
            {
                if (saved == null) continue;

                var prefab = FindPrefab(saved.ModelPrefabName);
                var creationData = saved.ToCreationData();
                creationData.AvatarPrefabName = saved.ModelPrefabName;

                var profile = AgentProfileSO.CreateFromData(creationData, prefab);

                var spawned = _spawner.SpawnAgent(profile);
                if (spawned != null)
                    Debug.Log($"[OfficeBootstrapper] 소환: {saved.AgentName} ({saved.ModelPrefabName})");
            }
        }

        private GameObject FindPrefab(string prefabName)
        {
            // Inspector 등록된 프리팹에서 검색
            if (_modelPrefabs != null)
            {
                foreach (var entry in _modelPrefabs)
                {
                    if (entry.PrefabName == prefabName && entry.Prefab != null)
                        return entry.Prefab;
                }
            }

            // fallback: Resources 로드
            var loaded = Resources.Load<GameObject>(prefabName);
            if (loaded != null) return loaded;

            Debug.LogWarning($"[OfficeBootstrapper] 프리팹 못 찾음: {prefabName}, 기본 프리팹 사용");
            return null;
        }

        /// <summary>디버그: 모든 에이전트 데이터 초기화 + 재소환</summary>
        [ContextMenu("Clear All & Respawn")]
        public void DebugClearAndRespawn()
        {
            _spawner.DespawnAll();
            AgentDataStore.ClearAll();
            Debug.Log("[OfficeBootstrapper] 전체 초기화 완료");
        }
    }
}
