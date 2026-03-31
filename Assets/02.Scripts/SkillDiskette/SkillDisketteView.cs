using System.Collections.Generic;
using OpenDesk.Presentation.Character;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 디스켓 3D 오브젝트의 비주얼 + 드래그&드롭 인터랙션.
    /// Update에서 Physics.Raycast로 직접 처리 (UI Canvas 간섭 회피).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SkillDisketteView : MonoBehaviour
    {
        [Header("비주얼")]
        [SerializeField] private TextMeshPro _nameLabel;
        [SerializeField] private MeshRenderer _bodyRenderer;

        // ── 내부 ──
        private SkillDiskette _data;
        private Camera _mainCamera;
        private bool _isEquipped;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;

        // ── 드래그 ──
        private bool _isDragging;
        private float _dragDepth;
        private Vector3 _dragOffset;
        private bool _isReturning;

        private const float DragHeight = 1.5f;
        private const float ReturnSpeed = 10f;
        private const float EquipDistance = 2.5f;

        // ── 현재 드래그 중인 디스켓 (전역 — 동시 드래그 방지) ──
        private static SkillDisketteView _currentDragging;

        public SkillDiskette Data => _data;
        public bool IsEquipped => _isEquipped;

        // ══════════════════════════════════════════════
        //  초기화
        // ══════════════════════════════════════════════

        public void Initialize(SkillDiskette diskette)
        {
            _data = diskette;
            _mainCamera = Camera.main;

            if (_nameLabel != null)
                _nameLabel.SetText(diskette.DisplayName);

            if (_bodyRenderer != null)
            {
                var mat = _bodyRenderer.material;
                // URP: _BaseColor, Standard: _Color
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", diskette.Color);
                else
                    mat.color = diskette.Color;
            }

            _originalPosition = transform.position;
            _originalRotation = transform.rotation;
        }

        // ══════════════════════════════════════════════
        //  Update — 드래그&드롭 직접 처리
        // ══════════════════════════════════════════════

        private void Update()
        {
            if (_isEquipped) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            var mousePos = mouse.position.ReadValue();

            // ── 마우스 클릭 시작 ──
            if (mouse.leftButton.wasPressedThisFrame && !_isDragging && _currentDragging == null)
            {
                if (IsPointerOverUI()) return;

                var ray = _mainCamera.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out var hit, 100f))
                {
                    if (hit.collider.gameObject == gameObject ||
                        hit.collider.transform.IsChildOf(transform))
                    {
                        StartDrag(mousePos);
                    }
                }
            }

            // ── 드래그 중 ──
            if (_isDragging && mouse.leftButton.isPressed)
            {
                DragUpdate(mousePos);
            }

            // ── 마우스 놓기 ──
            if (_isDragging && mouse.leftButton.wasReleasedThisFrame)
            {
                EndDrag();
            }

            // ── 원위치 복귀 ──
            if (_isReturning)
            {
                transform.position = Vector3.Lerp(
                    transform.position, _originalPosition, Time.deltaTime * ReturnSpeed);
                transform.rotation = Quaternion.Lerp(
                    transform.rotation, _originalRotation, Time.deltaTime * ReturnSpeed);

                if (Vector3.Distance(transform.position, _originalPosition) < 0.02f)
                {
                    transform.SetPositionAndRotation(_originalPosition, _originalRotation);
                    _isReturning = false;
                }
            }
        }

        // ══════════════════════════════════════════════
        //  드래그 시작/중/종료
        // ══════════════════════════════════════════════

        private void StartDrag(Vector2 mousePos)
        {
            _isDragging = true;
            _isReturning = false;
            _currentDragging = this;

            _dragDepth = _mainCamera.WorldToScreenPoint(transform.position).z;

            var mouseWorld = _mainCamera.ScreenToWorldPoint(
                new Vector3(mousePos.x, mousePos.y, _dragDepth));
            _dragOffset = transform.position - mouseWorld;

            Debug.Log($"[DisketteView] 드래그 시작: {_data?.DisplayName}");
        }

        private void DragUpdate(Vector2 mousePos)
        {
            var mouseWorld = _mainCamera.ScreenToWorldPoint(
                new Vector3(mousePos.x, mousePos.y, _dragDepth));

            var targetPos = mouseWorld + _dragOffset;
            targetPos.y = DragHeight;
            transform.position = targetPos;
        }

        private void EndDrag()
        {
            _isDragging = false;
            _currentDragging = null;

            // 에이전트 감지 (근접 거리 체크)
            if (TryFindNearestAgent(out var agent))
            {
                var equipment = agent.Equipment;
                if (equipment != null && equipment.TryEquip(_data))
                {
                    OnEquipSuccess(agent);
                    return;
                }
                Debug.Log($"[DisketteView] 장착 실패 - 슬롯 부족 또는 중복");
            }

            // 장착 실패 → 원위치 복귀
            _isReturning = true;
            Debug.Log($"[DisketteView] 드롭 실패, 원위치 복귀");
        }

        // ══════════════════════════════════════════════
        //  에이전트 감지
        // ══════════════════════════════════════════════

        private bool TryFindNearestAgent(out AgentCharacterController agent)
        {
            agent = null;

            var agents = FindObjectsByType<AgentCharacterController>(FindObjectsSortMode.None);
            float closestDist = EquipDistance;

            foreach (var a in agents)
            {
                var dist = Vector3.Distance(transform.position, a.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    agent = a;
                }
            }

            return agent != null;
        }

        // ══════════════════════════════════════════════
        //  장착 성공
        // ══════════════════════════════════════════════

        private void OnEquipSuccess(AgentCharacterController agent)
        {
            _isEquipped = true;
            Debug.Log($"[DisketteView] '{_data.DisplayName}' -> '{agent.AgentName}' 장착 완료");

            transform.position = agent.transform.position + Vector3.up * 2.5f;
            transform.localScale = Vector3.one * 0.3f;

            Destroy(gameObject, 0.5f);
        }

        // ══════════════════════════════════════════════
        //  유틸
        // ══════════════════════════════════════════════

        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;

            var mouse = Mouse.current;
            if (mouse == null) return false;

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = mouse.position.ReadValue()
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            return results.Count > 0;
        }
    }
}
