using OpenDesk.AgentCreation.Models;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬 진입 시 기존 에이전트 전부 삭제 후
    /// 최신 에이전트 1명만 소환하는 부트스트래퍼.
    /// 항상 최신 데이터로 갱신 — 단일 에이전트 정책.
    /// </summary>
    public class AgentOfficeBootstrapper : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;

        [Header("Model Prefabs (Inspector 등록)")]
        [SerializeField] private ModelPrefabEntry[] _modelPrefabs;

        [System.Serializable]
        public struct ModelPrefabEntry
        {
            public string PrefabName;
            public GameObject Prefab;
        }

        /// <summary>현재 소환된 에이전트 정보</summary>
        public AgentSpawner.SpawnedAgent CurrentAgent { get; private set; }

        private void Start()
        {
            SpawnLatestAgent();
        }

        /// <summary>기존 전부 삭제 → 최신 에이전트 1명만 소환</summary>
        private void SpawnLatestAgent()
        {
            // 1) 기존 소환된 에이전트 전부 제거
            _spawner.DespawnAll();

            // 2) 저장된 에이전트 중 가장 마지막(최신) 1개만 사용
            int count = AgentDataStore.Count;
            if (count == 0)
            {
                Debug.Log("[OfficeBootstrapper] 저장된 에이전트 없음");
                return;
            }

            var latest = AgentDataStore.Load(count - 1);
            if (latest == null)
            {
                Debug.LogWarning("[OfficeBootstrapper] 최신 에이전트 로드 실패");
                return;
            }

            // 3) 에이전트 데이터 정리 (세션/채팅 기록은 보존)
            CleanupOldAgentData(count, latest);

            // 4) 소환
            var prefab = FindPrefab(latest.ModelPrefabName);
            var creationData = latest.ToCreationData();
            creationData.AvatarPrefabName = latest.ModelPrefabName;

            var profile = AgentProfileSO.CreateFromData(creationData, prefab);
            CurrentAgent = _spawner.SpawnAgent(profile);

            if (CurrentAgent != null)
                Debug.Log($"[OfficeBootstrapper] 소환 완료: {latest.AgentName}");
            else
                Debug.LogWarning($"[OfficeBootstrapper] 소환 실패: {latest.AgentName}");
        }

        /// <summary>에이전트 데이터만 정리 (세션/채팅 기록은 보존)</summary>
        private static void CleanupOldAgentData(int totalCount, SavedAgentData keepData)
        {
            // 에이전트 데이터만 초기화 후 최신 1개 재저장
            // 세션과 채팅 기록은 보존하여 대화 이어나기 가능
            AgentDataStore.ClearAll();

            var data = keepData.ToCreationData();
            data.AvatarPrefabName = keepData.ModelPrefabName;
            AgentDataStore.Save(data, keepData.ModelPrefabName);
        }

        private GameObject FindPrefab(string prefabName)
        {
            if (_modelPrefabs != null)
            {
                foreach (var entry in _modelPrefabs)
                {
                    if (entry.PrefabName == prefabName && entry.Prefab != null)
                        return entry.Prefab;
                }
            }

            var loaded = Resources.Load<GameObject>(prefabName);
            if (loaded != null) return loaded;

            Debug.LogWarning($"[OfficeBootstrapper] 프리팹 못 찾음: {prefabName}, 기본 프리팹 사용");
            return null;
        }

        [ContextMenu("Clear All & Respawn")]
        public void DebugClearAndRespawn()
        {
            _spawner.DespawnAll();
            CurrentAgent = null;
            SpawnLatestAgent();
        }
    }
}
