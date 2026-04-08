using UnityEngine;

namespace OpenDesk.AgentCreation.Models
{
    /// <summary>
    /// 에이전트 한 명의 모든 설정을 담는 ScriptableObject.
    /// 위저드 완료 시 런타임 생성 가능, 또는 에셋으로 미리 생성 가능.
    /// 실제 3D 모델이 준비되면 ModelPrefab만 교체하면 됨.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAgentProfile", menuName = "OpenDesk/Agent Profile")]
    public class AgentProfileSO : ScriptableObject
    {
        // ── Identity ────────────────────────────────────────
        [Header("Identity")]
        [SerializeField] private string _agentName = "";
        [SerializeField] private AgentRole _role = AgentRole.None;

        // ── AI ──────────────────────────────────────────────
        [Header("AI")]
        [SerializeField] private AgentAIModel _aiModel = AgentAIModel.None;
        [SerializeField] private AgentTone _tone = AgentTone.None;

        // ── Visual ──────────────────────────────────────────
        [Header("Visual")]
        [Tooltip("에이전트 3D 모델 프리팹 (없으면 큐브 플레이스홀더 사용)")]
        [SerializeField] private GameObject _modelPrefab;
        [SerializeField] private Color _hudColor = new(0.31f, 0.55f, 1f, 1f); // #509CFF

        // ── Session ─────────────────────────────────────────
        [Header("Session")]
        [SerializeField] private string _sessionId = "";
        [Tooltip("미들웨어 에이전트 ID (researcher/writer/analyst)")]
        [SerializeField] private string _agentId = "";

        // ── Properties ──────────────────────────────────────
        public string AgentName       => _agentName;
        public AgentRole Role         => _role;
        public AgentAIModel AIModel   => _aiModel;
        public AgentTone Tone         => _tone;
        public GameObject ModelPrefab => _modelPrefab;
        public Color HudColor         => _hudColor;
        public string SessionId       => _sessionId;
        /// <summary>미들웨어 에이전트 ID (researcher/writer/analyst)</summary>
        public string AgentId         => _agentId;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(_agentName) &&
            _role != AgentRole.None &&
            _aiModel != AgentAIModel.None;

        // ── 런타임 팩토리 ───────────────────────────────────

        /// <summary>
        /// AgentCreationData + 기본 프리팹으로 런타임 SO 인스턴스 생성.
        /// Asset이 아닌 메모리상 인스턴스 — DontDestroyOnLoad 필요 시 별도 처리.
        /// </summary>
        public static AgentProfileSO CreateFromData(
            AgentCreationData data,
            GameObject defaultModelPrefab = null)
        {
            var so = CreateInstance<AgentProfileSO>();
            so.name = $"AgentProfile_{data.AgentName}";
            so._agentName = data.AgentName;
            so._role = data.Role;
            so._aiModel = data.AIModel;
            so._tone = data.Tone;
            so._modelPrefab = defaultModelPrefab;
            so._hudColor = GetDefaultHudColor(data.Role);
            so._sessionId = $"agent_{System.Guid.NewGuid():N}".Substring(0, 16);
            return so;
        }

        /// <summary>역할별 기본 HUD 색상</summary>
        public static Color GetDefaultHudColor(AgentRole role)
        {
            return role switch
            {
                AgentRole.Planning    => new Color32(255, 183, 77,  255), // 주황
                AgentRole.Development => new Color32(100, 181, 246, 255), // 파랑
                AgentRole.Design      => new Color32(206, 147, 216, 255), // 보라
                AgentRole.Legal       => new Color32(161, 136, 127, 255), // 갈색
                AgentRole.Marketing   => new Color32(255, 138, 101, 255), // 코랄
                AgentRole.Research    => new Color32(77,  182, 172, 255), // 민트
                AgentRole.Support     => new Color32(129, 199, 132, 255), // 녹색
                AgentRole.Finance     => new Color32(255, 213, 79,  255), // 노랑
                _                     => new Color32(80,  140, 255, 255), // 기본 파란
            };
        }
    }
}
