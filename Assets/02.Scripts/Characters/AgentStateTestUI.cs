using UnityEngine;
using UnityEngine.InputSystem;

public class AgentStateTestUI : MonoBehaviour
{
    [Header("Target")]
    public AgentCharacterController target;

    private string _customMessage = "";

    void Update()
    {
        if (target == null) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Idle);
        if (kb.digit2Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Working, "코드 작성 중...");
        if (kb.digit3Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Thinking, "아키텍처 검토 중...");
        if (kb.digit4Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Happy, "빌드 성공!");
        if (kb.digit5Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Error, "컴파일 에러 발생");
        if (kb.digit6Key.wasPressedThisFrame) target.SetState(AgentCharacterController.AgentState.Sleeping);
    }

    void OnGUI()
    {
        if (target == null)
        {
            GUI.Label(new Rect(10, 10, 400, 30), "AgentCharacterController를 target에 연결해주세요!");
            return;
        }

        float x = 10;
        float y = 10;
        float w = 160;
        float h = 40;
        float gap = 5;

        GUI.Box(new Rect(x, y, w * 2 + gap, h), $"현재 상태: {target.GetStateName()}");
        y += h + gap;

        if (GUI.Button(new Rect(x, y, w, h), "1. Idle (대기)"))
            target.SetState(AgentCharacterController.AgentState.Idle);
        if (GUI.Button(new Rect(x + w + gap, y, w, h), "2. Working (작업)"))
            target.SetState(AgentCharacterController.AgentState.Working, "코드 작성 중...");
        y += h + gap;

        if (GUI.Button(new Rect(x, y, w, h), "3. Thinking (생각)"))
            target.SetState(AgentCharacterController.AgentState.Thinking, "아키텍처 검토 중...");
        if (GUI.Button(new Rect(x + w + gap, y, w, h), "4. Happy (완료)"))
            target.SetState(AgentCharacterController.AgentState.Happy, "빌드 성공!");
        y += h + gap;

        if (GUI.Button(new Rect(x, y, w, h), "5. Error (에러)"))
            target.SetState(AgentCharacterController.AgentState.Error, "컴파일 에러 발생");
        if (GUI.Button(new Rect(x + w + gap, y, w, h), "6. Sleeping (수면)"))
            target.SetState(AgentCharacterController.AgentState.Sleeping);
        y += h + gap + 10;

        GUI.Label(new Rect(x, y, w, 20), "커스텀 메시지:");
        y += 22;
        _customMessage = GUI.TextField(new Rect(x, y, w * 2 + gap, h - 10), _customMessage);
        y += h - 5;

        if (GUI.Button(new Rect(x, y, w, h), "메시지로 Working"))
            target.SetState(AgentCharacterController.AgentState.Working, _customMessage);
        if (GUI.Button(new Rect(x + w + gap, y, w, h), "메시지로 Happy"))
            target.SetState(AgentCharacterController.AgentState.Happy, _customMessage);
    }
}