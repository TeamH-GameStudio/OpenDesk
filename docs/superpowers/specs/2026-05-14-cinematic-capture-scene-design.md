# Cinematic Capture Scene — Design

작성: 2026-05-14
대상: After Effects 후반 작업용 캐릭터 컷신을 알파 채널 보존된 PNG 시퀀스로 뽑는 독립 Unity 씬.

## 1. 목적

- 8초짜리 캐릭터 컷신(표정 + 포즈 + 라이팅 변화)을 Inspector 에서 직접 키프레임 입력해 재생.
- Unity Recorder 패키지로 알파 보존 PNG 시퀀스를 뽑아 After Effects 에 그대로 import.
- AgentCreation/Office 의 DI 그래프와 분리된 독립 씬 — VContainer 없이 순수 MonoBehaviour 만으로 동작.
- 기존 자산 재사용: `WardrobeCatalogSO`, `WardrobeApplier`, `CharacterPartSwapper`, `AgentExpressionKey`, `Model_Agent3D.prefab`, SD 마네킹 애니메이션 클립들.

## 2. 비목표 (YAGNI)

- 런타임 OS 빌드에서의 캡처. 에디터 전용.
- Unity Timeline 패키지 PlayableDirector 통합. (단순 Update 기반 키프레임 스캐너로 충분.)
- 다중 카메라 컷 편집. 단일 고정 카메라만.
- 자동 Recorder 설정 코드. 사용자가 Recorder Window 를 수동으로 한 번만 셋업.

## 3. 결정 사항

| 항목 | 선택 | 근거 |
|------|------|------|
| Outfit Source | `WardrobeCatalogSO` + Inspector `WardrobeOutfit` | 기존 시스템 재사용. AgentCreation 의 옷이 그대로 동작. |
| Timeline 정의 | Inspector `List<CinematicTimelineEntry>` (빈 리스트로 시작) | 사용자가 직접 매핑. SO 분리는 YAGNI. |
| 캡처 | Unity Recorder 5.1.6 (이미 설치됨) | 알파 PNG 시퀀스 표준. 자체 캡처 코드 작성 불요. |
| Animator | Playables API (`AnimationPlayableUtilities.PlayClip`) | 새 Animator Controller 작성 불요. Inspector 에 `AnimationClip` 직접 꽂음. |
| Base Char | `Assets/05.Prefabs/Agent/Model_Agent3D.prefab` 인스턴스화 | AgentCreation 과 동일한 외형. |
| DI | 없음. 순수 MonoBehaviour. | 독립 씬 — CoreInstaller 부트스트랩 회피. |
| 씬 위치 | `Assets/01.Scenes/CinematicCaptureScene.unity` | 기존 씬과 같은 폴더. |

## 4. Hierarchy

```
CinematicCaptureScene
├── Lighting
│   ├── KeyLight (Directional Light)         — 톤 변화 대상
│   └── FillLight (Directional Light, 약함)   — 그림자 보강 (정적)
├── Cinematic (Model_Agent3D 프리팹 인스턴스)
│   └── (기존 WardrobeApplier + CharacterPartSwapper + Animator)
├── CaptureCamera (Camera, MainCamera 태그)
│   ├── clearFlags = SolidColor
│   └── backgroundColor = (0, 0, 0, 0)
├── PostFX (Global Volume, 선택)
│   └── Vignette / Color Adjustments
└── _Cinematic (Empty GameObject)
    └── CinematicCaptureController.cs
```

## 5. 스크립트 — `Assets/02.Scripts/Cinematic/`

### 5.1 `CinematicTimelineEntry.cs`

```csharp
namespace OpenDesk.Cinematic
{
    [Serializable]
    public struct CinematicTimelineEntry
    {
        public float TimeSeconds;                  // 0.0, 0.5, 1.5, ...
        public AgentExpressionKey Expression;      // 기존 9종 enum 재사용
        public AnimationClip PoseClip;             // null 이면 직전 포즈 유지
        public float CrossfadeDuration;            // 0 = 즉시
        public LightingTone Lighting;
        [TextArea] public string Note;             // 주석 (런타임 무시)
    }

    [Serializable]
    public struct LightingTone
    {
        public Color KeyLightColor;
        public float KeyLightIntensity;
        public Color AmbientColor;
        [Range(0f, 1f)] public float Vignette;     // PostFX 없으면 무시
        public float TransitionSeconds;            // 이전 톤 → 이 톤 lerp 시간
    }
}
```

