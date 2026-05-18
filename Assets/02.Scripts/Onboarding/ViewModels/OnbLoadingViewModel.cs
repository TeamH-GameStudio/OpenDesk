using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Onboarding.Common;

namespace OpenDesk.Onboarding.ViewModels
{
    /// <summary>
    /// §6 로딩 화면 ViewModel — 메시지 시퀀스(900/900/1200ms) 후 LoadingCompleted 발화.
    /// </summary>
    public sealed class OnbLoadingViewModel : ObservableObject
    {
        private const int StepMilliseconds = 900;
        private const int ArrivalMilliseconds = 1200;

        private static readonly string[] DefaultMessages =
        {
            "동료를 부르는 중...",
            "사무실을 정리하는 중...",
        };

        private string _currentMessage = string.Empty;
        public string CurrentMessage
        {
            get => _currentMessage;
            private set => SetField(ref _currentMessage, value);
        }

        private bool _isArrival;
        public bool IsArrival
        {
            get => _isArrival;
            private set => SetField(ref _isArrival, value);
        }

        public event Action LoadingCompleted;

        /// <summary>
        /// 외부에서 임의의 메시지로 로딩 화면을 갱신할 때 사용 (씬 전환 인터스티셜 등).
        /// </summary>
        public void SetMessage(string message)
        {
            CurrentMessage = message ?? string.Empty;
        }

        public async UniTask RunAsync(string agentName, CancellationToken ct = default)
        {
            IsArrival = false;

            foreach (var msg in DefaultMessages)
            {
                CurrentMessage = msg;
                await UniTask.Delay(StepMilliseconds, cancellationToken: ct);
            }

            var arrivalText = string.IsNullOrEmpty(agentName)
                ? "동료가 도착했어요"
                : $"{agentName}이(가) 도착했어요";

            CurrentMessage = arrivalText;
            IsArrival = true;
            await UniTask.Delay(ArrivalMilliseconds, cancellationToken: ct);

            LoadingCompleted?.Invoke();
        }
    }
}
