using UnityEngine;
using TMPro;

public class AgentCharacterController : MonoBehaviour
{
    [Header("Agent Info")]
    public string agentName = "Scout";
    public string agentRole = "Developer";

    [Header("Animation")]
    private Animator _animator;

    [Header("Speech Bubble")]
    public GameObject speechBubble;        // 말풍선 오브젝트 (Canvas World Space)
    public TextMeshProUGUI speechText;     // 말풍선 텍스트
    public GameObject statusIcon;          // 상태 아이콘 (머리 위)

    [Header("Effects")]
    public ParticleSystem happyParticle;   // 완료 시 파티클
    public ParticleSystem errorParticle;   // 에러 시 파티클
    public GameObject zzzObject;           // 수면 Zzz 오브젝트

    private AgentState _currentState = AgentState.Idle;
    private float _speechTimer = 0f;
    private float _speechDuration = 3f;

    // =====================
    // 6개 상태 정의
    // =====================
    public enum AgentState
    {
        Idle,       // 대기 — 두리번, 하품
        Working,    // 작업중 — 타이핑
        Thinking,   // 생각중 — 턱 괴기
        Happy,      // 완료 — 박수/점프
        Error,      // 에러 — 당황
        Sleeping    // 비활성 — 잠자기
    }

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    void Update()
    {
        // 말풍선 자동 숨김
        if (_speechTimer > 0f)
        {
            _speechTimer -= Time.deltaTime;
            if (_speechTimer <= 0f)
            {
                HideSpeechBubble();
            }
        }
    }

    // =====================
    // 상태 전환 (핵심 메서드)
    // =====================
    public void SetState(AgentState newState, string message = "")
    {
        if (_currentState == newState && string.IsNullOrEmpty(message))
            return;

        _currentState = newState;

        // 모든 이펙트 초기화
        ResetEffects();

        // Animator 파라미터 설정
        _animator.SetInteger("State", (int)newState);

        // 상태별 추가 연출
        switch (newState)
        {
            case AgentState.Idle:
                ShowSpeechBubble(string.IsNullOrEmpty(message) ? "..." : message);
                break;

            case AgentState.Working:
                ShowSpeechBubble(string.IsNullOrEmpty(message) ? "작업 중..." : message);
                break;

            case AgentState.Thinking:
                ShowSpeechBubble(string.IsNullOrEmpty(message) ? "생각 중..." : message);
                break;

            case AgentState.Happy:
                ShowSpeechBubble(string.IsNullOrEmpty(message) ? "완료!" : message);
                if (happyParticle != null) happyParticle.Play();
                break;

            case AgentState.Error:
                ShowSpeechBubble(string.IsNullOrEmpty(message) ? "오류 발생!" : message);
                if (errorParticle != null) errorParticle.Play();
                break;

            case AgentState.Sleeping:
                HideSpeechBubble();
                if (zzzObject != null) zzzObject.SetActive(true);
                break;
        }
    }

    // =====================
    // 외부에서 이벤트 수신 (JSON → 상태 매핑)
    // =====================
    public void OnAgentEvent(string status, string message)
    {
        // 나중에 OpenClaw / Claude Code 이벤트와 연결
        AgentState state = status.ToLower() switch
        {
            "idle"     => AgentState.Idle,
            "working"  => AgentState.Working,
            "thinking" => AgentState.Thinking,
            "happy"    => AgentState.Happy,
            "complete" => AgentState.Happy,
            "error"    => AgentState.Error,
            "failed"   => AgentState.Error,
            "sleeping" => AgentState.Sleeping,
            "offline"  => AgentState.Sleeping,
            _          => AgentState.Idle
        };

        SetState(state, message);
    }

    // =====================
    // 말풍선
    // =====================
    private void ShowSpeechBubble(string text)
    {
        if (speechBubble == null || speechText == null) return;

        speechBubble.SetActive(true);
        speechText.text = text;
        _speechTimer = _speechDuration;
    }

    private void HideSpeechBubble()
    {
        if (speechBubble != null)
            speechBubble.SetActive(false);
    }

    // =====================
    // 이펙트 초기화
    // =====================
    private void ResetEffects()
    {
        if (happyParticle != null) happyParticle.Stop();
        if (errorParticle != null) errorParticle.Stop();
        if (zzzObject != null) zzzObject.SetActive(false);
    }

    // =====================
    // 현재 상태 조회
    // =====================
    public AgentState GetCurrentState() => _currentState;
    public string GetStateName() => _currentState.ToString();
}
