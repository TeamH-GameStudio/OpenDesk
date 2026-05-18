using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Models;

namespace OpenDesk.Onboarding.Services
{
    /// <summary>
    /// OnboardingShellView (UIDocument 보유 MonoBehaviour)에 공개되는 슬림 인터페이스.
    /// FlowController가 단방향으로 호출만 하기 위한 경계.
    /// </summary>
    public interface IOnboardingShellView
    {
        /// <summary>지정 스텝의 root만 활성화하고 나머지는 숨긴다.</summary>
        void ShowStep(OnboardingFlowStep step);

        /// <summary>로딩 스텝으로 즉시 전환하고 메시지를 표시한다 (씬 전환 인터스티셜용).</summary>
        void ShowLoadingMessage(string message);

        /// <summary>§6 로딩 화면을 띄우고 메시지 시퀀스(900/900/1200ms)를 실행한다. 완료까지 대기.</summary>
        UniTask RunLoadingAsync(string agentName, CancellationToken ct);
    }
}
