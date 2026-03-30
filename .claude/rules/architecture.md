---
paths:
  - "Assets/02.Scripts/**"
---

# OpenDesk 아키텍처

## 스크립트 폴더 구조

```
Assets/02.Scripts/
├── Core/
│   ├── Models/           — AgentActionType, AgentEvent, ApiProvider, AuditResult, ChannelConfig, ConsoleLogEntry, SkillEntry
│   ├── Services/         — I*Service 인터페이스 (Bridge, Parser, State, Cost, Console, Channel, Router, Vault, Security, Skill)
│   ├── Implementations/  — 서비스 구현체
│   └── Installers/       — CoreInstaller (DI), AppBootstrapper (진입점)
├── Onboarding/
│   ├── Models/           — OnboardingState, OnboardingContext, InstallationRecord
│   ├── Services/         — IOnboardingService, INodeEnvironment, IWsl2, IAdminPrivilege, IRollback, IOpenClawDetector
│   ├── Implementations/  — 서비스 구현체
│   └── Installers/       — OnboardingInstaller, OnboardingBootstrapper
├── AgentCreation/
│   ├── Models/           — AgentRole, AgentAIModel, AgentTone, AgentCreationData/Step, AgentProfileSO, AgentDataStore, AgentSession, AgentSessionStore, ChatMessageStore
│   └── Installers/       — AgentCreationInstaller, AgentOfficeInstaller
├── Presentation/
│   ├── Character/        — AgentSpawner, AgentHUDController, AgentOfficeBootstrapper, OrbitCamera, AgentClickHandler
│   ├── UI/
│   │   ├── AgentCreation/ — AgentCreationWizardController
│   │   ├── Session/       — SessionListController, ChatPanelController
│   │   ├── Panels/        — Terminal, Tab, Channels, ApiKeys, Routing, Skills, Security, Settings
│   │   ├── TopBar/        — TopBarController
│   │   ├── Onboarding/    — OnboardingUIController
│   │   └── Modals/        — ModalDialogController
│   ├── Dashboard/        — AgenticLoopVisualizer, ConsoleLogController, CostHudController
│   └── SceneLoading/     — LoadingSceneController
├── Claude/               — ClaudeWebSocketClient, ClaudeChatManager, MiddlewareLauncher, ClaudeChatProtocol
└── Editor/               — SceneGenerator, SceneFixer, PrefabGenerator, ScenePatchers, DebugWindows
```

## VContainer DI 계층

```
CoreInstaller (DontDestroyOnLoad)
  ├── 모든 Core 서비스 (Bridge, Parser, State, Cost, Console, Channel, Router, Vault, Security, Skill)
  ├── AppBootstrapper (진입점)
  │
  ├─→ OnboardingInstaller (OnboardingScene)
  │     └── 온보딩 서비스 + OnboardingUIController + RollbackService
  │
  ├─→ OfficeInstaller (OfficeScene)
  │     └── 15개 UI 컨트롤러 + OfficeWizardController
  │
  └─→ AgentCreationInstaller (AgentCreationScene)
        └── AgentCreationWizardController

  AgentOfficeInstaller (AgentOfficeScene)
    └── Spawner, HUD, Bootstrapper, ClickHandler, Session, Chat
```

## 에디터 자동화 패턴

- `SceneGenerator.cs` — 씬 전체 생성 + Canvas/UI 계층 구축
- `ScenePatcher.cs` — 기존 씬에 누락 요소만 추가
- `PrefabGenerator.cs` — 프리팹 자동 생성 (NotoSansKR 적용)
- Inspector 바인딩: `SerializedObject` + `FindProperty("_fieldName")` + `objectReferenceValue`

## 데이터 저장

- **PlayerPrefs**: 에이전트 데이터, 세션, 채팅 히스토리, 온보딩 상태
- **OpenClaw 설정**: `~/.openclaw/openclaw.json` (Gateway 토큰, AI 모델, env)
- **API 키**: Base64 암호화 PlayerPrefs (V1에서 DPAPI/Keychain 업그레이드)
