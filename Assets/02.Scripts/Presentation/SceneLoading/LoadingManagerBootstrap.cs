using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace OpenDesk.Presentation.SceneLoading
{
    /// <summary>
    /// CoreInstaller 가 BuildContainer 한 직후 한 번 호출되어
    /// <see cref="LoadingManager"/> GameObject 를 코드로 생성하고 DI 주입한다.
    /// 이 부트스트랩 덕분에 사용자/씬에 prefab 부착 작업이 필요 없다.
    /// </summary>
    public sealed class LoadingManagerBootstrap : IInitializable
    {
        private readonly IObjectResolver _resolver;

        public LoadingManagerBootstrap(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public void Initialize()
        {
            var go = new GameObject("[LoadingManager]");
            Object.DontDestroyOnLoad(go);
            var mgr = go.AddComponent<LoadingManager>();
            // Awake 가 먼저 돌아 UI 트리를 빌드해두고, 그 뒤 Inject 가 IGameSceneLoader 를 주입.
            _resolver.Inject(mgr);
        }
    }
}
