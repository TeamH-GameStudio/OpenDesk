using System.Diagnostics;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenDesk.Core;
using OpenDesk.Core.Services.Auth;
using UnityEngine;
using VContainer;
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
        private IAnthropicCredentialService _credentials;

        [Inject]
        public void Construct(IAnthropicCredentialService credentials = null)
        {
            _credentials = credentials;
        }

        private void Start() => StartAsync().Forget();

        private async UniTask StartAsync()
        {
            if (!_autoLaunch)
            {
                Debug.Log("[MiddlewareLauncher] 자동 실행 비활성");
                return;
            }

            // 양쪽 provider(anthropic_cli / anthropic_api) 모두 미들웨어를 경유한다.
            // 백엔드 분기 분기 제거 — 통합 게이트웨이 패턴.
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
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };
            // Python 측에 강제 UTF-8 stdout 환경변수 (Python 3.7+).
            _process.StartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            _process.StartInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

            // Claude CLI 격리 — 글로벌 ~/.claude/ 가 아닌 OpenDesk 전용 디렉토리 사용.
            ApplyClaudeIsolation(_process.StartInfo);

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

            ApplyClaudeIsolation(_process.StartInfo);

            _process.Start();
        }

        /// <summary>
        /// Claude Code CLI 의 글로벌 설정(~/.claude/)을 건드리지 않도록 OpenDesk 전용
        /// 격리 디렉토리(OpenDeskPaths.ClaudeConfigDir) 를 CLAUDE_CONFIG_DIR 환경변수로 주입한다.
        /// 자식 subprocess(Claude CLI) 가 이 환경변수를 상속해 격리된 settings/credentials/sessions 를 사용한다.
        ///
        /// 추가로 사용자가 OpenDesk 안에서 직접 입력한 ANTHROPIC_API_KEY 가 있으면 환경변수로 주입한다.
        /// API Key 가 있으면 anthropic_api provider 즉시 동작 + anthropic_cli 도 OAuth 토큰 없이 동작.
        /// </summary>
        private void ApplyClaudeIsolation(ProcessStartInfo info)
        {
            var dir = OpenDeskPaths.ClaudeConfigDir;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MiddlewareLauncher] CLAUDE_CONFIG_DIR 생성 실패: {dir} ({ex.Message})");
            }
            info.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = dir;
            info.EnvironmentVariables["OPENDESK_BASE_DIR"] = OpenDeskPaths.Base;

            var apiKey = ReadApiKeyBlocking();
            if (!string.IsNullOrEmpty(apiKey))
            {
                info.EnvironmentVariables["ANTHROPIC_API_KEY"] = apiKey;
                Debug.Log($"[MiddlewareLauncher] Claude 격리 + ANTHROPIC_API_KEY 주입 (키 {apiKey.Length}자): CLAUDE_CONFIG_DIR={dir}");
            }
            else
            {
                Debug.Log($"[MiddlewareLauncher] Claude CLI 격리 활성: CLAUDE_CONFIG_DIR={dir}");
            }
        }

        /// <summary>UniTask 를 동기 컨텍스트에서 짧게 await — Inspector 의 IAnthropicCredentialService 가 비주입이면 null.</summary>
        private string ReadApiKeyBlocking()
        {
            if (_credentials == null) return null;
            try
            {
                // 파일 IO 한 번이라 매우 짧음. UniTask 를 동기 wait 로 변환.
                return _credentials.GetApiKeyAsync().GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[MiddlewareLauncher] API Key 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Python 실행 경로 탐색 우선순위:
        /// 1) Inspector 지정 경로
        /// 2) Middleware/.venv (개발 시 의존성 격리 — 권장)
        /// 3) 플랫폼별 시스템 Python
        /// 4) PATH 폴백
        /// </summary>
        private string ResolvePythonPath()
        {
            // 1) Inspector에서 직접 지정한 경우
            if (!string.IsNullOrEmpty(_pythonPath) && File.Exists(_pythonPath))
                return _pythonPath;

            // 2) Middleware/.venv 우선 — 개발 시 의존성 격리
            var venvPython = TryResolveVenvPython();
            if (!string.IsNullOrEmpty(venvPython))
            {
                Debug.Log($"[MiddlewareLauncher] venv Python 사용: {venvPython}");
                return venvPython;
            }

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

            Debug.LogWarning("[MiddlewareLauncher] macOS Python 경로를 찾을 수 없음 — 'python3' 로 시도. " +
                             "권장: cd Middleware && python3 -m venv .venv && .venv/bin/pip install -r requirements.txt");
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

        /// <summary>
        /// Middleware/.venv 의 Python 인터프리터 경로 반환. 없으면 null.
        /// 플랫폼별 표준 venv 레이아웃을 따른다.
        /// </summary>
        private string TryResolveVenvPython()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var venvRoot = Path.Combine(projectRoot, _middlewareDir, ".venv");
            if (!Directory.Exists(venvRoot)) return null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var winPython = Path.Combine(venvRoot, "Scripts", "python.exe");
            if (File.Exists(winPython)) return winPython;
#else
            var nixCandidates = new[]
            {
                Path.Combine(venvRoot, "bin", "python3"),
                Path.Combine(venvRoot, "bin", "python"),
            };
            foreach (var p in nixCandidates)
                if (File.Exists(p)) return p;
#endif
            return null;
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
