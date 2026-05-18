using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenDesk.Core.Services.Licensing
{
    /// <summary>
    /// 디바이스 고유 식별자 생성. macOS 는 IOPlatformUUID (ioreg), Windows 는
    /// WMI Win32_ComputerSystemProduct.UUID. SHA256 으로 해시 후 라이선스 활성화 시
    /// 백엔드에 전송하여 디바이스 수 제한을 검증한다.
    ///
    /// PlayerPrefs 캐시 키 `OpenDesk_License_Fingerprint` 를 우선 사용 — OS 호출 비용 절약 +
    /// IOPlatformUUID 자체 변경이 거의 없으므로 안전.
    /// </summary>
    public interface IDeviceFingerprintService
    {
        /// <summary>
        /// 디바이스 지문을 계산하거나 캐시에서 반환. 실패 시 generated GUID fallback 으로
        /// PlayerPrefs 에 보존되어 같은 설치본은 동일한 값을 유지한다.
        /// </summary>
        UniTask<string> GetFingerprintAsync(CancellationToken ct = default);

        /// <summary>
        /// 호스트가 인식할 수 있는 사람용 디바이스 이름 (예: "MacBook Pro of choi"). UI 표시용.
        /// </summary>
        string GetSuggestedDeviceName();
    }
}
