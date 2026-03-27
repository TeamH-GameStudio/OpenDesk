# AgentOffice 애니메이터 세팅 가이드

## 1단계: Animator Controller 생성

1. Project 우클릭 → Create → Animator Controller → 이름: `AgentAnimator`
2. 캐릭터 오브젝트 선택 → Animator 컴포넌트의 Controller에 `AgentAnimator` 연결

## 2단계: 파라미터 추가

Animator 창 열기 (Window → Animation → Animator)
- Parameters 탭 클릭
- `+` 버튼 → Int → 이름: `State`

```
State 값 매핑:
0 = Idle
1 = Working  
2 = Thinking
3 = Happy
4 = Error
5 = Sleeping
```

## 3단계: 애니메이션 클립 임포트

Mixamo에서 다운로드할 애니메이션 (Without Skin, FBX, 30fps):

| 상태 | Mixamo 검색어 | 추천 애니메이션 |
|------|-------------|---------------|
| Idle | "idle" | "Happy Idle" 또는 "Breathing Idle" |
| Working | "typing" | "Typing" |
| Thinking | "thinking" | "Thinking" 또는 "Weight Shift" |
| Happy | "cheering" | "Cheering" 또는 "Victory" |
| Error | "disappointed" | "Disappointed" 또는 "Defeat" |
| Sleeping | "sleeping" | "Sleeping Idle" 또는 "Bored" |

각 FBX 임포트 후:
- Rig 탭 → Animation Type: Humanoid
- Avatar Definition: Copy From Other Avatar → 첫 번째 FBX의 Avatar 선택
- Animation 탭 → Loop Time 체크 (Happy 제외, 나머지 전부 루프)

## 4단계: Animator 스테이트 구성

Animator 창에서:

1. 각 애니메이션 클립을 Animator 창에 드래그 (6개 스테이트 생성)
2. "Idle" 스테이트 우클릭 → Set as Layer Default State (주황색)
3. **Any State** 우클릭 → Make Transition → 각 스테이트로 연결 (6개)

각 Transition 설정:
- Conditions: State → Equals → (해당 숫자)
- Has Exit Time: 체크 해제
- Transition Duration: 0.15 (빠른 전환)
- Can Transition To Self: 체크 해제

```
Any State → Idle:     State Equals 0
Any State → Working:  State Equals 1
Any State → Thinking: State Equals 2
Any State → Happy:    State Equals 3
Any State → Error:    State Equals 4
Any State → Sleeping: State Equals 5
```

## 5단계: 스크립트 연결

1. 캐릭터 오브젝트에 `AgentCharacterController` 컴포넌트 추가
2. 같은 오브젝트 또는 빈 오브젝트에 `AgentStateTestUI` 컴포넌트 추가
3. `AgentStateTestUI`의 target에 캐릭터의 `AgentCharacterController` 연결

## 6단계: 테스트

Play 모드에서:
- 화면 좌측 상단 버튼 클릭 또는 키보드 1~6으로 상태 전환
- 커스텀 메시지 입력 후 전송 테스트

## (선택) 말풍선 세팅

1. 캐릭터 머리 위에 빈 오브젝트 생성 (SpeechBubbleAnchor)
2. 그 아래에 Canvas (World Space) 생성
   - Canvas Scaler → 0.01
   - 크기 적절히 조절
3. Canvas 안에 Image (배경) + TextMeshPro 추가
4. AgentCharacterController의 speechBubble, speechText에 연결

말풍선은 나중에 해도 됨 — 일단 애니메이션 전환부터 확인!
