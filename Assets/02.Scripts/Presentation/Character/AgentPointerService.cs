using System;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 3D 에이전트 캐릭터에 대한 마우스 호버/클릭을 단일 raycast 로 통합 검출하는 서비스.
    ///
    /// AgentHoverHandler 가 <see cref="HoverChanged"/> 를 구독해 HUD 페이드 토글,
    /// AgentClickHandler 가 <see cref="Clicked"/> 를 구독해 채팅 + 카메라 포커스 트리거.
    /// 호버/클릭이 동일 raycast 결과를 공유하므로 1프레임 정합성이 보장된다.
    ///
    /// 입력 정책:
    ///  - 좌클릭 wasPressedThisFrame 만 사용 (New Input System)
    ///  - <see cref="EventSystem.IsPointerOverGameObject"/> 가 true 면 uGUI/UI Toolkit 위 클릭으로 간주해 차단
    /// </summary>
    public sealed class AgentPointerService : MonoBehaviour, IDisposable
    {
        [Header("Raycast")]
        [SerializeField] private float _maxRayDistance = 100f;
        [SerializeField] private LayerMask _agentLayer = ~0;

        [Header("References")]
        [SerializeField] private Camera _mainCamera;

        private AgentSpawner _spawner;
        private readonly Subject<AgentSpawner.SpawnedAgent> _hoverChanged = new();
        private readonly Subject<AgentSpawner.SpawnedAgent> _clicked = new();
        private AgentSpawner.SpawnedAgent _currentHover;
        private bool _disposed;

        /// <summary>
        /// 호버 대상이 바뀔 때마다 발행. 호버가 해제되면 null 을 발행.
        /// 동일 대상으로의 연속 hover 는 발행되지 않음 (내부 distinct 처리).
        /// </summary>
        public Observable<AgentSpawner.SpawnedAgent> HoverChanged => _hoverChanged;

        /// <summary>좌클릭이 에이전트 위에서 발생했을 때 해당 SpawnedAgent 를 발행.</summary>
        public Observable<AgentSpawner.SpawnedAgent> Clicked => _clicked;

        /// <summary>현재 호버 중인 에이전트 (없으면 null).</summary>
        public AgentSpawner.SpawnedAgent CurrentHover => _currentHover;

        [Inject]
        public void Construct(AgentSpawner spawner)
        {
            _spawner = spawner;
        }

        private void Start()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
        }

        private void Update()
        {
            if (_disposed) return;

            var mouse = Mouse.current;
            if (mouse == null || _mainCamera == null) return;

            // UI 위 호버/클릭은 캐릭터 인터랙션 차단.
            // EventSystem.IsPointerOverGameObject() 는 uGUI + UI Toolkit (PanelSettings.sortingOrder 가 적절히 설정된 경우) 모두 커버.
            var blockedByUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            var hit = blockedByUI ? null : RaycastForAgent(mouse.position.ReadValue());

            // Hover stream — DistinctUntilChanged 내장
            if (!ReferenceEquals(hit, _currentHover))
            {
                _currentHover = hit;
                _hoverChanged.OnNext(hit);
            }

            // Click stream — 캐릭터 위에서만 발행
            if (mouse.leftButton.wasPressedThisFrame && hit != null)
            {
                _clicked.OnNext(hit);
            }
        }

        private AgentSpawner.SpawnedAgent RaycastForAgent(Vector2 screenPos)
        {
            var ray = _mainCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var info, _maxRayDistance, _agentLayer)) return null;

            var agentRoot = FindAgentRoot(info.collider.gameObject);
            return agentRoot != null ? FindSpawnedAgent(agentRoot) : null;
        }

        // 클릭/호버된 콜라이더 → "Agent_" 프리픽스 root GameObject 탐색.
        private static GameObject FindAgentRoot(GameObject obj)
        {
            var t = obj.transform;
            while (t != null)
            {
                if (t.name.StartsWith("Agent_")) return t.gameObject;
                t = t.parent;
            }
            return null;
        }

        private AgentSpawner.SpawnedAgent FindSpawnedAgent(GameObject agentRoot)
        {
            if (_spawner == null) return null;
            foreach (var kv in _spawner.SpawnedAgents)
            {
                if (kv.Value.ModelInstance == agentRoot) return kv.Value;
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _hoverChanged.Dispose();
            _clicked.Dispose();
        }

        private void OnDestroy() => Dispose();
    }
}
