using System;
using OpenDesk.Presentation.Cameras;
using OpenDesk.Presentation.UI.Chat;
using R3;
using UnityEngine;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 3D 에이전트 클릭 → UI Toolkit ChatPanelView 진입 + Cinemachine 카메라 포커스.
    ///
    /// 이전 구현은 자체 Physics.Raycast + 레거시 uGUI SessionListController 호출이었으나,
    /// raycast 는 <see cref="AgentPointerService"/> 로 통합되고 채팅 경로는 ChatPanelView (UI Toolkit) 로 일원화됨.
    /// </summary>
    public sealed class AgentClickHandler : MonoBehaviour, IDisposable
    {
        private AgentPointerService _pointerService;
        private ChatPanelView _chatPanel;
        private ICameraFocusService _cameraFocus;
        private IDisposable _subscription;
        private bool _disposed;

        [Inject]
        public void Construct(
            AgentPointerService pointerService,
            ChatPanelView chatPanel = null,
            ICameraFocusService cameraFocus = null)
        {
            _pointerService = pointerService;
            _chatPanel = chatPanel;
            _cameraFocus = cameraFocus;
        }

        private void Start()
        {
            if (_pointerService == null)
            {
                Debug.LogWarning("[AgentClickHandler] AgentPointerService 미주입 — 클릭 핸들러 비활성");
                return;
            }

            _subscription = _pointerService.Clicked.Subscribe(HandleClick);
        }

        private void HandleClick(AgentSpawner.SpawnedAgent spawned)
        {
            if (spawned == null || spawned.Profile == null) return;

            var sessionId = spawned.Profile.SessionId;
            var name = spawned.Profile.AgentName;
            var role = spawned.Profile.Role;
            // 위저드 자유 텍스트(예: "글쓰기 전문가") 는 JSON Source 에만 보존된다 — enum 매핑이 누락된 입력은
            // ParseRole 폴백으로 Planning 이 되므로, rawRole 을 별도로 넘겨 system prompt 의 "전문 분야:" 줄에
            // 손실 없이 들어가게 한다.
            var rawRole = spawned.Profile.Source?.role;

            Debug.Log($"[AgentClick] 에이전트 클릭: {name} ({sessionId})");

            // 카메라 먼저 — 사용자에게 "이 캐릭터에 진입 중" 시각 큐.
            // 1초 블렌드가 진행되는 사이 채팅 패널이 슬라이드 인.
            _cameraFocus?.FocusOn(sessionId);

            // ChatPanelView 미장착 (테스트 씬 등) 시 silent skip.
            _chatPanel?.OpenForAgent(sessionId, name, role, rawRole);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
        }

        private void OnDestroy() => Dispose();
    }
}
