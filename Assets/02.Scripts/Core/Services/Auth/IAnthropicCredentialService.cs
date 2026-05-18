using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace OpenDesk.Core.Services.Auth
{
    /// <summary>
    /// Claude / Anthropic 인증 자격증명 저장소.
    /// API Key 와 OAuth 토큰을 모두 같은 백엔드에 저장하지만, 호출부는 각각 다르게 사용한다.
    /// 글로벌 ~/.claude/ 와 분리된 OpenDesk 격리 디렉토리(OpenDeskPaths.ClaudeConfigDir) 가 OAuth 토큰 저장소 역할을 하며,
    /// 본 서비스는 사용자가 OpenDesk 안에서 직접 입력한 API Key 를 보관한다.
    /// </summary>
    public interface IAnthropicCredentialService
    {
        /// <summary>API Key 또는 OAuth 토큰이 변경되었음을 알리는 이벤트.</summary>
        Observable<AuthCredentialChange> OnChanged { get; }

        UniTask<string> GetApiKeyAsync(CancellationToken ct = default);

        UniTask SetApiKeyAsync(string apiKey, CancellationToken ct = default);

        UniTask DeleteApiKeyAsync(CancellationToken ct = default);

        /// <summary>API Key 가 비어있지 않은지 빠른 동기 확인 (UI 가드용).</summary>
        bool HasApiKeyCached { get; }

        /// <summary>OAuth 토큰이 격리 디렉토리에 존재하는지 검사 (Claude CLI login 결과).</summary>
        bool HasOAuthTokens { get; }

        /// <summary>API Key + OAuth 둘 중 하나라도 활성이면 인증된 것으로 간주.</summary>
        bool IsAuthenticated { get; }
    }

    public enum AuthCredentialChange
    {
        ApiKeySet,
        ApiKeyDeleted,
        OAuthTokensChanged,
    }
}
