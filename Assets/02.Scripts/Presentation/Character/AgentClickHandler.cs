using OpenDesk.AgentCreation.Models;
using OpenDesk.Presentation.UI.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 3D 에이전트 클릭 감지 -> SessionListController 오픈.
    /// agent_id 기반 멀티 에이전트 라우팅.
    /// AgentCharacterController에서 AgentId를 읽어 미들웨어 에이전트와 매핑.
    /// </summary>
    public class AgentClickHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AgentSpawner _spawner;
        [SerializeField] private SessionListController _sessionList;
        [SerializeField] private Camera _mainCamera;

        [Header("Settings")]
        [SerializeField] private float _maxRayDistance = 100f;
        [SerializeField] private LayerMask _agentLayer = ~0;

        private void Start()
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || _mainCamera == null) return;

            if (!mouse.leftButton.wasPressedThisFrame) return;

            // UI 위에서 클릭한 경우 무시
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            var ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, _agentLayer))
                return;

            var clickedObj = hit.collider.gameObject;
            var agentRoot = FindAgentRoot(clickedObj);
            if (agentRoot == null) return;

            // AgentCharacterController에서 agent_id 읽기
            var charCtrl = agentRoot.GetComponent<AgentCharacterController>();
            if (charCtrl == null) return;

            var agentId = charCtrl.AgentId;
            var agentName = charCtrl.AgentName;
            var role = charCtrl.Profile != null ? charCtrl.Profile.Role : AgentRole.Research;

            if (string.IsNullOrEmpty(agentId))
            {
                Debug.LogWarning($"[AgentClick] AgentId가 설정되지 않음: {agentRoot.name}");
                return;
            }

            Debug.Log($"[AgentClick] 에이전트 클릭: {agentName} (id: {agentId})");

            // 세션 리스트 패널 오픈 (agent_id 기반)
            if (_sessionList != null)
                _sessionList.OpenForAgent(agentId, agentName, role);
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
    }
}
