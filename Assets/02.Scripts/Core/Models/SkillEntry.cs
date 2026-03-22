namespace OpenDesk.Core.Models
{
    /// <summary>ClawHub/GitHub 스킬 항목</summary>
    public class SkillEntry
    {
        public string Id           { get; set; } = "";
        public string Name         { get; set; } = "";
        public string Description  { get; set; } = "";
        public string Author       { get; set; } = "";
        public string Category     { get; set; } = "";   // "생산성", "개발", "브라우저" 등
        public string Version      { get; set; } = "";
        public string IconUrl      { get; set; } = "";
        public bool   IsInstalled  { get; set; }
        public bool   IsSandboxed  { get; set; } = true; // 기본: 샌드박스 ON
        public string InstallPath  { get; set; } = "";   // ~/.openclaw/skills/{name}/
        public float  Rating       { get; set; }         // 0.0 ~ 5.0
        public int    Downloads    { get; set; }
    }
}
