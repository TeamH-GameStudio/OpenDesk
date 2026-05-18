using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.Core.Services.Auth;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Presentation.UI.Auth
{
    /// <summary>
    /// 씬 시작 시점에 인증 상태를 점검하고, 미인증이면 AnthropicAuthModal 을 한 번 띄운다.
    /// IInitializable 로 등록되어 VContainer 가 컨테이너 빌드 직후 자동 호출.
    /// </summary>
    public sealed class AuthEntryGuard : IInitializable
    {
        private readonly IAnthropicCredentialService _credentials;
        private readonly AnthropicAuthModal _modal;

        public AuthEntryGuard(IAnthropicCredentialService credentials, AnthropicAuthModal modal = null)
        {
            _credentials = credentials;
            _modal = modal;
        }

        public void Initialize()
        {
            if (_credentials == null)
            {
                Debug.Log("[AuthEntryGuard] credential service 없음 — 가드 비활성");
                return;
            }
            if (_credentials.IsAuthenticated)
            {
                Debug.Log("[AuthEntryGuard] 인증 OK — 모달 스킵");
                return;
            }
            if (_modal == null)
            {
                Debug.LogWarning("[AuthEntryGuard] AnthropicAuthModal 미배치 — 인증 미완 상태로 진행");
                return;
            }

            // 씬 첫 프레임에 모달이 자기 OnEnable 을 마치도록 한 프레임 양보 후 호출.
            ShowAsync().Forget();
        }

        private async UniTaskVoid ShowAsync()
        {
            await UniTask.Yield();
            await _modal.AskAsync();
        }
    }
}
