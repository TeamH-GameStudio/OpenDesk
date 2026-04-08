#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 에이전트 3D 모델용 Animator Controller 자동 생성.
/// SD_Maneqquin + Businessman 애니메이션 클립을 State int 파라미터로 전환.
/// State: 0=Idle, 1=Typing, 2=Walk, 3=Cheering, 4=Thinking(Drinking), 5=Sleeping,
///        6=StandToSit, 7=SitToStand, 8=SitToType, 9=TypeToSit, 10=Error(FemaleStandingPose)
/// 메뉴: Tools → OpenDesk → Build Agent Animator Controller
/// </summary>
public static class AgentAnimatorBuilder
{
    private const string SDAnimDir = "Assets/03.Models/SD_Maneqquin/Animations";
    private const string BizAnimDir = "Assets/03.Models/Businessman";
    private const string OutputDir = "Assets/05.Prefabs/Agent";
    private const string OutputPath = OutputDir + "/AgentAnimatorController.controller";
    private const string Agent3DPrefabPath = "Assets/03.Models/SD_Maneqquin/SD_ManeequinPrefab.prefab";

    [MenuItem("Tools/OpenDesk/Build Agent Animator Controller", false, 125)]
    public static void Build()
    {
        EnsureFolder(OutputDir);

        // Businessman 클립 (FBX 모델 교체 완료 — Avatar 호환)
        var idleClip = LoadClipFromFbx($"{BizAnimDir}/Idle.fbx");
        var cheerClip = LoadClipFromFbx($"{BizAnimDir}/Cheering.fbx");
        var errorClip = LoadClipFromFbx($"{SDAnimDir}/Female Standing Pose.fbx"); // 에러용

        // SD_Maneqquin 클립 (타이핑/의자 계열)
        var typingClip = LoadClipFromFbx($"{SDAnimDir}/Typing.fbx");
        var walkClip = LoadClipFromFbx($"{SDAnimDir}/Walking.fbx");
        var thinkingClip = LoadClipFromFbx($"{SDAnimDir}/Drinking.fbx"); // 고민 → Drinking
        var sleepingClip = LoadClipFromFbx($"{SDAnimDir}/Sitting.fbx");

        // 전환 애니메이션 (원샷)
        var standToSitClip = LoadClipFromFbx($"{SDAnimDir}/Stand To Sit.fbx");
        var sitToStandClip = LoadClipFromFbx($"{SDAnimDir}/Sit To Stand.fbx");
        var sitToTypeClip = LoadClipFromFbx($"{SDAnimDir}/Sit To Type.fbx");
        var typeToSitClip = LoadClipFromFbx($"{SDAnimDir}/Type To Sit.fbx");

        if (idleClip == null)
        {
            EditorUtility.DisplayDialog("오류",
                $"Idle 애니메이션을 찾을 수 없습니다.\n{SDAnimDir}/Female Standing Pose.fbx", "OK");
            return;
        }

        // 기존 Controller 삭제 후 재생성
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(OutputPath) != null)
            AssetDatabase.DeleteAsset(OutputPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);

