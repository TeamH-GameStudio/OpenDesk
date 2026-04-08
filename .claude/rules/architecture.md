---
paths:
  - "Assets/02.Scripts/**"
---

# OpenDesk 아키텍처

## 스크립트 폴더 구조

```
Assets/02.Scripts/
├── Core/
│   ├── Models/           — AgentActionType, AgentEvent, SkillCategory, SkillEntry, ApiProvider 등
│   ├── Services/         — IClaudeService, IAgentStateService, ICostMonitorService 등
│   ├── Implementations/  — ClaudeService, 기타 서비스 구현체
│   └── Installers/       — CoreInstaller (DI), AppBootstrapper (진입점)
├── SkillDiskette/
│   ├── SkillDiskette.cs          — ScriptableObject (프리셋 + CreateRuntime)
│   ├── SkillDisketteView.cs      — 3D 비주얼 + Update Raycast 드래그 (New Input System)
│   ├── SkillDisketteFactory.cs   — 프리셋 로드 (Resources/SkillDisks) + 런타임 생성
│   ├── AgentEquipmentManager.cs  — 장착/해제 + BuildSystemPrompt + R3 이벤트
│   ├── DisketteShelfUI.cs        — 화면 우측 UI 패널 + EventTrigger 드래그 → 3D 에이전트 드롭
│   └── Models/CraftResult.cs     — 크래프팅 JSON 파싱 모델
├── Pipeline/
│   ├── DiskettePrinterController.cs  — 크래프팅 + 토글 버튼 + ShelfUI 연동
│   ├── InboxController.cs            — 파일 투입 + BuildFileContext
│   ├── OutboxController.cs           — 결과 저장 (TMP 태그 제거) + 클릭 열기
│   └── OfficePipelineManager.cs      — 파이프라인 중앙 통제 (BuildFullSystemPrompt)
├── AgentCreation/
│   ├── Models/           — AgentRole, AgentAIModel, AgentTone, AgentCreationData, AgentProfileSO 등
│   └── Installers/       — AgentCreationInstaller, AgentOfficeInstaller
├── Presentation/
│   ├── Character/        — AgentCharacterController (Equipment 프로퍼티), AgentSpawner, FSM 6상태
│   ├── UI/
│   │   ├── Session/       — ChatPanelController (ApplySystemPrompt + Pipeline 연동)
│   │   ├── EquipmentSlotUI.cs — 장착 슬롯 표시 + 해제 → 선반 복귀
│   │   ├── Panels/        — Terminal, Tab, Channels, ApiKeys 등
│   │   └── Modals/        — ModalDialogController
│   ├── Dashboard/        — AgenticLoopVisualizer, ConsoleLogController, CostHudController
│   └── SceneLoading/     — LoadingSceneController
├── Claude/               — ClaudeWebSocketClient (새 프로토콜 7+6종), AgentProtocolTestManager, ClaudeChatManager (레거시), MiddlewareLauncher (main.py), ClaudeChatProtocol (새 DTO)
└── Onboarding/           — 온보딩 (현재 미사용 — OpenClaw 제거)
```

## VContainer DI 계층

```
CoreInstaller (DontDestroyOnLoad)
  ├── Core 서비스 (State, Cost, Console, Vault 등)
  ├── AppBootstrapper (진입점)
  │
  ├─→ OfficeInstaller (OfficeScene)
  │     └── 15개 UI 컨트롤러
  │
  ├─→ AgentCreationInstaller (AgentCreationScene)
  │     └── AgentCreationWizardController
  │
  └─→ AgentOfficeInstaller (AgentOfficeScene)
        ├── AgentSpawner, AgentOfficeBootstrapper, AgentClickHandler
        ├── SessionListController, ChatPanelController
        ├── ClaudeWebSocketClient (ComponentInHierarchy)
        ├── ClaudeService → IClaudeService (Scoped)
        └── DiskettePrinterController (ComponentInHierarchy)
```

## 디스켓 시스템 흐름

```
[DisketteShelfUI]           [DiskettePrinterController]
  카드 목록 (우측 패널)         크래프팅 (하단 토글)
       |                           |
  드래그 → 3D 에이전트        CraftDisketteAsync → Claude 응답
       |                           |
  AgentEquipmentManager        ShelfUI.AddDiskette
  .TryEquip()                      |
       |                    카드 추가 (선반에 표시)
  BuildSystemPrompt()
       |
  ChatPanelController
  .ApplySystemPrompt()
       |
  ClaudeWebSocketClient
  .SendConfig(prompt)
```

## 파이프라인 흐름

```
[InboxController]  →  [OfficePipelineManager]  →  [OutboxController]
  파일 선택              BuildFullSystemPrompt       결과 저장
  BuildFileContext       (Equipment + Files)          TMP 태그 제거
       |                       |                       |
       +-------→ ChatPanelController ←--------+
                  ApplySystemPrompt()
                  HandleFinal → Outbox.ReceiveResult
```

## Input System

**New Input System 사용** (레거시 Input 비활성)
- `Mouse.current.leftButton.wasPressedThisFrame` / `.isPressed` / `.wasReleasedThisFrame`
- `Mouse.current.position.ReadValue()`
- UI 위 클릭 감지: `EventSystem.current.RaycastAll()`
- `using UnityEngine.InputSystem;` 필수

## 에디터 메뉴

| 메뉴 | 파일 | 역할 |
|------|------|------|
| OpenDesk > Build Diskette Prefab | DiskettePrefabBuilder.cs | 디스켓 프리팹 생성 |
| OpenDesk > Build Preset Skill Disks | SkillDiskettePresetBuilder.cs | 프리셋 SO 5개 생성 |
| Tools > OpenDesk > Patch Pipeline Objects | PipelineScenePatcher.cs | 3D Printer/InBox/OutBox 배치 |
| Tools > OpenDesk > Patch Diskette Shelf UI | DisketteShelfUIPatcher.cs | 선반 UI + 토글 배치 |

## 데이터 저장

- **PlayerPrefs**: 에이전트 데이터, 세션, 채팅 히스토리
- **Resources/SkillDisks/**: 프리셋 디스켓 ScriptableObject
- **API 키**: Base64 암호화 PlayerPrefs
- **Out-box 결과**: Desktop/OpenDesk_Output/