규칙:
- `TimeSeconds` 오름차순 정렬 필수. `OnValidate` 에서 정렬 안 됐으면 경고.
- `Expression` 은 항상 의미 있음 (이전 키프레임이 Default 면 다음에서 Default 명시 가능).
- `PoseClip` 이 null 이면 직전 포즈 유지 — 표정만 바꾸고 싶을 때 편함.
- `LightingTone.TransitionSeconds = 0` 이면 snap, > 0 이면 이전 톤에서 lerp.

### 5.2 `CinematicCaptureController.cs` (MonoBehaviour)

Inspector 필드:

```csharp
[Header("Wardrobe")]
[SerializeField] WardrobeCatalogSO _catalog;
[SerializeField] WardrobeApplier _wardrobeApplier;
[SerializeField] WardrobeOutfit _outfit;             // 빈 슬롯은 카탈로그 기본값
[SerializeField] bool _useDefaultOutfit = true;       // true 면 _outfit 무시 + ApplyDefaults

[Header("Animation")]
[SerializeField] Animator _animator;                  // Playables 그래프의 출력 대상

[Header("Lighting")]
[SerializeField] Light _keyLight;
[SerializeField] Light _fillLight;                    // 선택 (정적)
[SerializeField] Volume _postVolume;                  // 선택

[Header("Timeline")]
[SerializeField] List<CinematicTimelineEntry> _timeline = new();
[SerializeField] float _totalDuration = 8f;

[Header("Runtime")]
[SerializeField] bool _autoExitPlayMode = true;       // 8초 후 EditorApplication.isPlaying = false
[SerializeField] bool _logProgress = true;
```

런타임:
- `Start()`
  1. `_wardrobeApplier.SetCatalog(_catalog)`
  2. `_useDefaultOutfit` ? `ApplyDefaults()` : `Apply(_outfit.ToWardrobe(_catalog))`
  3. Playables 그래프 생성 (`PlayableGraph.Create`) + `AnimationPlayableOutput.Create(graph, "Anim", _animator)`
  4. 첫 키프레임의 표정/포즈/라이팅을 즉시 적용 (lerp 없이 snap).
  5. `_currentLighting` 캐시 (현재 적용 중 톤) 초기화.

- `Update()`
  1. `elapsed = Time.timeSinceLevelLoad`
  2. `currentIndex = 가장 큰 i where _timeline[i].TimeSeconds ≤ elapsed`
  3. `currentIndex` 가 직전 프레임과 다르면 → **전환 트리거**:
     - 표정: `_wardrobeApplier.SetEyeExpression(entry.Expression)`
     - 포즈: `entry.PoseClip` 이 null 이 아니면 새 `AnimationClipPlayable` 만들어 `Mixer` 의 다음 input 으로 crossfade. 0이면 weight 즉시 1로.
     - 라이팅: `_lightingFrom = _currentLighting`, `_lightingTo = entry.Lighting`, `_lightingTimer = 0`.
  4. 라이팅 lerp 매 프레임 진행: `t = min(1, _lightingTimer / max(0.0001, _lightingTo.TransitionSeconds))` → `_keyLight.color/intensity`, `RenderSettings.ambientLight`, `_postVolume` Vignette weight 보간.
  5. `elapsed ≥ _totalDuration` → 동작 정지 + (autoExit 이면) `EditorApplication.isPlaying = false`.

- `OnDestroy()` — PlayableGraph 정리.

**Playables Crossfade 구현 노트**

```
Mixer (AnimationMixerPlayable, 2 inputs)
 ├── inputA: 직전 클립 (weight 1 → 0)
 └── inputB: 새 클립 (weight 0 → 1)
```

crossfade 진행 중에는 둘 다 살아 있고, 끝나면 inputA 를 Destroy 하고 inputB 가 inputA 자리로 이동. `CrossfadeDuration = 0` 이면 즉시 swap.

### 5.3 `CinematicCaptureControllerEditor.cs` (Editor 전용)

`Assets/02.Scripts/Cinematic/Editor/` 에 위치.

버튼:
- **Apply Outfit Now** — Play 안 켜도 Edit 모드에서 `_wardrobeApplier.SetCatalog(...).Apply(...)` 실행. 미리보기.
- **Sort Timeline By Time** — `_timeline` 을 `TimeSeconds` 오름차순 정렬.
- **Reset Timeline** — 리스트 비우기 (확인 다이얼로그 후).

`Add Default 8s Template` 버튼은 만들지 않음 (사용자가 직접 매핑 결정).

## 6. 알파 배경 보존