        controller.AddParameter("State", AnimatorControllerParameterType.Int);
        controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);

        var rootLayer = controller.layers[0];
        var sm = rootLayer.stateMachine;

        // ── 루프 상태 (0~5) ─────────────────────────────────────
        var stIdle = sm.AddState("Idle", new Vector3(300, 100, 0));
        stIdle.motion = idleClip;

        var stTyping = sm.AddState("Typing", new Vector3(300, 200, 0));
        stTyping.motion = typingClip ?? idleClip;

        var stWalk = sm.AddState("Walk", new Vector3(300, 300, 0));
        stWalk.motion = walkClip ?? idleClip;

        var stCheer = sm.AddState("Cheering", new Vector3(300, 400, 0));
        stCheer.motion = cheerClip ?? idleClip;

        var stThinking = sm.AddState("Thinking", new Vector3(300, 500, 0));
        stThinking.motion = thinkingClip ?? idleClip;

        var stSleeping = sm.AddState("Sleeping", new Vector3(300, 600, 0));
        stSleeping.motion = sleepingClip ?? idleClip;

        // ── 전환 원샷 상태 (6~9) ────────────────────────────────
        var stStandToSit = sm.AddState("StandToSit", new Vector3(550, 200, 0));
        stStandToSit.motion = standToSitClip ?? idleClip;

        var stSitToStand = sm.AddState("SitToStand", new Vector3(550, 300, 0));
        stSitToStand.motion = sitToStandClip ?? idleClip;

        var stSitToType = sm.AddState("SitToType", new Vector3(550, 400, 0));
        stSitToType.motion = sitToTypeClip ?? idleClip;

        var stTypeToSit = sm.AddState("TypeToSit", new Vector3(550, 500, 0));
        stTypeToSit.motion = typeToSitClip ?? idleClip;

        // Error 전용 (Female Standing Pose)
        var stError = sm.AddState("Error", new Vector3(550, 600, 0));
        stError.motion = errorClip ?? idleClip;

        sm.defaultState = stIdle;

        // ── Any State → 각 상태 전환 ────────────────────────────
        AddTransitionFromAny(sm, stIdle, 0);
        AddTransitionFromAny(sm, stTyping, 1);
        AddTransitionFromAny(sm, stWalk, 2);
        AddTransitionFromAny(sm, stCheer, 3);
        AddTransitionFromAny(sm, stThinking, 4);
        AddTransitionFromAny(sm, stSleeping, 5);
        AddTransitionFromAny(sm, stStandToSit, 6);
        AddTransitionFromAny(sm, stSitToStand, 7);
        AddTransitionFromAny(sm, stSitToType, 8);
        AddTransitionFromAny(sm, stTypeToSit, 9);
        AddTransitionFromAny(sm, stError, 10);

        // Cheering 종료 후 Idle 복귀 (Animator 레벨 자동 전환)
        var cheerToIdle = stCheer.AddTransition(stIdle);
        cheerToIdle.hasExitTime = true;
        cheerToIdle.exitTime = 1f;
        cheerToIdle.duration = 0.25f;
        cheerToIdle.hasFixedDuration = true;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        AssignToAgent3DPrefab(controller);
        AssetDatabase.Refresh();

        var names = new[] { "Idle", "Typing", "Walk", "Cheering", "Thinking(Drinking)", "Sleeping",
                            "StandToSit", "SitToStand", "SitToType", "TypeToSit", "Error(FemaleStandingPose)" };
        var clips = new AnimationClip[] { idleClip, typingClip, walkClip, cheerClip,
                                          thinkingClip, sleepingClip,
                                          standToSitClip, sitToStandClip, sitToTypeClip, typeToSitClip, errorClip };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < names.Length; i++)
            sb.AppendLine($"  {i}={names[i]}: {(clips[i] != null ? "OK" : "X")}");

        var clipInfo = sb.ToString();
        Debug.Log($"[AgentAnimatorBuilder] Animator Controller 생성 완료\n{clipInfo}");
        EditorUtility.DisplayDialog("완료",
            $"AgentAnimatorController 생성 완료.\n\n{clipInfo}\n위치: {OutputPath}", "OK");
    }

    private static void AddTransitionFromAny(AnimatorStateMachine sm, AnimatorState target,
        int stateValue, bool hasExitTime = false)
    {
        var transition = sm.AddAnyStateTransition(target);
        transition.AddCondition(AnimatorConditionMode.Equals, stateValue, "State");
        transition.hasExitTime = hasExitTime;
        transition.duration = 0.25f;
        transition.canTransitionToSelf = false;
    }

    private static AnimationClip LoadClipFromFbx(string fbxPath)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        if (assets == null) return null;

        foreach (var asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                return clip;
        }
        return null;
    }

    private static void AssignToAgent3DPrefab(AnimatorController controller)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Agent3DPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[AgentAnimatorBuilder] Model_Agent3D 프리팹 없음: {Agent3DPrefabPath}");
            return;
        }

        // 프리팹 편집 모드
        var prefabRoot = PrefabUtility.LoadPrefabContents(Agent3DPrefabPath);

        // Missing Script 제거 (레거시 AgentCharacterController 등)
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(prefabRoot);

        var animator = prefabRoot.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = prefabRoot.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // Collider 없으면 추가 (클릭 감지용)
        if (prefabRoot.GetComponentInChildren<Collider>() == null)
        {
            var col = prefabRoot.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.9f, 0);
            col.radius = 0.3f;
            col.height = 1.8f;
        }

        // 새 AgentCharacterController 부착 (없으면)
        var charCtrl = prefabRoot.GetComponent<OpenDesk.Presentation.Character.AgentCharacterController>();
        if (charCtrl == null)
            prefabRoot.AddComponent<OpenDesk.Presentation.Character.AgentCharacterController>();

        PrefabUtility.SaveAsPrefabAsset(prefabRoot, Agent3DPrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);

        Debug.Log("[AgentAnimatorBuilder] Model_Agent3D 프리팹 설정 완료 (Animator + CharacterController + Missing Script 정리)");
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Replace("\\", "/").Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
