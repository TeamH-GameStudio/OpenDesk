using System;
using System.Collections.Generic;
using OpenDesk.AgentCreation.Models;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// AgentProfileSO 를 받아 3D 모델을 소환하고 Cinemachine 카메라 타겟을 설정하는 매니저.<br/>
    /// HUD 는 <see cref="OpenDesk.Presentation.UI.Hud.AgentHudView"/> 가 Spawned 이벤트를 구독해 별도로 그린다.<br/>
    /// SpawnPoints 배열에서 빈 위치를 자동 선택.
    /// </summary>
    public class AgentSpawner : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private Transform[] _spawnPoints;
        [Tooltip("ModelPrefab이 null일 때 사용할 플레이스홀더")]
        [SerializeField] private GameObject _defaultModelPrefab;

        [Header("Camera Target")]
        [Tooltip("Cinemachine 카메라가 LookAt 할 자식 트랜스폼의 로컬 Y (가슴 높이). Spawner 가 자동 생성.")]
        [SerializeField] private float _cameraTargetHeight = 1.4f;
        [SerializeField] private string _cameraTargetChildName = "CameraTarget";

        // 레거시 _stateService 슬롯은 제거됨 — HUD/카메라가 모두 IAgentStateService 를 자체 구독.

        /// <summary>
        /// 캐릭터가 despawn 될 때 sessionId 발행.
        /// <see cref="OpenDesk.Presentation.Cameras.CinemachineCameraFocusService"/> 가 구독해
        /// 포커스 중이던 캐릭터가 사라지면 자동 release 한다. <see cref="OpenDesk.Presentation.UI.Hud.AgentHudView"/> 도 구독.
        /// </summary>
        public event Action<string> Despawned;

        /// <summary>
        /// 캐릭터가 spawn 된 직후 발행 — SpawnedAgent 인스턴스를 전달.
        /// <see cref="OpenDesk.Presentation.UI.Hud.AgentHudView"/> 가 구독해 HUD 카드를 생성한다.
        /// 등록 자체는 spawn 완료 후 마지막에 invoke 되므로 구독자는 GetAgent/SpawnedAgents 로도 동일 인스턴스 조회 가능.
        /// </summary>
        public event Action<SpawnedAgent> Spawned;

        // VContainer 리졸버 — Instantiate 한 prefab 에 [Inject] 를 발화시키기 위해 보관.
        // Spawner 자체 동작에는 다른 의존성이 없으므로 옵셔널 — 미주입 시 spawn 된 캐릭터의 Construct 가 호출되지 않아
        // IAgentStateService 구독이 빠진다 (= 채팅 상태가 캐릭터까지 도달하지 않음).
        private IObjectResolver _resolver;

        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        // ── 소환 관리 ───────────────────────────────────────
        private readonly Dictionary<string, SpawnedAgent> _spawnedAgents = new();
        private readonly HashSet<int> _occupiedPoints = new();

        /// <summary>소환된 에이전트 정보. HUD 는 UI Toolkit AgentHudView 가 별도로 카드를 그리므로 여기 슬롯 없음.</summary>
        public class SpawnedAgent
        {
            public string SessionId;
            public AgentProfileSO Profile;
            public GameObject ModelInstance;
            public AgentAnimationController AnimController;
            public int SpawnPointIndex;
        }

        // ================================================================
        //  소환
        // ================================================================

        /// <summary>
        /// 에이전트를 3D 공간에 소환. 빈 SpawnPoint 자동 선택 + 모델 인스턴스화 + Cinemachine 카메라 타겟 자식 생성.
        /// HUD 는 AgentHudView (UI Toolkit) 가 Spawned 이벤트를 구독해 별도로 그린다.
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

            // VContainer [Inject] 발화 — 반드시 SetIdentity 보다 먼저 호출해야 한다.
            // SetIdentity 가 _initialized==false 면 InitializeFSM 을 즉시 부르고, 그 안에서
            // _agentStateService.OnStateChanged 를 구독하므로 그 시점에 Construct 가 완료돼있어야 한다.
            // _resolver 미주입(테스트 씬 등) 시 silent skip — Spawner 자체는 계속 동작.
            if (_resolver != null)
                _resolver.InjectGameObject(modelInstance);

            // AgentCharacterController에 identity + profile 전달
            var charCtrl = modelInstance.GetComponent<AgentCharacterController>();
            if (charCtrl != null)
            {
                charCtrl.SetProfile(profile);
                charCtrl.SetIdentity(profile.SessionId, profile.AgentName);
            }

            // AgentAnimationController (간이 전환용, FSM과 별도)
            var animator = modelInstance.GetComponentInChildren<Animator>();
            var animController = modelInstance.GetComponent<AgentAnimationController>();
            if (animController == null)
                animController = modelInstance.AddComponent<AgentAnimationController>();
            animController.Initialize(animator);

            // 2) Cinemachine 카메라 타겟 자식 생성 (가슴 높이) — FocusVCam 의 LookAt 대상.
            EnsureCameraTarget(modelInstance);

            // 3) 등록
            var spawned = new SpawnedAgent
            {
                SessionId = profile.SessionId,
                Profile = profile,
                ModelInstance = modelInstance,
                AnimController = animController,
                SpawnPointIndex = pointIndex
            };

            _spawnedAgents[profile.SessionId] = spawned;
            _occupiedPoints.Add(pointIndex);

            Debug.Log($"[AgentSpawner] 소환 완료: {profile.AgentName} @ SpawnPoint[{pointIndex}]");
            Spawned?.Invoke(spawned);
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
            Despawned?.Invoke(sessionId);
            Debug.Log($"[AgentSpawner] 제거: {spawned.Profile.AgentName}");
        }

        public void DespawnAll()
        {
            // 키 복사 후 순회 — Despawned 구독자가 GetAgent 등을 호출할 가능성에 대비.
            var ids = new List<string>(_spawnedAgents.Keys);
            foreach (var id in ids)
            {
                if (_spawnedAgents.TryGetValue(id, out var spawned) && spawned.ModelInstance != null)
                    Destroy(spawned.ModelInstance);
                Despawned?.Invoke(id);
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

        // Cinemachine FocusVCam 이 LookAt/Follow 할 자식 트랜스폼.
        // 캐릭터 root 는 발 위치라 미디엄샷 구도가 어긋난다 → 가슴 높이(y=_cameraTargetHeight) 빈 GameObject 를 자식으로 생성.
        // 이미 존재하면 no-op.
        private void EnsureCameraTarget(GameObject modelInstance)
        {
            if (modelInstance == null) return;

            var existing = modelInstance.transform.Find(_cameraTargetChildName);
            if (existing != null) return;

            var targetGo = new GameObject(_cameraTargetChildName);
            targetGo.transform.SetParent(modelInstance.transform, worldPositionStays: false);
            targetGo.transform.localPosition = new Vector3(0f, _cameraTargetHeight, 0f);
            targetGo.transform.localRotation = Quaternion.identity;
        }
    }
}
