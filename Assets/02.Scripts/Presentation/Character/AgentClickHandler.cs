using OpenDesk.AgentCreation.Models;
using OpenDesk.Presentation.Camera;
using OpenDesk.Presentation.UI;
using OpenDesk.Presentation.UI.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 3D 에이전트 클릭 감지 → SessionListController 오픈 + 카메라 포커스.
    /// 마우스 좌클릭 Raycast로 에이전트 식별 후 해당 에이전트의 세션 패널을 열고,
    /// Cinemachine 카메라를 에이전트 정면으로 이동시킨다.
    /// </summary>
    public class AgentClickHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;
        [SerializeField] private SessionListController _sessionList;
        [SerializeField] private UnityEngine.Camera _mainCamera;
        [SerializeField] private AgentFocusCameraController _focusCamera;

        [Header("이소메트릭 카메라 (새 프로토콜 씬용)")]
        [SerializeField] private IsometricCameraController _isometricCamera;

        [Header("패널 토글")]
        [SerializeField] private LeftPanelToggle _leftPanelToggle;

        [Header("Settings")]
        [SerializeField] private float _maxRayDistance = 100f;
        [SerializeField] private LayerMask _agentLayer = ~0; // 기본: 모든 레이어

        private void Start()
        {
            if (_mainCamera == null)
                _mainCamera = UnityEngine.Camera.main;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _mainCamera == null) return;

            // 좌클릭
            if (!mouse.leftButton.wasPressedThisFrame) return;

            // UI 위에서 클릭한 경우 무시
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            var ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, _agentLayer))
                return; // 빈 공간 클릭 → 무시

            // 클릭된 오브젝트에서 에이전트 찾기 (본인 또는 부모)
            var clickedObj = hit.collider.gameObject;
            var agentRoot = FindAgentRoot(clickedObj);
            if (agentRoot == null)
            {
                // 에이전트가 아닌 오브젝트 → 무시
                return;
            }

            // Spawner에서 해당 에이전트 정보 찾기
            var spawnedAgent = FindSpawnedAgent(agentRoot);
            if (spawnedAgent == null) return;

            Debug.Log($"[AgentClick] 에이전트 클릭: {spawnedAgent.Profile.AgentName}");

            // 에이전트 인덱스 찾기 (DataStore 기반)
            int agentIndex = FindAgentDataIndex(spawnedAgent.Profile.AgentName);

            // 디버그: 위저드 세팅값 전체 출력
            var savedData = AgentDataStore.Load(agentIndex);
            if (savedData != null)
                Debug.Log($"[AgentClick] 프로필 상세:\n{savedData.ToDebugString()}");

            // 세션이 없으면 자동 생성
            var sessions = AgentSessionStore.LoadByAgent(agentIndex);
            if (sessions.Count == 0)
            {
                AgentSessionStore.CreateSession(
                    agentIndex,
                    spawnedAgent.Profile.AgentName,
                    spawnedAgent.Profile.Role);
            }

            // 카메라 포커스
            if (_isometricCamera != null)
                _isometricCamera.FocusOnAgent(agentRoot.transform);
            else
                _focusCamera?.FocusOnAgent(agentRoot.transform);

            // 우측 세션+채팅 패널 활성화
            _leftPanelToggle?.Show();

            // 세션 리스트 패널 오픈
            if (_sessionList != null)
            {
                _sessionList.OpenForAgent(
                    agentIndex,
                    spawnedAgent.Profile.AgentName,
                    spawnedAgent.Profile.Role);
            }
        }

        /// <summary>클릭된 오브젝트에서 에이전트 루트 (Agent_ 이름) 찾기</summary>
        private static GameObject FindAgentRoot(GameObject obj)
        {
            var current = obj.transform;
            while (current != null)
            {
                if (current.name.StartsWith("Agent_"))
                    return current.gameObject;
                current = current.parent;
            }
            return null;
        }

        /// <summary>Spawner의 목록에서 에이전트 찾기</summary>
        private AgentSpawner.SpawnedAgent FindSpawnedAgent(GameObject agentRoot)
        {
            if (_spawner == null) return null;

            foreach (var kv in _spawner.SpawnedAgents)
            {
                if (kv.Value.ModelInstance == agentRoot)
                    return kv.Value;
            }
            return null;
        }

        /// <summary>에이전트 이름으로 DataStore 인덱스 찾기</summary>
        private static int FindAgentDataIndex(string agentName)
        {
            int count = AgentDataStore.Count;
            for (int i = 0; i < count; i++)
            {
                var data = AgentDataStore.Load(i);
                if (data != null && data.AgentName == agentName)
                    return i;
            }
            return 0;
        }
    }
}
