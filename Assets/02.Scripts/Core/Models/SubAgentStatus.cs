using System;

namespace OpenDesk.Core.Models
{
    public class SubAgentStatus
    {
        public string   Id        { get; set; } = "";
        public string   Label     { get; set; } = "";
        public string   ParentId  { get; set; } = "";   // 어느 에이전트가 spawn했는지
        public bool     IsRunning { get; set; }
        public DateTime StartedAt { get; set; }

        public string DisplayText => $"[{Label}] 작업 중... ⏳";
    }
}
