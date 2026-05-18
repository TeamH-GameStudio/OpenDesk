namespace OpenDesk.Presentation.Cameras
{
    /// <summary>
    /// 채팅 진입 시 특정 에이전트를 Cinemachine 카메라로 포커싱하고,
    /// 채팅 종료 시 오피스 overview 로 복귀시키는 서비스.
    ///
    /// 구현체(<see cref="CinemachineCameraFocusService"/>)는 씬에 배치된 CameraRig 의 두 vcam
    /// (OverviewVCam / FocusVCam) 우선순위를 swap 하는 방식. AgentClickHandler 가 클릭 시 호출하고,
    /// AgentOfficeInstaller 가 ChatPanelView.Closed 이벤트를 ReleaseFocus 로 와이어링한다.
    /// </summary>
    public interface ICameraFocusService
    {
        /// <summary>현재 포커스 중인지 여부.</summary>
        bool IsFocused { get; }

        /// <summary>현재 포커스 대상 SessionId (없으면 null).</summary>
        string CurrentSessionId { get; }

        /// <summary>
        /// 지정 SessionId 에 해당하는 캐릭터를 카메라로 포커싱.
        /// 같은 SessionId 재요청은 no-op (vcam blend 깜빡임 방지).
        /// 캐릭터가 spawn 되어 있지 않으면 warning 후 무시.
        /// </summary>
        void FocusOn(string sessionId);

        /// <summary>오피스 overview vcam 으로 복귀.</summary>
        void ReleaseFocus();
    }
}
