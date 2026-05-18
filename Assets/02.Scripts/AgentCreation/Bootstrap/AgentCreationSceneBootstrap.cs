using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenDesk.AgentCreation.Bootstrap
{
    /// <summary>
    /// AgentCreationScene 이 Additive 로드될 때 자기 씬의 MainCamera/AudioListener 를 비활성화한다.
    /// 부모 씬(오피스) 의 카메라/리스너와 충돌해서:
    ///   - Camera.main 비결정성 → HUD 빌보드 오작동
    ///   - AudioListener 2개 → Unity 경고
    ///   - 화면 출력 카메라 2개 → 분할/덮어쓰기
    /// 가 발생하는 것을 방지.
    ///
    /// Single 로드(온보딩 첫 흐름)일 때는 자기 씬이 유일하므로 그대로 살린다.
    /// 와드로브 프리뷰용 RenderTexture 카메라(_previewCamera)는 항상 유지 — 별개 카메라이고
    /// 출력이 Display 가 아닌 RenderTexture 라 충돌 없음.
    /// </summary>
    public sealed class AgentCreationSceneBootstrap : MonoBehaviour
    {
        [Tooltip("위저드 씬의 메인 카메라 — Additive 로드 시 비활성될 대상.")]
        [SerializeField] private Camera _mainCamera;

        [Tooltip("위저드 씬의 메인 AudioListener — Additive 로드 시 비활성될 대상.")]
        [SerializeField] private AudioListener _mainAudioListener;

        private void Awake()
        {
            bool isAdditive = SceneManager.sceneCount > 1;
            if (!isAdditive) return;

            if (_mainCamera != null)
            {
                _mainCamera.gameObject.SetActive(false);
                Debug.Log("[AgentCreationSceneBootstrap] Additive 로드 — MainCamera 비활성.");
            }
            if (_mainAudioListener != null)
            {
                _mainAudioListener.enabled = false;
                Debug.Log("[AgentCreationSceneBootstrap] Additive 로드 — AudioListener 비활성.");
            }
        }
    }
}
