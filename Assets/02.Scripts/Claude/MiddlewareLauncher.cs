using System.Diagnostics;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenDesk.Claude
{
    /// <summary>
    /// 미들웨어 서버 자동 시작/종료.
    /// - 에디터: python server.py 직접 실행
    /// - 빌드: StreamingAssets/Middleware/Middleware.exe 실행
    /// </summary>
    public class MiddlewareLauncher : MonoBehaviour
    {
        [Header("설정")]
        [SerializeField] private bool   _autoLaunch   = true;
        [SerializeField] private float  _startupDelay = 2f;

        [Header("배포용 (빌드)")]
        [SerializeField] private string _middlewareDir = "Middleware";
        [SerializeField] private string _exeName       = "Middleware.exe";

#if UNITY_EDITOR
        [Header("개발용 (에디터)")]
        [SerializeField] private string _pythonPath = "";
        [SerializeField] private string _serverScript = "server.py";
#endif

        private Process _process;
        private CancellationTokenSource _cts;

        private void Start() => StartAsync().Forget();

        private async UniTask StartAsync()
        {
            if (!_autoLaunch)
            {
                Debug.Log("[MiddlewareLauncher] 자동 실행 비활성");
                return;
            }

            // API 백엔드 선택 시 미들웨어 불필요 — 스킵
            var backend = PlayerPrefs.GetString("OpenDesk_ChatBackend", "cli");
            if (backend == "api")
            {
                Debug.Log("[MiddlewareLauncher] AI 백엔드: api — Python 미들웨어 기동 스킵");
                return;
            }

            _cts = new CancellationTokenSource();

            try
            {
#if UNITY_EDITOR
                LaunchDevMode();
#else
                LaunchBuildMode();
#endif
                if (_process == null)
                {
                    Debug.LogWarning("[MiddlewareLauncher] 프로세스 시작 실패 (실행 파일 없음)");
                    return;
                }

                Debug.Log($"[MiddlewareLauncher] 미들웨어 시작: PID={_process.Id}");

                await UniTask.Delay(
                    (int)(_startupDelay * 1000),
                    cancellationToken: _cts.Token
                );
            }
            catch (System.OperationCanceledException) { /* 정상 취소 */ }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MiddlewareLauncher] 미들웨어 시작 실패: {ex.Message}");
            }
        }

#if UNITY_EDITOR
        private void LaunchDevMode()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var middlewarePath = Path.Combine(projectRoot, _middlewareDir);

            if (!File.Exists(Path.Combine(middlewarePath, _serverScript)))
            {
                Debug.LogWarning($"[MiddlewareLauncher] 서버 스크립트 없음: {middlewarePath}/{_serverScript}");
                return;
            }

            var pythonExe = ResolvePythonPath();
            Debug.Log($"[MiddlewareLauncher] Python 경로: {pythonExe}");

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName         = pythonExe,
                    Arguments        = _serverScript,
                    WorkingDirectory = middlewarePath,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                },
                EnableRaisingEvents = true,
            };

            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.Log($"[Middleware] {e.Data}");
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.Log($"[Middleware] {e.Data}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Debug.Log($"[MiddlewareLauncher] 개발 모드: python {_serverScript} (워킹디렉토리: {middlewarePath})");
        }
#endif

        private void LaunchBuildMode()
        {
            var basePath = Application.streamingAssetsPath;
            var exePath  = Path.Combine(basePath, _middlewareDir, _exeName);

            if (!File.Exists(exePath))
            {
                Debug.LogWarning($"[MiddlewareLauncher] 미들웨어 실행 파일 없음: {exePath}");
                return;
            }

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName         = exePath,
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    WorkingDirectory = Path.Combine(basePath, _middlewareDir),
                },
                EnableRaisingEvents = true,
            };

            _process.Start();
        }

        /// <summary>Python 실행 경로 탐색 — Inspector 지정 > 플랫폼별 일반 경로 > PATH 폴백</summary>
        private string ResolvePythonPath()
        {
            // 1) Inspector에서 직접 지정한 경우
            if (!string.IsNullOrEmpty(_pythonPath) && File.Exists(_pythonPath))
                return _pythonPath;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            // macOS: python3 우선 (system python은 macOS 12.3+에서 제거됨)
            var macCandidates = new[]
            {
                "/opt/homebrew/bin/python3",   // Apple Silicon Homebrew
                "/usr/local/bin/python3",      // Intel Homebrew
                "/usr/bin/python3",            // Xcode Command Line Tools
            };
            foreach (var p in macCandidates)
                if (File.Exists(p)) return p;

            Debug.LogWarning("[MiddlewareLauncher] macOS Python 경로를 찾을 수 없음 — 'python3' 로 시도");
            return "python3";
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            var linuxCandidates = new[] { "/usr/bin/python3", "/usr/local/bin/python3" };
            foreach (var p in linuxCandidates)
                if (File.Exists(p)) return p;
            return "python3";
#else
            // Windows
            // 2) WindowsApps python3.exe / python.exe 탐색
            var windowsApps = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps");

            var candidates = new[] { "python3.exe", "python.exe", "python3.12.exe" };
            foreach (var name in candidates)
            {
                var fullPath = Path.Combine(windowsApps, name);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // 3) 일반 설치 경로
            var programPaths = new[]
            {
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Python"),
                @"C:\Python312", @"C:\Python311", @"C:\Python310",
            };

            foreach (var basePath in programPaths)
            {
                if (!Directory.Exists(basePath)) continue;
                foreach (var dir in Directory.GetDirectories(basePath, "Python*"))
                {
                    var exe = Path.Combine(dir, "python.exe");
                    if (File.Exists(exe)) return exe;
                }
                var direct = Path.Combine(basePath, "python.exe");
                if (File.Exists(direct)) return direct;
            }

            // 4) 폴백: PATH에서 찾기를 기대
            Debug.LogWarning("[MiddlewareLauncher] Python 경로를 찾을 수 없음 — 'python' 으로 시도");
            return "python";
#endif
        }

        private void OnApplicationQuit()
        {
            KillProcess();
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            KillProcess();
        }

        private void KillProcess()
        {
            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    Debug.Log("[MiddlewareLauncher] 미들웨어 프로세스 종료");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MiddlewareLauncher] 프로세스 종료 실패: {ex.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }
}
