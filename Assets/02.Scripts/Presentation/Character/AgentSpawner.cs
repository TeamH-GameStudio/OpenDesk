using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using OpenDesk.Core.Services;
using UnityEngine;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// AgentProfileSO를 받아 3D 모델을 소환하고 HUD를 부착하는 매니저.
    /// SpawnPoints 배열에서 빈 위치를 자동 선택.
    /// </summary>
    public class AgentSpawner : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private Transform[] _spawnPoints;
        [Tooltip("ModelPrefab이 null일 때 사용할 플레이스홀더")]
        [SerializeField] private GameObject _defaultModelPrefab;

        [Header("HUD")]
        [SerializeField] private GameObject _hudPrefab;
        [SerializeField] private float _hudHeight = 2.2f;

        private IAgentStateService _stateService;

        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            try { _stateService = resolver.Resolve<IAgentStateService>(); }
            catch { _stateService = null; }
        }

        // ── 소환 관리 ───────────────────────────────────────
        private readonly Dictionary<string, SpawnedAgent> _spawnedAgents = new();
        private readonly HashSet<int> _occupiedPoints = new();

        /// <summary>소환된 에이전트 정보</summary>
        public class SpawnedAgent
        {
            public string SessionId;
            public AgentProfileSO Profile;
            public GameObject ModelInstance;
            public AgentHUDController HUD;
            public AgentAnimationController AnimController;
            public int SpawnPointIndex;
        }

        // ================================================================
        //  소환
        // ================================================================

        /// <summary>
        /// 에이전트를 3D 공간에 소환.
        /// 빈 SpawnPoint를 자동 선택, 모델 인스턴스화 + HUD 부착.
        /// </summary>
        public SpawnedAgent SpawnAgent(AgentProfileSO profile)
        {
            if (profile == null || !profile.IsValid)
            {
                Debug.LogWarning("[AgentSpawner] 유효하지 않은 프로필");
                return null;
            }

            if (_spawnedAgents.ContainsKey(profile.SessionId))
            {
                Debug.LogWarning($"[AgentSpawner] 이미 소환됨: {profile.SessionId}");
                return _spawnedAgents[profile.SessionId];
            }

            // 빈 SpawnPoint 찾기
            int pointIndex = FindAvailableSpawnPoint();
            if (pointIndex < 0)
            {
                Debug.LogWarning("[AgentSpawner] 빈 SpawnPoint 없음");
                return null;
            }

            var spawnPos = _spawnPoints[pointIndex].position;
            var spawnRot = _spawnPoints[pointIndex].rotation;

            // 1) 모델 인스턴스화
            var prefab = profile.ModelPrefab != null ? profile.ModelPrefab : _defaultModelPrefab;
            if (prefab == null)
            {
                Debug.LogError("[AgentSpawner] 모델 프리팹 없음");
                return null;
            }

            var modelInstance = Instantiate(prefab, spawnPos, spawnRot);
            modelInstance.name = $"Agent_{profile.AgentName}";

            Debug.Log($"[AgentSpawner] 프리팹: {prefab.name}, profile.ModelPrefab={(profile.ModelPrefab != null ? profile.ModelPrefab.name : "null")}");

            // Animator Controller 확인
            var spawnedAnimator = modelInstance.GetComponentInChildren<Animator>();
            Debug.Log($"[AgentSpawner] Animator: {(spawnedAnimator != null ? "있음" : "없음")}, Controller: {(spawnedAnimator?.runtimeAnimatorController != null ? spawnedAnimator.runtimeAnimatorController.name : "NULL")}");

            // AgentCharacterController에 identity 전달
            var charCtrl = modelInstance.GetComponent<AgentCharacterController>();
            if (charCtrl != null)
                charCtrl.SetIdentity(profile.SessionId, profile.AgentName);

            // AgentAnimationController (간이 전환용, FSM과 별도)
            var animator = modelInstance.GetComponentInChildren<Animator>();
            var animController = modelInstance.GetComponent<AgentAnimationController>();
            if (animController == null)
                animController = modelInstance.AddComponent<AgentAnimationController>();
            animController.Initialize(animator);

            // 2) HUD 부착
            AgentHUDController hud = null;
            if (_hudPrefab != null)
            {
                var hudPos = spawnPos + Vector3.up * _hudHeight;
                var hudInstance = Instantiate(_hudPrefab, hudPos, Quaternion.identity, modelInstance.transform);
                hud = hudInstance.GetComponent<AgentHUDController>();
                hud?.Initialize(profile, _stateService);
            }

            // 3) 등록
            var spawned = new SpawnedAgent
            {
                SessionId = profile.SessionId,
                Profile = profile,
                ModelInstance = modelInstance,
                HUD = hud,
                AnimController = animController,
                SpawnPointIndex = pointIndex
            };

            _spawnedAgents[profile.SessionId] = spawned;
            _occupiedPoints.Add(pointIndex);

            Debug.Log($"[AgentSpawner] 소환 완료: {profile.AgentName} @ SpawnPoint[{pointIndex}]");
            return spawned;
        }

        // ================================================================
        //  제거
        // ================================================================

        public void DespawnAgent(string sessionId)
        {
            if (!_spawnedAgents.TryGetValue(sessionId, out var spawned))
            {
                Debug.LogWarning($"[AgentSpawner] 없는 에이전트: {sessionId}");
                return;
            }

            _occupiedPoints.Remove(spawned.SpawnPointIndex);
            if (spawned.ModelInstance != null)
                Destroy(spawned.ModelInstance);

            _spawnedAgents.Remove(sessionId);
            Debug.Log($"[AgentSpawner] 제거: {spawned.Profile.AgentName}");
        }

        public void DespawnAll()
        {
            foreach (var kv in _spawnedAgents)
            {
                if (kv.Value.ModelInstance != null)
                    Destroy(kv.Value.ModelInstance);
            }
            _spawnedAgents.Clear();
            _occupiedPoints.Clear();
        }

        // ================================================================
        //  조회
        // ================================================================

        public SpawnedAgent GetAgent(string sessionId)
            => _spawnedAgents.GetValueOrDefault(sessionId);

        public IReadOnlyDictionary<string, SpawnedAgent> SpawnedAgents => _spawnedAgents;

        public int AvailableSpawnPointCount
        {
            get
            {
                if (_spawnPoints == null) return 0;
                return _spawnPoints.Length - _occupiedPoints.Count;
            }
        }

        // ================================================================
        //  내부
        // ================================================================

        private int FindAvailableSpawnPoint()
        {
            if (_spawnPoints == null) return -1;
            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                if (!_occupiedPoints.Contains(i))
                    return i;
            }
            return -1;
        }
    }
}
