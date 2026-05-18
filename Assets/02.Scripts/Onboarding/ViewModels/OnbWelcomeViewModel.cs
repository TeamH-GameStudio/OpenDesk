using System;
using OpenDesk.Onboarding.Common;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §1 환영 화면 ViewModel — CTA "시작하기" 클릭만 처리.
    /// </summary>
    public sealed class OnbWelcomeViewModel : ObservableObject
    {
        public event Action StartRequested;

        public void Start() => StartRequested?.Invoke();
    }
}
