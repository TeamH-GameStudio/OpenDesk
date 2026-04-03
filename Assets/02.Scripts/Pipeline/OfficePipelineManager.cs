using System.Text;
using OpenDesk.SkillDiskette;
using UnityEngine;

namespace OpenDesk.Pipeline
{
    /// <summary>
    /// 오피스 파이프라인 중앙 관리자.
    /// In-box 파일 + 에이전트 Equipment → 통합 system prompt 빌드.
    /// Out-box로 결과 라우팅.
    /// </summary>
    public class OfficePipelineManager : MonoBehaviour
    {
        [SerializeField] private InboxController _inbox;
        [SerializeField] private OutboxController _outbox;

        public InboxController Inbox => _inbox;
        public OutboxController Outbox => _outbox;

        /// <summary>
        /// Equipment + In-box 파일 컨텍스트를 합친 최종 system prompt.
        /// ChatPanelController에서 매 메시지마다 호출.
        /// </summary>
        public string BuildFullSystemPrompt(AgentEquipmentManager equipment)
        {
            var sb = new StringBuilder();

            // 1. 에이전트 프로필 + 디스켓 prompt
            var equipPrompt = equipment.BuildSystemPrompt();
            if (!string.IsNullOrEmpty(equipPrompt))
                sb.Append(equipPrompt);

            // 2. In-box 파일 컨텍스트
            if (_inbox != null)
            {
                var fileContext = _inbox.BuildFileContext();
                if (!string.IsNullOrEmpty(fileContext))
                {
                    sb.AppendLine();
                    sb.AppendLine("사용자가 다음 파일을 첨부했습니다. 요청 시 이 파일의 내용을 참고하세요:");
                    sb.Append(fileContext);
                }
            }

            return sb.ToString();
        }
    }
}
