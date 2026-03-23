#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace OpenDesk.Editor
{
    /// <summary>
    /// 온보딩 플로우 디버그 창
    /// Window > OpenDesk > Onboarding Debug 에서 열기
    ///
    /// 기능:
    /// 1. Mock 모드로 전체 플로우 자동 시뮬레이션 (실제 설치 안 함)
    /// 2. 각 상태로 강제 전환 (UI 확인용)
    /// 3. PlayerPrefs 초기화
    /// </summary>
    public class OnboardingDebugWindow : EditorWindow
    {
        private static readonly string[] StateNames =
        {
            "Init",
            "CheckingFirstRun",
            "ScanningEnvironment",
            "InstallingNodeJs",
            "NodeJsFailed",
            "CheckingWsl2",
            "InstallingWsl2",
            "Wsl2NeedsReboot",
            "DetectingOpenClaw",
            "OpenClawNotFound",
            "InstallingOpenClaw",
            "InstallFailed",
            "ConnectingGateway",
            "GatewayFailed",
            "WaitingForManualUrl",
            "ParsingAgents",
            "NoAgentsFound",
            "WorkspaceSetup",
            "ReadyToEnter",
            "Completed",
            "FatalError",
        };

        private int _selectedState;
        private bool _useMockMode = true;
        private Vector2 _scrollPos;

        [MenuItem("Window/OpenDesk/Onboarding Debug")]
        public static void ShowWindow()
        {
            var window = GetWindow<OnboardingDebugWindow>("Onboarding Debug");
            window.minSize = new Vector2(350, 500);
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // ════════════════════════════════════════════
            //  섹션 1: 테스트 모드 설정
            // ════════════════════════════════════════════
            EditorGUILayout.LabelField("테스트 모드", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Mock 모드를 켜면 실제 Node.js/WSL2/OpenClaw 설치 없이\n" +
                "온보딩 UI 플로우를 테스트할 수 있습니다.",
                MessageType.Info);

            _useMockMode = EditorGUILayout.Toggle("Mock 모드 사용", _useMockMode);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("▶  Mock 모드로 Play 시작", GUILayout.Height(35)))
            {
                if (_useMockMode)
                    EnableMockMode();
                else
                    DisableMockMode();

                ResetPlayerPrefs();
                EditorApplication.isPlaying = true;
            }

            EditorGUILayout.Space(15);

            // ════════════════════════════════════════════
            //  섹션 2: PlayerPrefs 관리
            // ════════════════════════════════════════════
            EditorGUILayout.LabelField("PlayerPrefs 관리", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("초기화 (최초실행 상태로)", GUILayout.Height(28)))
            {
                ResetPlayerPrefs();
                Debug.Log("[Debug] PlayerPrefs 초기화 완료 — 다음 Play 시 온보딩 처음부터 시작");
            }
            if (GUILayout.Button("완료 상태로 설정", GUILayout.Height(28)))
            {
                PlayerPrefs.SetInt("OpenDesk_IsFirstRun", 0);
                PlayerPrefs.SetString("OpenDesk_GatewayUrl", "ws://localhost:18789/events");
                PlayerPrefs.Save();
                Debug.Log("[Debug] PlayerPrefs 완료 상태 설정 — 다음 Play 시 온보딩 건너뜀");
            }
            EditorGUILayout.EndHorizontal();

            // 현재 PlayerPrefs 상태 표시
            EditorGUILayout.Space(5);
            var isFirstRun = PlayerPrefs.GetInt("OpenDesk_IsFirstRun", 1);
            var gatewayUrl = PlayerPrefs.GetString("OpenDesk_GatewayUrl", "(없음)");
            var rebootPending = PlayerPrefs.GetInt("OpenDesk_RebootPending", 0);
            var officeSetup = PlayerPrefs.GetInt("OpenDesk_OfficeSetupDone", 0);
            EditorGUILayout.LabelField($"  IsFirstRun: {(isFirstRun == 1 ? "true (온보딩 실행됨)" : "false (건너뜀)")}");
            EditorGUILayout.LabelField($"  GatewayUrl: {gatewayUrl}");
            EditorGUILayout.LabelField($"  RebootPending: {(rebootPending == 1 ? "true" : "false")}");
            EditorGUILayout.LabelField($"  OfficeSetupDone: {(officeSetup == 1 ? "true (마법사 완료)" : "false (마법사 표시됨)")}");


            EditorGUILayout.Space(15);

            // ════════════════════════════════════════════
            //  섹션 3: 상태 강제 전환 (Play 모드 전용)
            // ════════════════════════════════════════════
            EditorGUILayout.LabelField("상태 강제 전환 (Play 모드)", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 모드에서만 사용 가능합니다.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "각 버튼을 클릭하면 해당 상태의 UI를 즉시 확인할 수 있습니다.\n" +
                    "설명 텍스트, 버튼 표시, 진행바 등을 확인하세요.",
                    MessageType.Info);

                _selectedState = EditorGUILayout.Popup("대상 상태", _selectedState, StateNames);

                if (GUILayout.Button($"→  '{StateNames[_selectedState]}' 상태로 전환", GUILayout.Height(30)))
                {
                    ForceTransition(_selectedState);
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("빠른 전환", EditorStyles.miniLabel);

                // 주요 상태 빠른 버튼
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("환경스캔")) ForceTransition(2);
                if (GUILayout.Button("Node설치")) ForceTransition(3);
                if (GUILayout.Button("Node실패")) ForceTransition(4);
                if (GUILayout.Button("WSL2설치")) ForceTransition(6);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("재시작필요")) ForceTransition(7);
                if (GUILayout.Button("AI설치중")) ForceTransition(10);
                if (GUILayout.Button("설치실패")) ForceTransition(11);
                if (GUILayout.Button("연결중")) ForceTransition(12);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("연결실패")) ForceTransition(13);
                if (GUILayout.Button("폴더선택")) ForceTransition(17);
                if (GUILayout.Button("완료!")) ForceTransition(18);
                if (GUILayout.Button("에러")) ForceTransition(20);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(15);

            // ════════════════════════════════════════════
            //  섹션 4: 설치 기록 & 롤백
            // ════════════════════════════════════════════
            EditorGUILayout.LabelField("설치 기록 & 롤백", EditorStyles.boldLabel);

            var recordPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "OpenDesk", "installation_record.json");

            if (System.IO.File.Exists(recordPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(recordPath);
                    var record = JsonUtility.FromJson<OpenDesk.Onboarding.Models.InstallationRecord>(json);
                    if (record != null && record.Items.Count > 0)
                    {
                        EditorGUILayout.HelpBox($"OpenDesk가 설치한 항목 {record.Items.Count}개", MessageType.Info);
                        foreach (var item in record.Items)
                        {
                            var status = item.RolledBack ? " [롤백됨]" : "";
                            EditorGUILayout.LabelField($"  {item.DisplayName}: {item.PreviousState} → {item.InstalledState}{status}");
                        }

                        EditorGUILayout.Space(5);
                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("전체 롤백 (설치한 모든 것 제거)", GUILayout.Height(30)))
                        {
                            if (EditorUtility.DisplayDialog("전체 롤백",
                                "OpenDesk가 설치한 모든 항목을 제거합니다.\n정말 진행하시겠습니까?",
                                "롤백 실행", "취소"))
                            {
                                RollbackAll(recordPath);
                            }
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        EditorGUILayout.LabelField("  설치 기록 없음");
                    }
                }
                catch
                {
                    EditorGUILayout.LabelField("  기록 파일 읽기 실패");
                }
            }
            else
            {
                EditorGUILayout.LabelField("  설치 기록 없음 (아직 실제 설치를 진행하지 않음)");
            }

            EditorGUILayout.Space(15);

            // ════════════════════════════════════════════
            //  섹션 5: 환경 상태 확인
            // ════════════════════════════════════════════
            EditorGUILayout.LabelField("외부 환경 상태", EditorStyles.boldLabel);

            if (GUILayout.Button("Node.js / WSL2 / OpenClaw 상태 확인", GUILayout.Height(28)))
            {
                CheckEnvironment();
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Mock 모드 ──────────────────────────────────────

        private static void EnableMockMode()
        {
            // 스크립팅 심볼로 Mock 모드 활성화
            PlayerPrefs.SetInt("OpenDesk_MockMode", 1);
            PlayerPrefs.Save();
            Debug.Log("[Debug] Mock 모드 활성화 — 실제 설치 명령 실행하지 않음");
        }

        private static void DisableMockMode()
        {
            PlayerPrefs.DeleteKey("OpenDesk_MockMode");
            PlayerPrefs.Save();
            Debug.Log("[Debug] Mock 모드 비활성화 — 실제 명령 실행됨");
        }

        private static void ResetPlayerPrefs()
        {
            PlayerPrefs.DeleteKey("OpenDesk_IsFirstRun");
            PlayerPrefs.DeleteKey("OpenDesk_GatewayUrl");
            PlayerPrefs.DeleteKey("OpenDesk_LocalPath");
            PlayerPrefs.DeleteKey("OpenDesk_AppVersion");
            PlayerPrefs.DeleteKey("OpenDesk_RebootPending");
            PlayerPrefs.DeleteKey("OpenDesk_OfficeSetupDone");
            PlayerPrefs.Save();
            Debug.Log("[Debug] OpenDesk PlayerPrefs 모두 초기화 완료 (온보딩 + Office 마법사)");
        }

        // ── 상태 강제 전환 ─────────────────────────────────

        private static void ForceTransition(int stateIndex)
        {
            // OnboardingService의 _state를 리플렉션으로 강제 변경
            var serviceType = System.Type.GetType(
                "OpenDesk.Onboarding.Implementations.OnboardingService, Assembly-CSharp");
            var stateEnumType = System.Type.GetType(
                "OpenDesk.Onboarding.Models.OnboardingState, Assembly-CSharp");

            if (serviceType == null || stateEnumType == null)
            {
                Debug.LogError("[Debug] OnboardingService 또는 OnboardingState 타입을 찾을 수 없습니다. 컴파일을 확인하세요.");
                return;
            }

            // VContainer에서 서비스 찾기 — MonoBehaviour가 아니므로 직접 검색 불가
            // 대신 OnboardingUIController를 찾아서 _onboarding 필드 접근
            var ctrlType = System.Type.GetType(
                "OpenDesk.Presentation.UI.Onboarding.OnboardingUIController, Assembly-CSharp");
            if (ctrlType == null)
            {
                Debug.LogError("[Debug] OnboardingUIController 타입을 찾을 수 없습니다.");
                return;
            }

            var ctrl = Object.FindObjectOfType(ctrlType);
            if (ctrl == null)
            {
                Debug.LogError("[Debug] 씬에 OnboardingUIController가 없습니다. Onboarding 씬에서 실행하세요.");
                return;
            }

            // _onboarding 필드 접근
            var onbField = ctrlType.GetField("_onboarding",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (onbField == null)
            {
                Debug.LogError("[Debug] _onboarding 필드를 찾을 수 없습니다.");
                return;
            }

            var onbService = onbField.GetValue(ctrl);
            if (onbService == null)
            {
                Debug.LogWarning("[Debug] _onboarding이 null — VContainer 주입이 안 됐을 수 있습니다. UI만 직접 갱신합니다.");

                // VContainer 없이 UI만 직접 테스트: OnStateChanged 직접 호출
                var onStateChanged = ctrlType.GetMethod("OnStateChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (onStateChanged != null)
                {
                    var stateValue = System.Enum.ToObject(stateEnumType, stateIndex);
                    onStateChanged.Invoke(ctrl, new[] { stateValue });
                    Debug.Log($"[Debug] UI 직접 전환 → {StateNames[stateIndex]}");
                }
                return;
            }

            // _state ReactiveProperty에 직접 값 설정
            var stateField = serviceType.GetField("_state",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (stateField != null)
            {
                var reactiveProperty = stateField.GetValue(onbService);
                var valueProp = reactiveProperty.GetType().GetProperty("Value");
                var enumValue = System.Enum.ToObject(stateEnumType, stateIndex);
                valueProp.SetValue(reactiveProperty, enumValue);
                Debug.Log($"[Debug] 상태 전환 → {StateNames[stateIndex]}");
            }
        }

        // ── 롤백 ───────────────────────────────────────────

        private static void RollbackAll(string recordPath)
        {
            // Play 모드가 아니면 에디터에서 직접 RollbackService 생성하여 실행
            var svcType = System.Type.GetType(
                "OpenDesk.Onboarding.Implementations.RollbackService, Assembly-CSharp");
            if (svcType == null)
            {
                Debug.LogError("[Debug] RollbackService 타입 미발견");
                return;
            }

            var svc = System.Activator.CreateInstance(svcType);
            var method = svcType.GetMethod("RollbackAllAsync");
            if (method == null)
            {
                Debug.LogError("[Debug] RollbackAllAsync 메서드 미발견");
                return;
            }

            Debug.Log("[Debug] 전체 롤백 시작...");
            // UniTask이므로 동기 실행은 어려움 — Play 모드에서만 사용 권장
            if (Application.isPlaying)
            {
                method.Invoke(svc, new object[] { System.Threading.CancellationToken.None });
            }
            else
            {
                Debug.Log("[Debug] 에디터 모드에서는 Play 상태에서 롤백을 실행해주세요.");
            }
        }

        // ── 환경 확인 ──────────────────────────────────────

        private static void CheckEnvironment()
        {
            Debug.Log("=== 외부 환경 상태 확인 ===");

            // Node.js
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "node", Arguments = "--version",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                var ver = p?.StandardOutput.ReadToEnd().Trim();
                p?.WaitForExit(5000);
                Debug.Log($"  Node.js: {(string.IsNullOrEmpty(ver) ? "미설치" : ver)}");
            }
            catch { Debug.Log("  Node.js: 미설치"); }

            // WSL2
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wsl", Arguments = "--status",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);
                Debug.Log($"  WSL2: {(p?.ExitCode == 0 ? "활성화됨" : "미설치")}");
            }
            catch { Debug.Log("  WSL2: 미설치"); }

            // OpenClaw
            var configPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "openclaw", "openclaw.json");
            Debug.Log($"  OpenClaw 설정: {(System.IO.File.Exists(configPath) ? "발견" : "미발견")} ({configPath})");

            // 포트 18789
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var task = tcp.ConnectAsync("127.0.0.1", 18789);
                task.Wait(500);
                Debug.Log($"  Gateway (18789): {(tcp.Connected ? "열림" : "닫힘")}");
            }
            catch { Debug.Log("  Gateway (18789): 닫힘"); }

            Debug.Log("=== 확인 완료 ===");
        }
    }
}
#endif
