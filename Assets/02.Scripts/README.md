# OpenDesk — 코어 로직 파일 구조

## 📦 필수 패키지 (Package Manager에서 설치)

| 패키지 | 설치 방법 |
|--------|----------|
| VContainer | `https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer` |
| UniTask | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` |
| R3 | `https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity` |
| NativeWebSocket | `https://github.com/endel/NativeWebSocket.git#upm` |
| Google Drive API | NuGet (GoogleApis.Drive.v3) |

## 📁 Unity 프로젝트 내 파일 위치

```
Assets/
└── 02.Scripts/
    └── Core/
        ├── Models/
        │   ├── AgentActionType.cs
        │   ├── AgentEvent.cs
        │   ├── SubAgentStatus.cs
        │   └── WorkspaceEntry.cs
        ├── Services/          ← 인터페이스
        │   ├── IOpenClawBridgeService.cs
        │   ├── IEventParserService.cs
        │   ├── IAgentStateService.cs
        │   ├── ISubAgentService.cs
        │   ├── IWorkspaceService.cs
        │   └── IGoogleDriveService.cs
        ├── Implementations/   ← 구현체
        │   ├── OpenClawBridgeService.cs
        │   ├── EventParserService.cs
        │   ├── AgentStateService.cs
        │   ├── SubAgentService.cs
        │   ├── WorkspaceService.cs
        │   └── GoogleDriveService.cs
        └── Installers/
            ├── CoreInstaller.cs     ← LifetimeScope (씬에 컴포넌트로 추가)
            └── AppBootstrapper.cs   ← 앱 시작 시 자동 실행

Assets/
└── Tests/
    └── EditMode/
        ├── EventParserServiceTests.cs
        ├── AgentStateServiceTests.cs
        └── SubAgentServiceTests.cs
```

## ⚙️ 씬 설정

1. 빈 GameObject 생성 → `CoreInstaller` 컴포넌트 추가
2. VContainer가 자동으로 `AppBootstrapper.Start()` 호출
3. `PlayerPrefs`에 `OpenDesk_GatewayUrl` 없으면 `ws://localhost:18789/events` 사용

## 🔑 Google Drive 설정

1. Google Cloud Console에서 OAuth 2.0 클라이언트 ID 생성
2. `client_secret.json` 다운로드
3. `Assets/StreamingAssets/client_secret.json` 에 배치

## 🧪 테스트 실행

Unity 메뉴 → Window → General → Test Runner → EditMode → Run All