| 위치 | 설정 |
|------|------|
| Camera | `clearFlags = SolidColor`, `backgroundColor.a = 0` |
| URP Renderer Asset | Post-Processing on. 표준 URP Asset 이면 그대로 알파 보존됨. |
| Recorder Window | Image Sequence → PNG → **Capture Alpha 체크** + Source = Targeted Camera (CaptureCamera) |
| Fallback | Toon 셰이더가 알파에 1 을 강제하면 → CaptureCamera 에 ARGB32 RenderTexture 할당 + Recorder Source 를 그 RT 로 변경 (씬 README 에 안내) |

## 7. Recorder 사용법 (씬 GameObject 의 메모 컴포넌트 + README 코멘트)

1. `Window > General > Recorder > Recorder Window`
2. `+ Add Recorder > Image Sequence`
3. Format: PNG, **Include Alpha** 체크
4. Source: `Targeted Camera` → CinematicCaptureScene 의 `CaptureCamera`
5. Output: `<Project>/Recordings/cinematic_<Take>/frame_<Frame>`
6. Frame Rate: 30 (또는 60)
7. **START RECORDING** → 씬이 자동 Play → 8초 후 컨트롤러가 Play Mode 종료 → Recorder 가 자동 stop & flush.

## 8. 사용 워크플로우

1. CinematicCaptureScene 열기.
2. `_Cinematic > CinematicCaptureController` Inspector 에서:
   - `Outfit` 채우거나 `UseDefaultOutfit = true`.
   - `Timeline` 키프레임 추가 — `TimeSeconds`, `Expression`, `PoseClip` (Project 패널에서 `.anim` 드래그), `CrossfadeDuration`, `Lighting`.
3. (선택) Editor 의 **Apply Outfit Now** 로 외형 미리보기.
4. Recorder Window 셋업 (위 8 단계).
5. START RECORDING → 8초 컷 자동 캡처 → After Effects 에서 PNG 시퀀스 import.

## 9. 영향 받는 파일

신규:
- `Assets/01.Scenes/CinematicCaptureScene.unity` + `.meta`
- `Assets/02.Scripts/Cinematic/CinematicTimelineEntry.cs`
- `Assets/02.Scripts/Cinematic/CinematicCaptureController.cs`
- `Assets/02.Scripts/Cinematic/Editor/CinematicCaptureControllerEditor.cs`
- `Assets/02.Scripts/Cinematic/Editor/OpenDesk.Cinematic.Editor.asmdef` (Editor 전용 asmdef — 기존 패턴 따름)
- `Assets/02.Scripts/Cinematic/OpenDesk.Cinematic.asmdef` (런타임 asmdef — Characters/Wardrobe 의존)

수정: 없음. 기존 코드 변경 무.

## 10. 위험/주의

| 위험 | 완화 |
|------|------|
| URP Toon 셰이더가 알파에 1 을 써서 배경 투명이 깨질 수 있음 | 발견 시 RenderTexture(ARGB32) 우회 — 씬 README 에 안내. |
| Playables 그래프 leak | `OnDestroy` 에서 `_graph.Destroy()` 명시 호출. Play Mode 종료 시 Unity 가 자동 정리하지만 안전망. |
| Inspector 에서 `TimeSeconds` 순서 잘못 입력 | `OnValidate` 경고 + Editor 의 Sort 버튼 제공. |
| `_animator` 가 비활성/없을 때 Playables 실패 | `Start()` 에서 null 체크 후 경고 로그. PoseClip 이 있는 키프레임만 영향. |
| 8초 끝났는데 Recorder 가 아직 마지막 프레임 flushing 중 | `_autoExitPlayMode` 가 true 라도 한 프레임 yield 후 종료 (`yield return null` 한 번). |

## 11. 테스트 전략

이 씬은 에디터 도구이고 자동 단위 테스트가 무의미 (Animator/렌더링 의존). 수동 검증 체크리스트로 대체:

- [ ] CinematicCaptureScene Play → 콘솔 에러 0건.
- [ ] 빈 Timeline 으로 Play → 8초 후 Play Mode 자동 종료, 캐릭터는 기본 외형 + Default 표정.
- [ ] Timeline 2 키프레임 (0s: Idle/Default, 4s: Sitting Talking/Surprised, 둘 다 Lighting transition 1s) → 표정/포즈/조명 모두 보간 적용 확인.
- [ ] Recorder 로 1초 컷 캡처 → PNG 파일이 배경 투명 (Preview 에서 체커보드).
- [ ] **Apply Outfit Now** 버튼 → Edit 모드에서 외형 즉시 갱신.
