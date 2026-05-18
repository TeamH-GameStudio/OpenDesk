using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.SceneLoading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace OpenDesk.AgentCreation.Persistence
{
    /// <summary>
    /// 위저드 씬 측. AgentDraftSaveTrigger.Saved 를 받아:
    ///   - Bridge 로 record 발행 → 오피스 측이 스폰
    ///   - Single 진입(온보딩 첫 흐름)이면 직접 AgentOfficeScene Single 로드
    ///   - Additive 진입(오피스에서 + 동료 추가)이면 Unload 책임은 AgentCreationOpener 에 위임
    ///
    /// Single/Additive 분기는 SceneManager.sceneCount 로 판정.
    ///   sceneCount == 1 → 위저드만 로드된 상태 = Single 진입 → 오피스 씬으로 전환 필요
    ///   sceneCount  > 1 → 다른 씬과 함께 로드 = Additive 진입 → 그대로 둠
    /// </summary>
    public sealed class AgentCreationCompletionRelay : MonoBehaviour
    {
        [SerializeField] private AgentDraftSaveTrigger _saveTrigger;
        [SerializeField] private string _officeSceneName = "AgentOfficeScene_Moon";

        private IAgentCreationBridge _bridge;
        private IGameSceneLoader _sceneLoader;
        private bool _completed;

        [Inject]
        public void Construct(IAgentCreationBridge bridge, IGameSceneLoader sceneLoader)
        {
            _bridge = bridge;
            _sceneLoader = sceneLoader;
        }

        private void OnEnable()
        {
            // 인스펙터에서 명시 연결 안 된 경우 같은 GameObject 에서 자동 획득 —
            // RequireComponent 로 SaveTrigger 와 Relay 가 항상 함께 부착되므로 신뢰 가능.
            if (_saveTrigger == null)
                _saveTrigger = GetComponent<AgentDraftSaveTrigger>();

            if (_saveTrigger == null)
            {
                Debug.LogError("[AgentCreationCompletionRelay] AgentDraftSaveTrigger reference missing — 같은 GameObject 에 SaveTrigger 가 부착되어 있어야 합니다.");
                return;
            }
#pragma warning disable CS0618 // Saved 는 후방 호환 — Step 5 에서 IAgentRepository 구독으로 이전 예정.
            _saveTrigger.Saved += OnDraftSaved;
#pragma warning restore CS0618
        }

        private void OnDisable()
        {
#pragma warning disable CS0618
            if (_saveTrigger != null)
                _saveTrigger.Saved -= OnDraftSaved;
#pragma warning restore CS0618
        }

        private void OnDraftSaved(AgentDraftRecord record, string path)
        {
            if (_completed) return;
            _completed = true;

            if (_bridge == null)
            {
                Debug.LogError("[AgentCreationCompletionRelay] IAgentCreationBridge 미주입.");
                return;
            }

            bool isAdditive = SceneManager.sceneCount > 1;

            if (isAdditive)
            {
                // 오피스가 부모 씬으로 살아있음 → 그쪽이 RosterBootstrapper 로 record 받아 처리.
                // Unload 는 AgentCreationOpener 가 OfficeSetupCompleted 를 받은 뒤 수행.
                _bridge.RaiseAgentSaved(record, path);
            }
            else
            {
                // Single 진입 — 온보딩 첫 흐름. 오피스 씬을 직접 Single 로 로드.
                // 오피스 측 RosterBootstrapper.Start 가 LoadAll 으로 방금 저장된 record 를 자연스럽게 픽업.
                LoadOfficeAsync(record, path, this.GetCancellationTokenOnDestroy()).Forget();
            }
        }

        private async UniTaskVoid LoadOfficeAsync(AgentDraftRecord record, string path, CancellationToken ct)
        {
            try
            {
                // 한 프레임 yield — 위저드 UI 의 마지막 페인트가 끝나도록.
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);

                if (_sceneLoader == null)
                {
                    Debug.LogError("[AgentCreationCompletionRelay] IGameSceneLoader 미주입.");
                    return;
                }
                await _sceneLoader.ChangeSceneAsync(_officeSceneName, ct);
                // Single 전환이라 _bridge 발행 없이도 RosterBootstrapper.Start 가 LoadAll 로 픽업.
            }
            catch (OperationCanceledException) { /* 무시 */ }
            catch (Exception e)
            {
                Debug.LogError($"[AgentCreationCompletionRelay] LoadOffice 실패: {e}");
            }
        }
    }
}
