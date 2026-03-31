using System.Collections.Generic;
using OpenDesk.Presentation.Character;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace OpenDesk.SkillDiskette
{
    /// <summary>
    /// 디스켓 선반 UI.
    /// 화면 구석에 세로 배열, 각 디스켓 카드(색상 + 이름) 표시.
    /// 카드를 드래그 → 3D 에이전트 위에 드롭 → 장착.
    /// </summary>
    public class DisketteShelfUI : MonoBehaviour
    {
        [Header("UI 참조")]
        [SerializeField] private Transform _cardContainer;
        [SerializeField] private GameObject _cardPrefab;
        [SerializeField] private ScrollRect _scrollRect;

        [Header("드래그 고스트")]
        [SerializeField] private RectTransform _dragGhost;
        [SerializeField] private TextMeshProUGUI _dragGhostLabel;
        [SerializeField] private Image _dragGhostBg;

        // ── 내부 ──
        private readonly List<DisketteCardEntry> _cards = new();
        private Camera _mainCamera;

        // ── 드래그 상태 ──
        private bool _isDragging;
        private SkillDiskette _draggingDiskette;

        private struct DisketteCardEntry
        {
            public SkillDiskette Diskette;
            public GameObject CardObject;
        }

        // ══════════════════════════════════════════════
        //  초기화
        // ══════════════════════════════════════════════

        private void Start()
        {
            _mainCamera = Camera.main;
            if (_dragGhost != null)
                _dragGhost.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════
        //  디스켓 추가/제거
        // ══════════════════════════════════════════════

        /// <summary>선반에 디스켓 카드 추가</summary>
        public void AddDiskette(SkillDiskette diskette)
        {
            if (diskette == null) return;
            if (_cards.Exists(c => c.Diskette.SkillId == diskette.SkillId)) return;

            var card = CreateCard(diskette);
            _cards.Add(new DisketteCardEntry
            {
                Diskette = diskette,
                CardObject = card
            });

            // 레이아웃 강제 갱신
            RebuildLayout();
        }

        /// <summary>장착된 디스켓을 선반에서 제거</summary>
        public void RemoveDiskette(string skillId)
        {
            var idx = _cards.FindIndex(c => c.Diskette.SkillId == skillId);
            if (idx < 0) return;

            if (_cards[idx].CardObject != null)
                Destroy(_cards[idx].CardObject);
            _cards.RemoveAt(idx);

            RebuildLayout();
        }

        /// <summary>장착 해제된 디스켓을 다시 선반에 추가</summary>
        public void ReturnDiskette(SkillDiskette diskette)
        {
            AddDiskette(diskette);
        }

        // ══════════════════════════════════════════════
        //  카드 생성
        // ══════════════════════════════════════════════

        private GameObject CreateCard(SkillDiskette diskette)
        {
            if (_cardPrefab == null || _cardContainer == null) return null;

            var card = Instantiate(_cardPrefab, _cardContainer);
            card.SetActive(true);
            card.name = $"Card_{diskette.SkillId}";

            // 색상 바
            var colorBar = card.transform.Find("ColorBar");
            if (colorBar != null)
            {
                var img = colorBar.GetComponent<Image>();
                if (img != null) img.color = diskette.Color;
            }

            // 이름
            var nameText = card.transform.Find("NameLabel");
            if (nameText != null)
            {
                var tmp = nameText.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.SetText(diskette.DisplayName);
            }

            // 카테고리
            var catText = card.transform.Find("CategoryLabel");
            if (catText != null)
            {
                var tmp = catText.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.SetText(diskette.Category.ToString());
            }

            // 드래그 이벤트
            var trigger = card.AddComponent<EventTrigger>();

            // BeginDrag
            var beginEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
            beginEntry.callback.AddListener(_ => OnBeginDrag(diskette));
            trigger.triggers.Add(beginEntry);

            // Drag
            var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            dragEntry.callback.AddListener(data => OnDrag((PointerEventData)data));
            trigger.triggers.Add(dragEntry);

            // EndDrag
            var endEntry = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
            endEntry.callback.AddListener(_ => OnEndDrag());
            trigger.triggers.Add(endEntry);

            return card;
        }

        // ══════════════════════════════════════════════
        //  드래그 & 드롭 (UI → 3D 에이전트)
        // ══════════════════════════════════════════════

        private void OnBeginDrag(SkillDiskette diskette)
        {
            _isDragging = true;
            _draggingDiskette = diskette;

            // 스크롤 중 드래그 방지
            if (_scrollRect != null)
                _scrollRect.enabled = false;

            // 고스트 표시
            if (_dragGhost != null)
            {
                _dragGhost.gameObject.SetActive(true);
                if (_dragGhostLabel != null)
                    _dragGhostLabel.SetText(diskette.DisplayName);
                if (_dragGhostBg != null)
                    _dragGhostBg.color = diskette.Color;
            }
        }

        private void OnDrag(PointerEventData data)
        {
            if (!_isDragging || _dragGhost == null) return;
            _dragGhost.position = data.position;
        }

        private void OnEndDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;

            if (_scrollRect != null)
                _scrollRect.enabled = true;

            if (_dragGhost != null)
                _dragGhost.gameObject.SetActive(false);

            // 3D 에이전트 감지
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || _draggingDiskette == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            var mousePos = mouse.position.ReadValue();
            var ray = _mainCamera.ScreenPointToRay(mousePos);

            AgentCharacterController targetAgent = null;

            // Raycast로 에이전트 히트
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                targetAgent = hit.collider.GetComponentInParent<AgentCharacterController>();
            }

            // Raycast 실패 시 화면 기준 근접 체크
            if (targetAgent == null)
            {
                var agents = FindObjectsByType<AgentCharacterController>(FindObjectsSortMode.None);
                float closestScreenDist = 150f; // 150px 이내

                foreach (var a in agents)
                {
                    var screenPos = _mainCamera.WorldToScreenPoint(a.transform.position);
                    if (screenPos.z < 0) continue;
                    var dist = Vector2.Distance(mousePos, new Vector2(screenPos.x, screenPos.y));
                    if (dist < closestScreenDist)
                    {
                        closestScreenDist = dist;
                        targetAgent = a;
                    }
                }
            }

            if (targetAgent != null)
            {
                var equipment = targetAgent.Equipment;
                if (equipment != null && equipment.TryEquip(_draggingDiskette))
                {
                    RemoveDiskette(_draggingDiskette.SkillId);
                    Debug.Log($"[ShelfUI] '{_draggingDiskette.DisplayName}' -> '{targetAgent.AgentName}' 장착");
                }
                else
                {
                    Debug.Log($"[ShelfUI] 장착 실패 - 슬롯 부족 또는 중복");
                }
            }

            _draggingDiskette = null;
        }

        // ══════════════════════════════════════════════
        //  레이아웃 갱신
        // ══════════════════════════════════════════════

        private void RebuildLayout()
        {
            if (_cardContainer == null) return;

            Canvas.ForceUpdateCanvases();

            var contentRect = _cardContainer.GetComponent<RectTransform>();
            if (contentRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
