using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Models;
using R3;

namespace OpenDesk.Core.Services
{
    /// <summary>
    /// AI 제공업체 API 키 안전 저장소
    /// - 키는 OS 자격 증명 관리자에 암호화 저장 (Unity/PlayerPrefs에 저장하지 않음)
    /// - 14개+ AI 제공업체 지원
    /// - Ollama(로컬) 설정 시 API 키 없이도 무료 사용 가능
    /// </summary>
    public interface IApiKeyVaultService
    {
        /// <summary>지원하는 AI 제공업체 목록</summary>
        IReadOnlyList<ApiProvider> GetProviders();

        /// <summary>특정 제공업체의 키 저장 상태 조회</summary>
        ApiKeyEntry GetKeyStatus(string providerId);

        /// <summary>전체 키 상태 목록</summary>
        IReadOnlyList<ApiKeyEntry> GetAllKeyStatuses();

        /// <summary>
        /// API 키 저장 (OS 자격 증명 관리자에 암호화)
        /// 저장 후 자동 유효성 검증
        /// </summary>
        UniTask<ApiKeyStatus> SaveKeyAsync(string providerId, string apiKey, CancellationToken ct = default);

        /// <summary>저장된 키 복호화 조회 (내부 사용)</summary>
        UniTask<string> GetKeyAsync(string providerId, CancellationToken ct = default);

        /// <summary>API 키 유효성 검증 (실제 API 호출)</summary>
        UniTask<ApiKeyStatus> ValidateKeyAsync(string providerId, CancellationToken ct = default);

        /// <summary>키 삭제</summary>
        UniTask DeleteKeyAsync(string providerId, CancellationToken ct = default);

        /// <summary>키 변경 시 이벤트</summary>
        Observable<ApiKeyEntry> OnKeyChanged { get; }

        /// <summary>
        /// API 키 없이 사용 가능한지 확인
        /// Ollama 로컬 모델이 설정되어 있으면 true
        /// </summary>
        UniTask<bool> CanRunWithoutApiKeyAsync(CancellationToken ct = default);
    }
}
