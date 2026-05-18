using System;
using UnityEngine;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// [Deprecated 2026-05-14] uGUI <c>AgentHUDController</c> 호환용 호버 라우터.
    /// AgentSpawner 가 더 이상 레거시 HUD 를 인스턴스화하지 않으므로 본 핸들러는 no-op 가 됨.
    /// UI Toolkit <see cref="OpenDesk.Presentation.UI.Hud.AgentHudView"/> 가
    /// <see cref="AgentPointerService.HoverChanged"/> 를 자체 구독한다.
    /// </summary>
    [Obsolete("AgentHudView 가 HoverChanged 를 직접 구독함. 본 컴포넌트는 더 이상 필요 없음 — 다음 PR 에서 제거 예정.", error: false)]
    public sealed class AgentHoverHandler : MonoBehaviour
    {
    }
}
