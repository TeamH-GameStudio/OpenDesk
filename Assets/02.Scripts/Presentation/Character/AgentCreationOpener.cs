using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.AgentCreation.Persistence;
using OpenDesk.Core.Services;
using OpenDesk.Presentation.SceneLoading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace OpenDesk.Presentation.Character
{
    /// <summary>
    /// 오피스 씬 측 진입점. "동료 추가" CTA 가 이걸 호출한다.
    ///
    /// 흐름 (Single 모드):
    ///   OpenAsync()
    ///     → IGameSceneLoader.ChangeSceneAsync(AgentCreationScene)  // 로딩 오버레이 + 페이드
    ///     → 위저드 진행 → 저장
    ///     → AgentCreationCompletionRelay 가 sceneCount==1 분기로 AgentOfficeScene 재로드
    ///
    /// Single 전환이라 오피스 씬 상태는 휘발되지만, RosterBootstrapper 가 LoadAll 로
    /// 새 에이전트를 자연스럽게 픽업한다.
    ///
    /// 레거시 Additive 경로(<see cref="OpenAdditiveAsync"/>)는 메모리/가역성 보존 차원에서
    /// [Obsolete] 유지. 새 호출자는 사용 금지.
    /// </summary>
    public sealed class AgentCreationOpener : MonoBehaviour
    {
        [SerializeField] private string _sceneName = "AgentCreationScene";

        private IGameSceneLoader _sceneLoader;
        private IAgentCreationBridge _bridge; // 레거시 Additive 경로 전용 — Single 경로에선 미사용.
        private bool _isOpen;

        [Inject]
        public void Construct(IGameSceneLoader sceneLoader, IAgentCreationBridge bridge)
        {
            _sceneLoader = sceneLoader;
            _bridge = bridge;
        }

        public bool IsOpen => _isOpen;

        public void Open()
        {
            // Single 전환이라 Opener 자체가 OfficeScene 과 함께 곧 파괴된다.
            // GetCancellationTokenOnDestroy() 를 넘기면 ChangeSceneAsync 가 MinDisplayMs 딜레이
            // 도중에 취소돼서 LoadingManager 페이드 시퀀스가 정상 완료되지 않는다.
            // IGameSceneLoader 는 CoreInstaller 의 Singleton 으로 살아남으므로
            // Application 종료까지 유효한 토큰을 넘긴다.
            OpenAsync(Application.exitCancellationToken).Forget();
        }

        public async UniTask OpenAsync(CancellationToken ct)
        {
            if (_isOpen)
            {
                Debug.Log("[AgentCreationOpener] 이미 열려 있음 — 중복 호출 무시.");
                return;
            }
            if (_sceneLoader == null)
            {
                Debug.LogError("[AgentCreationOpener] IGameSceneLoader 미주입.");
                return;
            }
            if (string.IsNullOrEmpty(_sceneName))
            {
                Debug.LogError("[AgentCreationOpener] _sceneName 비어있음.");
                return;
            }

            _isOpen = true;
            try
            {
                Debug.Log($"[AgentCreationOpener] '{_sceneName}' Single 로 전환.");
                await _sceneLoader.ChangeSceneAsync(_sceneName, ct);
                // Single 전환 이후 Opener 자체가 파괴됨 — finally 블록의 _isOpen=false 는
                // 이미 새 씬이므로 도달하지 않을 수 있으나 도달해도 영향 없음.
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AgentCreationOpener] 취소됨.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AgentCreationOpener] 예외: {e}");
            }
            finally
            {
                _isOpen = false;
            }
        }

        /// <summary>
        /// 레거시 Additive 오버레이 경로. AgentCreationCompletionRelay 의 Additive 분기 +
        /// bridge.OfficeSetupCompleted 핸드셰이크에 의존. Single 모드 전환(<see cref="OpenAsync"/>)
        /// 도입 후 사용처 제거.
        /// </summary>
        [Obsolete("Use OpenAsync (Single mode). Additive overlay 경로는 더 이상 사용하지 않는다.")]
        public async UniTask OpenAdditiveAsync(CancellationToken ct)
        {
            if (_isOpen)
            {
                Debug.Log("[AgentCreationOpener] 이미 열려 있음 — 중복 호출 무시.");
                return;
            }
            if (_bridge == null)
            {
                Debug.LogError("[AgentCreationOpener] IAgentCreationBridge 미주입.");
                return;
            }

            _isOpen = true;
            Action<AgentDraftRecord, string> savedHandler = null;
            Action setupHandler = null;
            try
            {
                var loadOp = SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Additive);
                if (loadOp == null)
                {
                    Debug.LogError($"[AgentCreationOpener] 씬 로드 실패: {_sceneName} (Build Settings 확인).");
                    return;
                }
                await loadOp.ToUniTask(cancellationToken: ct);

                var setupTcs = new UniTaskCompletionSource();
                var savedTcs = new UniTaskCompletionSource();

                savedHandler = (rec, path) =>
                {
                    Debug.Log($"[AgentCreationOpener] AgentSaved 수신: {rec?.name} ({rec?.id})");
                    savedTcs.TrySetResult();
                };
                setupHandler = () =>
                {
                    Debug.Log("[AgentCreationOpener] OfficeSetupCompleted 수신 — unload 진행.");
                    setupTcs.TrySetResult();
                };

                _bridge.AgentSaved += savedHandler;
                _bridge.OfficeSetupCompleted += setupHandler;

                await savedTcs.Task.AttachExternalCancellation(ct);
                await setupTcs.Task.AttachExternalCancellation(ct);

                var scene = SceneManager.GetSceneByName(_sceneName);
                if (scene.IsValid() && scene.isLoaded)
                {
                    var unloadOp = SceneManager.UnloadSceneAsync(scene);
                    if (unloadOp != null)
                        await unloadOp.ToUniTask(cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AgentCreationOpener] 취소됨 — 정리 중.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AgentCreationOpener] 예외: {e}");
            }
            finally
            {
                if (_bridge != null)
                {
                    if (savedHandler != null) _bridge.AgentSaved -= savedHandler;
                    if (setupHandler != null) _bridge.OfficeSetupCompleted -= setupHandler;
                }
                _isOpen = false;
            }
        }
    }
}
