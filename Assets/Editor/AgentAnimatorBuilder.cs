#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 에이전트 3D 모델용 Animator Controller 자동 생성.
/// FBX 애니메이션 클립(Idle/Typing/Walk/Cheering)을 State 파라미터로 전환.
/// 메뉴: Tools → OpenDesk → Build Agent Animator Controller
/// </summary>
public static class AgentAnimatorBuilder
{
    private const string AnimDir = "Assets/03.Models/Businessman";
    private const string OutputDir = "Assets/05.Prefabs/Agent";
    private const string OutputPath = OutputDir + "/AgentAnimatorController.controller";
    private const string Agent3DPrefabPath = "Assets/05.Prefabs/Agent/Model_Agent3D.prefab";

    [MenuItem("Tools/OpenDesk/Build Agent Animator Controller", false, 125)]
    public static void Build()
    {
        EnsureFolder(OutputDir);

        // FBX에서 AnimationClip 로드
        var idleClip = LoadClipFromFbx($"{AnimDir}/Idle.fbx");
        var typingClip = LoadClipFromFbx($"{AnimDir}/Typing.fbx");
        var walkClip = LoadClipFromFbx($"{AnimDir}/Standard Walk.fbx");
        var cheerClip = LoadClipFromFbx($"{AnimDir}/Cheering.fbx");

        if (idleClip == null)
        {
            EditorUtility.DisplayDialog("오류",
                $"Idle.fbx 애니메이션을 찾을 수 없습니다.\n{AnimDir}/Idle.fbx", "OK");
            return;
        }

        // Animator Controller 생성
        var controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);

        // 파라미터 추가
        controller.AddParameter("State", AnimatorControllerParameterType.Int);
        controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);

        var rootLayer = controller.layers[0];
        var stateMachine = rootLayer.stateMachine;

        // 상태 생성 (0=Idle, 1=Typing, 2=Walk, 3=Cheering)
        var stIdle = stateMachine.AddState("Idle", new Vector3(300, 100, 0));
        stIdle.motion = idleClip;

        var stTyping = stateMachine.AddState("Typing", new Vector3(300, 200, 0));
        stTyping.motion = typingClip ?? idleClip;

        var stWalk = stateMachine.AddState("Walk", new Vector3(300, 300, 0));
        stWalk.motion = walkClip ?? idleClip;

        var stCheer = stateMachine.AddState("Cheering", new Vector3(300, 400, 0));
        stCheer.motion = cheerClip ?? idleClip;

        // 기본 상태 = Idle
        stateMachine.defaultState = stIdle;

        // Any State → 각 상태 전환 (State 파라미터 기반)
        AddTransitionFromAny(stateMachine, stIdle, 0);
        AddTransitionFromAny(stateMachine, stTyping, 1);
        AddTransitionFromAny(stateMachine, stWalk, 2);
        AddTransitionFromAny(stateMachine, stCheer, 3);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // Model_Agent3D 프리팹에 Animator Controller 할당
        AssignToAgent3DPrefab(controller);

        AssetDatabase.Refresh();

        var clipInfo = $"Idle: {(idleClip != null ? "OK" : "X")}\n" +
                       $"Typing: {(typingClip != null ? "OK" : "X")}\n" +
                       $"Walk: {(walkClip != null ? "OK" : "X")}\n" +
                       $"Cheering: {(cheerClip != null ? "OK" : "X")}";

        Debug.Log($"[AgentAnimatorBuilder] Animator Controller 생성 완료\n{clipInfo}");
        EditorUtility.DisplayDialog("완료",
            $"AgentAnimatorController 생성 완료.\n\n{clipInfo}\n\n" +
            $"위치: {OutputPath}\n\n" +
            "State 파라미터: 0=Idle, 1=Typing, 2=Walk, 3=Cheering",
            "OK");
    }

    private static void AddTransitionFromAny(AnimatorStateMachine sm, AnimatorState target, int stateValue)
    {
        var transition = sm.AddAnyStateTransition(target);
        transition.AddCondition(AnimatorConditionMode.Equals, stateValue, "State");
        transition.hasExitTime = false;
        transition.duration = 0.15f;
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
